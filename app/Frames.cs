using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class FrameGrab : IDisposable
    {
        private const int AccessLost = unchecked((int)0x887A0026);
        private const int WaitTimeout = unchecked((int)0x887A0027);
        private readonly object gate = new object();
        private readonly Func<long> clock;
        private readonly Func<bool> fullScreen;
        private readonly Action openDisplay;
        private readonly Func<bool> copyScreen;
        private Rectangle wanted, area, desktop;
        private IntPtr output, device, context, duplication, staging;
        private OutputDesc outputDesc;
        private TextureDesc textureDesc;
        private Box crop;
        private MapTexture map;
        private UnmapTexture unmap;
        private CopyTexture copy;
        private AcquireFrame acquire;
        private EndFrame release;
        private DescribeOutput describe;
        private Bitmap fallback;
        private bool opened, ready, disposed;
        private long retryAt, countedAt, settleUntil;
        private int counted;
        private double fps;

        internal FrameGrab(Func<long> time = null, Func<bool> full = null, Action open = null, Func<bool> copy = null)
        {
            clock = time ?? Stopwatch.GetTimestamp;
            fullScreen = full ?? Native.FillsScreen;
            openDisplay = open ?? OpenDuplication;
            copyScreen = copy ?? CopyDesktop;
        }

        internal string Live { get { lock (gate) return !opened ? "Closed" : duplication != IntPtr.Zero ? "DXGI Desktop Duplication" : "Graphics.CopyFromScreen"; } }
        internal double Fps { get { lock (gate) return fps; } }

        internal void Open(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("The frame area is empty.", "bounds");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("FrameGrab");
                wanted = bounds;
                opened = true;
                counted = 0;
                countedAt = clock();
                fps = 0;
                retryAt = settleUntil = 0;
                Reopen();
            }
        }

        private void Reopen()
        {
            CloseDuplication();
            if (fallback != null) { fallback.Dispose(); fallback = null; }
            desktop = Screen.FromRectangle(wanted).Bounds;
            area = CaptureArea(wanted, SystemInformation.VirtualScreen);
            try { if (desktop.Contains(area)) openDisplay(); }
            catch (Exception) { Settle(); }
            retryAt = clock() + Stopwatch.Frequency * 2;
        }

        private void Settle()
        {
            CloseDuplication();
            retryAt = settleUntil = clock() + Stopwatch.Frequency * 3 / 2;
        }

        internal static Rectangle CaptureArea(Rectangle bounds, Rectangle screens) { return Rectangle.Intersect(bounds, screens); }

        private void OpenDuplication()
        {
            IntPtr factory = IntPtr.Zero;
            IntPtr chosenAdapter = IntPtr.Zero;
            IntPtr chosenOutput = IntPtr.Zero;
            try
            {
                Guid factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                Check(CreateDXGIFactory1(ref factoryId, out factory));
                Enumerate adapters = Method<Enumerate>(factory, 12);
                long largest = 0;
                for (int adapterIndex = 0; ; adapterIndex++)
                {
                    IntPtr adapter;
                    if (adapters(factory, adapterIndex, out adapter) < 0) break;
                    try
                    {
                        Enumerate outputs = Method<Enumerate>(adapter, 7);
                        for (int outputIndex = 0; ; outputIndex++)
                        {
                            IntPtr candidate;
                            if (outputs(adapter, outputIndex, out candidate) < 0) break;
                            try
                            {
                                OutputDesc details;
                                Check(Method<DescribeOutput>(candidate, 7)(candidate, out details));
                                Rectangle overlap = Rectangle.Intersect(wanted, details.Bounds);
                                long size = (long)overlap.Width * overlap.Height;
                                if (details.Attached == 0 || size <= largest) continue;
                                Drop(ref chosenAdapter);
                                Drop(ref chosenOutput);
                                Marshal.AddRef(adapter);
                                chosenAdapter = adapter;
                                chosenOutput = candidate;
                                candidate = IntPtr.Zero;
                                outputDesc = details;
                                largest = size;
                            }
                            finally { Drop(ref candidate); }
                        }
                    }
                    finally { Drop(ref adapter); }
                }
                if (chosenOutput == IntPtr.Zero || !outputDesc.Bounds.Contains(area)) return;
                Guid outputId = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
                Check(Marshal.QueryInterface(chosenOutput, ref outputId, out output));
                int feature;
                Check(D3D11CreateDevice(chosenAdapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out feature, out context));
                Check(Method<DuplicateOutput>(output, 22)(output, device, out duplication));
                map = Method<MapTexture>(context, 14);
                unmap = Method<UnmapTexture>(context, 15);
                copy = Method<CopyTexture>(context, 46);
                acquire = Method<AcquireFrame>(duplication, 8);
                release = Method<EndFrame>(duplication, 14);
                describe = Method<DescribeOutput>(output, 7);
                desktop = outputDesc.Bounds;
                area = Rectangle.Intersect(wanted, desktop);
            }
            finally
            {
                Drop(ref chosenOutput);
                Drop(ref chosenAdapter);
                Drop(ref factory);
            }
        }

        internal bool Next(int timeoutMs)
        {
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException("timeoutMs");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("FrameGrab");
                if (!opened) return false;
                // Lost to a mode change or a window taking the screen, it waits for that to settle before it tries again, even by
                // copying the screen: a new device on every frame meanwhile can hang the graphics driver under a game
                // (john_sandevistan, 2 Nov: Valorant crashed as the Mainframe opened).
                long now = clock();
                if (now < settleUntil) return false;
                if (duplication == IntPtr.Zero && now >= retryAt)
                {
                    if (fullScreen()) retryAt = now + Stopwatch.Frequency / 2;
                    else Reopen();
                    if (clock() < settleUntil) return false;
                }
                if (duplication != IntPtr.Zero)
                {
                    try
                    {
                        OutputDesc current;
                        Check(describe(output, out current));
                        if (current.Attached == 0 || current.Bounds != desktop || current.Rotation != outputDesc.Rotation) { Settle(); return false; }
                        if (duplication != IntPtr.Zero)
                        {
                            if (!Take(timeoutMs)) return false;
                            Count();
                            return true;
                        }
                    }
                    catch (COMException)
                    {
                        Settle();
                        return false;
                    }
                }
                if (area.Width <= 0 || area.Height <= 0) return false;
                try
                {
                    if (!copyScreen()) return false;
                    ready = true;
                    Count();
                    return true;
                }
                catch (System.ComponentModel.Win32Exception) { ready = false; return false; }
                catch (ExternalException) { ready = false; return false; }
            }
        }

        private bool CopyDesktop()
        {
            if (fallback == null) fallback = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (Graphics graphics = Graphics.FromImage(fallback)) graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            return true;
        }

        private bool Take(int timeoutMs)
        {
            FrameInfo info;
            IntPtr resource;
            int result = acquire(duplication, timeoutMs, out info, out resource);
            if (result < 0)
            {
                Drop(ref resource);
                if (result == WaitTimeout) return false;
                Check(result);
            }
            IntPtr texture = IntPtr.Zero;
            try
            {
                if (info.LastPresentTime == 0 && ready) return false;
                Guid textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                Check(Marshal.QueryInterface(resource, ref textureId, out texture));
                TextureDesc details;
                Method<DescribeTexture>(texture, 10)(texture, out details);
                if (staging == IntPtr.Zero || details.Width != textureDesc.Width || details.Height != textureDesc.Height || details.Format != textureDesc.Format)
                    Prepare(details);
                copy(context, staging, 0, 0, 0, 0, texture, 0, ref crop);
                ready = true;
                return true;
            }
            finally
            {
                Drop(ref texture);
                Drop(ref resource);
                Check(release(duplication));
            }
        }

        private void Prepare(TextureDesc details)
        {
            bool sideways = outputDesc.Rotation == 2 || outputDesc.Rotation == 4;
            if (details.Format != 87 || details.Width != (sideways ? desktop.Height : desktop.Width) || details.Height != (sideways ? desktop.Width : desktop.Height))
                throw new COMException("The desktop mode changed.", AccessLost);
            int left = area.Left - desktop.Left;
            int top = area.Top - desktop.Top;
            crop = new Box { Left = left, Top = top, Right = left + area.Width, Bottom = top + area.Height, Back = 1 };
            if (outputDesc.Rotation == 2) crop = new Box { Left = top, Top = details.Height - left - area.Width, Right = top + area.Height, Bottom = details.Height - left, Back = 1 };
            else if (outputDesc.Rotation == 3) crop = new Box { Left = details.Width - left - area.Width, Top = details.Height - top - area.Height, Right = details.Width - left, Bottom = details.Height - top, Back = 1 };
            else if (outputDesc.Rotation == 4) crop = new Box { Left = details.Width - top - area.Height, Top = left, Right = details.Width - top, Bottom = left + area.Width, Back = 1 };
            TextureDesc stage = new TextureDesc { Width = crop.Right - crop.Left, Height = crop.Bottom - crop.Top, Mips = 1, ArraySize = 1, Format = details.Format, Samples = 1, Usage = 3, CpuAccess = 0x20000 };
            Drop(ref staging);
            Check(Method<CreateTexture>(device, 5)(device, ref stage, IntPtr.Zero, out staging));
            textureDesc = details;
        }

        private void Count()
        {
            counted++;
            long now = clock();
            double elapsed = (double)(now - countedAt) / Stopwatch.Frequency;
            if (elapsed < 0.5) return;
            fps = counted / elapsed;
            counted = 0;
            countedAt = now;
        }

        private IntPtr Read(out int across, out int down, out BitmapData locked)
        {
            locked = null;
            across = 4;
            if (duplication == IntPtr.Zero)
            {
                locked = fallback.LockBits(new Rectangle(Point.Empty, fallback.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                down = locked.Stride;
                return locked.Scan0;
            }
            Mapped mapped;
            Check(map(context, staging, 0, 1, 0, out mapped));
            down = mapped.RowPitch;
            int stageWidth = crop.Right - crop.Left;
            int stageHeight = crop.Bottom - crop.Top;
            if (outputDesc.Rotation == 2) { across = -mapped.RowPitch; down = 4; return IntPtr.Add(mapped.Data, (stageHeight - 1) * mapped.RowPitch); }
            if (outputDesc.Rotation == 3) { across = -4; down = -mapped.RowPitch; return IntPtr.Add(mapped.Data, (stageHeight - 1) * mapped.RowPitch + (stageWidth - 1) * 4); }
            if (outputDesc.Rotation == 4) { across = mapped.RowPitch; down = -4; return IntPtr.Add(mapped.Data, (stageWidth - 1) * 4); }
            return mapped.Data;
        }

        private void Unread(BitmapData locked)
        {
            if (locked != null) fallback.UnlockBits(locked);
            else unmap(context, staging, 0);
        }

        // A frame wider than maxWidth shrinks straight out of the capture in one pass. It used to be copied at full size, then
        // shrunk again by GDI+: two passes over a 1440p or 4K frame for every clip frame while a game runs (Lachy, 2 Nov: the PC
        // lagged while clipping, and one clip came out at 4 fps).
        internal unsafe Bitmap Copy(int maxWidth)
        {
            if (maxWidth <= 0) throw new ArgumentOutOfRangeException("maxWidth");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("FrameGrab");
                if (!ready) return null;
                int wide = Math.Min(maxWidth, area.Width), tall = wide == area.Width ? area.Height : Math.Max(1, (int)((long)area.Height * wide / area.Width));
                Bitmap picture = new Bitmap(wide, tall, PixelFormat.Format32bppRgb);
                try
                {
                    BitmapData locked;
                    int across, down;
                    IntPtr source = Read(out across, out down, out locked);
                    try
                    {
                        BitmapData target = picture.LockBits(new Rectangle(0, 0, wide, tall), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        try { Shrink((byte*)source, across, down, area.Width, area.Height, target, wide, tall); }
                        finally { picture.UnlockBits(target); }
                    }
                    finally { Unread(locked); }
                    Bitmap done = picture;
                    picture = null;
                    return done;
                }
                catch (COMException) { Settle(); return null; }
                finally { if (picture != null) picture.Dispose(); }
            }
        }

        // Each pixel out is the average of the frame's pixels under it, so text and a game's HUD lines don't shimmer. At the same
        // size it's a plain copy.
        private static unsafe void Shrink(byte* source, int across, int down, int width, int height, BitmapData target, int wide, int tall)
        {
            if (wide == width && tall == height)
            {
                for (int row = 0; row < height; row++)
                {
                    byte* from = source + (long)row * down;
                    uint* to = (uint*)((byte*)target.Scan0 + (long)row * target.Stride);
                    for (int column = 0; column < width; column++, from += across) to[column] = *(uint*)from;
                }
                return;
            }
            int[] lefts = new int[wide + 1];
            for (int column = 0; column <= wide; column++) lefts[column] = (int)((long)column * width / wide);
            for (int row = 0; row < tall; row++)
            {
                int top = (int)((long)row * height / tall), bottom = Math.Max(top + 1, (int)((long)(row + 1) * height / tall));
                uint* to = (uint*)((byte*)target.Scan0 + (long)row * target.Stride);
                for (int column = 0; column < wide; column++)
                {
                    int left = lefts[column], right = Math.Max(left + 1, lefts[column + 1]);
                    uint blue = 0, green = 0, red = 0;
                    for (int y = top; y < bottom; y++)
                    {
                        byte* from = source + (long)y * down + (long)left * across;
                        for (int x = left; x < right; x++, from += across) { blue += from[0]; green += from[1]; red += from[2]; }
                    }
                    uint count = (uint)((bottom - top) * (right - left));
                    to[column] = 0xFF000000u | ((red / count) << 16) | ((green / count) << 8) | (blue / count);
                }
            }
        }

        internal byte[] Gray(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("FrameGrab");
                if (!ready) return null;
                try
                {
                    BitmapData locked;
                    int across, down;
                    IntPtr source = Read(out across, out down, out locked);
                    try { return Downsample(source, area.Width, area.Height, across, down, width, height); }
                    finally { Unread(locked); }
                }
                catch (COMException) { Settle(); return null; }
            }
        }

        internal static unsafe byte[] Downsample(IntPtr source, int sourceWidth, int sourceHeight, int across, int down, int width, int height)
        {
            if (source == IntPtr.Zero || sourceWidth <= 0 || sourceHeight <= 0 || width <= 0 || height <= 0) throw new ArgumentException("The gray image has no pixels.");
            byte[] gray = new byte[checked(width * height)];
            if (across == 4 && sourceWidth == (long)width * 8 && sourceHeight == (long)height * 8)
            {
                Boxes8(source, down, width, height, gray);
                return gray;
            }
            int[] edges = new int[checked(width + 1)];
            for (int edge = 0; edge <= width; edge++) edges[edge] = (int)((long)edge * sourceWidth / width);
            int targetAt = 0;
            for (int grayY = 0; grayY < height; grayY++)
            {
                int top = (int)((long)grayY * sourceHeight / height);
                int bottom = Math.Max(top + 1, (int)((long)(grayY + 1) * sourceHeight / height));
                for (int grayX = 0; grayX < width; grayX++)
                {
                    int left = edges[grayX];
                    int right = Math.Max(left + 1, edges[grayX + 1]);
                    int count = checked((right - left) * (bottom - top));
                    if (across == 4 && count <= 256)
                    {
                        ulong pairsRedBlue = 0, pairsGreen = 0;
                        uint tailRedBlue = 0;
                        int tailGreen = 0;
                        for (int pairY = top; pairY < bottom; pairY++)
                        {
                            uint* pairPixel = (uint*)((byte*)source + pairY * down) + left;
                            int pairX = left;
                            for (; pairX + 3 < right; pairX += 4, pairPixel += 4)
                            {
                                ulong firstPair = *(ulong*)pairPixel;
                                ulong secondPair = *(ulong*)(pairPixel + 2);
                                pairsRedBlue += (firstPair & 0x00ff00ff00ff00ffUL) + (secondPair & 0x00ff00ff00ff00ffUL);
                                pairsGreen += ((firstPair >> 8) & 0x000000ff000000ffUL) + ((secondPair >> 8) & 0x000000ff000000ffUL);
                            }
                            if (pairX + 1 < right)
                            {
                                ulong lastPair = *(ulong*)pairPixel;
                                pairsRedBlue += lastPair & 0x00ff00ff00ff00ffUL;
                                pairsGreen += (lastPair >> 8) & 0x000000ff000000ffUL;
                                pairPixel += 2;
                                pairX += 2;
                            }
                            if (pairX < right) { tailRedBlue += *pairPixel & 0x00ff00ff; tailGreen += (int)((*pairPixel >> 8) & 255); }
                        }
                        int pairRed = (int)((pairsRedBlue >> 16) & 65535) + (int)(pairsRedBlue >> 48) + (int)(tailRedBlue >> 16);
                        int pairBlue = (int)(pairsRedBlue & 65535) + (int)((pairsRedBlue >> 32) & 65535) + (int)(tailRedBlue & 65535);
                        int pairGreen = (int)(pairsGreen & 65535) + (int)(pairsGreen >> 32) + tailGreen;
                        gray[targetAt++] = (byte)((pairRed * 77 + pairGreen * 150 + pairBlue * 29) / (256 * count));
                    }
                    else if (count <= 256)
                    {
                        uint redBlue = 0;
                        int green = 0;
                        for (int fastY = top; fastY < bottom; fastY++)
                        {
                            byte* fastPixel = (byte*)source + fastY * down + left * across;
                            int fastX = left;
                            for (; fastX + 1 < right; fastX += 2, fastPixel += across * 2)
                            {
                                redBlue += (*(uint*)fastPixel & 0x00ff00ff) + (*(uint*)(fastPixel + across) & 0x00ff00ff);
                                green += fastPixel[1] + fastPixel[across + 1];
                            }
                            if (fastX < right) { redBlue += *(uint*)fastPixel & 0x00ff00ff; green += fastPixel[1]; }
                        }
                        gray[targetAt++] = (byte)(((int)(redBlue >> 16) * 77 + green * 150 + (int)(redBlue & 65535) * 29) / (256 * count));
                    }
                    else
                    {
                        long red = 0, slowGreen = 0, blue = 0;
                        for (int sourceY = top; sourceY < bottom; sourceY++)
                        {
                            byte* pixel = (byte*)source + sourceY * down + left * across;
                            for (int sourceX = left; sourceX < right; sourceX++, pixel += across)
                            {
                                blue += pixel[0];
                                slowGreen += pixel[1];
                                red += pixel[2];
                            }
                        }
                        gray[targetAt++] = (byte)((red * 77 + slowGreen * 150 + blue * 29) / (256L * count));
                    }
                }
            }
            return gray;
        }

        private static unsafe void Boxes8(IntPtr source, int stride, int width, int height, byte[] gray)
        {
            const ulong redBlueMask = 0x00ff00ff00ff00ffUL;
            const ulong greenMask = 0x000000ff000000ffUL;
            fixed (byte* target = gray)
            {
                byte* destination = target;
                for (int boxY = 0; boxY < height; boxY++)
                {
                    byte* band = (byte*)source + boxY * stride * 8;
                    for (int boxX = 0; boxX < width; boxX++)
                    {
                        byte* row = band + boxX * 32;
                        ulong redBlue = 0, green = 0;
                        for (int boxRow = 0; boxRow < 8; boxRow++, row += stride)
                        {
                            ulong first = ((ulong*)row)[0];
                            ulong second = ((ulong*)row)[1];
                            ulong third = ((ulong*)row)[2];
                            ulong fourth = ((ulong*)row)[3];
                            redBlue += (first & redBlueMask) + (second & redBlueMask) + (third & redBlueMask) + (fourth & redBlueMask);
                            green += ((first >> 8) & greenMask) + ((second >> 8) & greenMask) + ((third >> 8) & greenMask) + ((fourth >> 8) & greenMask);
                        }
                        int sumRed = (int)((redBlue >> 16) & 65535) + (int)(redBlue >> 48);
                        int sumBlue = (int)(redBlue & 65535) + (int)((redBlue >> 32) & 65535);
                        int sumGreen = (int)(green & 65535) + (int)(green >> 32);
                        *destination++ = (byte)((sumRed * 77 + sumGreen * 150 + sumBlue * 29) >> 14);
                    }
                }
            }
        }

        private void CloseDuplication()
        {
            ready = false;
            Drop(ref staging);
            Drop(ref duplication);
            Drop(ref context);
            Drop(ref device);
            Drop(ref output);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                opened = false;
                CloseDuplication();
                if (fallback != null) { fallback.Dispose(); fallback = null; }
            }
        }

        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Drop(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }
        private static T Method<T>(IntPtr instance, int slot) where T : class { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T)); }

        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, int flags, IntPtr levels, int levelCount, int sdk, out IntPtr device, out int feature, out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr instance, int index, out IntPtr found);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DescribeOutput(IntPtr instance, out OutputDesc description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int DuplicateOutput(IntPtr instance, IntPtr device, out IntPtr duplicate);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AcquireFrame(IntPtr instance, int timeout, out FrameInfo info, out IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EndFrame(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DescribeTexture(IntPtr instance, out TextureDesc description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr instance, ref TextureDesc description, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyTexture(IntPtr instance, IntPtr target, int targetIndex, int x, int y, int z, IntPtr source, int sourceIndex, ref Box box);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapTexture(IntPtr instance, IntPtr texture, int index, int kind, int flags, out Mapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapTexture(IntPtr instance, IntPtr texture, int index);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OutputDesc
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
            internal int Left, Top, Right, Bottom, Attached, Rotation;
            internal IntPtr Monitor;
            internal Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDesc
        {
            internal int Width, Height, Mips, ArraySize, Format, Samples, Quality, Usage, Bind, CpuAccess, Misc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Box { internal int Left, Top, Front, Right, Bottom, Back; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Mapped { internal IntPtr Data; internal int RowPitch, DepthPitch; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FrameInfo
        {
            internal long LastPresentTime, LastMouseUpdateTime;
            internal int AccumulatedFrames, RectsCoalesced, ProtectedContentMaskedOut, PointerX, PointerY, PointerVisible, MetadataSize, PointerShapeSize;
        }
    }

    internal static class Motion
    {
        internal static double Changed(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) throw new ArgumentException("The gray frames must have the same size.");
            int changed = 0;
            for (int index = 0; index < a.Length; index++) if (a[index] != b[index]) changed++;
            return a.Length == 0 ? 0 : (double)changed / a.Length;
        }

        internal static bool Shift(byte[] a, byte[] b, int width, int height, int reach, out double dx, out double dy, out double match)
        {
            dx = 0;
            dy = 0;
            match = 0;
            if (a == null || b == null || width <= 0 || height <= 0 || (long)width * height != a.Length || a.Length != b.Length || reach < 0)
                throw new ArgumentException("The gray frames and their dimensions must agree.");
            if (width < 8 || height < 8) return false;
            int radius = Math.Min(reach, Math.Min((width - 4) / 2, (height - 4) / 2));
            int stepX = Math.Max(1, (width - radius * 2) / 32);
            int stepY = Math.Max(1, (height - radius * 2) / 24);
            double sum = 0, square = 0;
            int samples = 0;
            for (int sampleY = radius; sampleY < height - radius; sampleY += stepY)
                for (int sampleX = radius; sampleX < width - radius; sampleX += stepX)
                {
                    double value = a[sampleY * width + sampleX];
                    sum += value;
                    square += value * value;
                    samples++;
                }
            double variance = square / samples - sum * sum / ((double)samples * samples);
            if (variance < 4) return false;
            int bestX = 0, bestY = 0;
            double best = double.MaxValue;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    double error = Error(a, b, width, height, radius, stepX, stepY, offsetX, offsetY);
                    if (error > best || (error == best && offsetX * offsetX + offsetY * offsetY >= bestX * bestX + bestY * bestY)) continue;
                    best = error;
                    bestX = offsetX;
                    bestY = offsetY;
                }
            double nextSum = 0, nextSquare = 0;
            for (int matchY = radius; matchY < height - radius; matchY += stepY)
                for (int matchX = radius; matchX < width - radius; matchX += stepX)
                {
                    double nextValue = b[(matchY + bestY) * width + matchX + bestX];
                    nextSum += nextValue;
                    nextSquare += nextValue * nextValue;
                }
            double nextVariance = nextSquare / samples - nextSum * nextSum / ((double)samples * samples);
            if (nextVariance < 4) return false;
            double covariance = (square + nextSquare - best) / (2 * samples) - sum * nextSum / ((double)samples * samples);
            match = Math.Max(0, Math.Min(1, covariance / Math.Sqrt(variance * nextVariance)));
            if (match < 0.5) return false;
            dx = bestX;
            dy = bestY;
            if (Math.Abs(bestX) < radius) dx += Refine(Error(a, b, width, height, radius, stepX, stepY, bestX - 1, bestY), best, Error(a, b, width, height, radius, stepX, stepY, bestX + 1, bestY));
            if (Math.Abs(bestY) < radius) dy += Refine(Error(a, b, width, height, radius, stepX, stepY, bestX, bestY - 1), best, Error(a, b, width, height, radius, stepX, stepY, bestX, bestY + 1));
            return true;
        }

        private static double Error(byte[] a, byte[] b, int width, int height, int radius, int stepX, int stepY, int dx, int dy)
        {
            double error = 0;
            for (int row = radius; row < height - radius; row += stepY)
            {
                int first = row * width;
                int second = (row + dy) * width + dx;
                for (int column = radius; column < width - radius; column += stepX)
                {
                    int difference = a[first + column] - b[second + column];
                    error += difference * difference;
                }
            }
            return error;
        }

        private static double Refine(double before, double middle, double after)
        {
            double curve = before - 2 * middle + after;
            return curve <= 0 ? 0 : Math.Max(-0.5, Math.Min(0.5, (before - after) / (2 * curve)));
        }
    }
}
