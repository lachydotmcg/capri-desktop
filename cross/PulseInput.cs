using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // libpulse's DONT_MOVE binds capture to the selected source. If it disappears, fail instead
    // of letting the server silently move recording to another microphone (or a sink monitor).
    internal sealed class PulseInput : IDisposable
    {
        private const string Library = "libpulse.so.0";
        private readonly CancellationTokenSource stopped = new CancellationTokenSource();
        private readonly TaskCompletionSource opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string source;
        internal volatile bool Ready;
        private readonly Action<short[]> heard;
        private readonly Action<string> failed;
        internal PulseInput(string source, Action<short[]> heard, Action<string> failed) { this.source = source; this.heard = heard; this.failed = failed; }
        internal Task Start() { Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default); return opened.Task; }
        private void Pump()
        {
            IntPtr loop = IntPtr.Zero, context = IntPtr.Zero, stream = IntPtr.Zero;
            try
            {
                loop = pa_mainloop_new();
                if (loop == IntPtr.Zero) throw new IOException("Could not open the audio main loop.");
                context = pa_context_new(pa_mainloop_get_api(loop), "Capri microphone");
                if (context == IntPtr.Zero || pa_context_connect(context, IntPtr.Zero, 1, IntPtr.Zero) < 0) throw new IOException("Could not connect to your audio server.");
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (pa_context_get_state(context) != 4)
                {
                    Iterate(loop); CheckContext(context);
                    if (deadline.Elapsed.TotalSeconds > 5) throw new IOException("Your audio server did not respond.");
                }
                SampleSpec format = new SampleSpec { Format = 3, Rate = Ears.Rate, Channels = 1 };
                stream = pa_stream_new(context, "Capri microphone", ref format, IntPtr.Zero);
                BufferAttr buffer = new BufferAttr { MaxLength = Ears.Rate * 2, Target = uint.MaxValue, Prebuffer = uint.MaxValue, Minimum = uint.MaxValue, Fragment = Ears.Chunk * 2 };
                if (stream == IntPtr.Zero || pa_stream_connect_record(stream, source, ref buffer, 0x0200 | 0x2000) < 0) throw new IOException("Can't open your mic. Pick another under Settings > Voice > Microphone.");
                while (pa_stream_get_state(stream) != 2)
                {
                    Iterate(loop); CheckContext(context); CheckStream(stream);
                    if (deadline.Elapsed.TotalSeconds > 5) throw new IOException("Your microphone did not respond.");
                }
                Ready = true; opened.TrySetResult();
                byte[] chunk = new byte[Ears.Chunk * 2]; int filled = 0;
                while (!stopped.IsCancellationRequested)
                {
                    Iterate(loop); CheckContext(context); CheckStream(stream);
                    UIntPtr available = pa_stream_readable_size(stream);
                    if (available == UIntPtr.MaxValue) throw new IOException("The microphone stopped responding.");
                    while (available.ToUInt64() > 0)
                    {
                        IntPtr data; UIntPtr size;
                        if (pa_stream_peek(stream, out data, out size) < 0) throw new IOException("Could not read the microphone.");
                        int count = checked((int)size.ToUInt64()), offset = 0;
                        if (count == 0) break;
                        while (offset < count)
                        {
                            int take = Math.Min(chunk.Length - filled, count - offset);
                            if (data == IntPtr.Zero) Array.Clear(chunk, filled, take);
                            else Marshal.Copy(IntPtr.Add(data, offset), chunk, filled, take);
                            filled += take; offset += take;
                            if (filled == chunk.Length)
                            {
                                short[] samples = new short[Ears.Chunk]; Buffer.BlockCopy(chunk, 0, samples, 0, chunk.Length);
                                if (!stopped.IsCancellationRequested) heard(samples);
                                filled = 0;
                            }
                        }
                        if (pa_stream_drop(stream) < 0) throw new IOException("Could not release the microphone buffer.");
                        available = pa_stream_readable_size(stream);
                        if (available == UIntPtr.MaxValue) throw new IOException("The microphone stopped responding.");
                    }
                }
            }
            catch (OperationCanceledException) { opened.TrySetCanceled(); }
            catch (Exception error)
            {
                if (!opened.TrySetException(error) && !stopped.IsCancellationRequested) failed(Voice.Short(error.Message));
            }
            finally
            {
                Ready = false;
                if (stream != IntPtr.Zero) { pa_stream_disconnect(stream); pa_stream_unref(stream); }
                if (context != IntPtr.Zero) { pa_context_disconnect(context); pa_context_unref(context); }
                if (loop != IntPtr.Zero) pa_mainloop_free(loop);
                opened.TrySetCanceled();
            }
        }
        private void Iterate(IntPtr loop)
        {
            stopped.Token.ThrowIfCancellationRequested();
            int result;
            if (pa_mainloop_iterate(loop, 0, out result) < 0) throw new IOException("Your audio connection closed.");
            // Dedicated capture thread, not Avalonia's UI; cancellation bounds teardown to this interval.
            if (stopped.Token.WaitHandle.WaitOne(5)) stopped.Token.ThrowIfCancellationRequested();
        }
        private static void CheckContext(IntPtr context) { if (pa_context_get_state(context) >= 5) throw new IOException("Your audio server disconnected. Press TALK to reconnect."); }
        private static void CheckStream(IntPtr stream) { if (pa_stream_get_state(stream) >= 3) throw new IOException("Your microphone disconnected. Select a mic and press TALK again."); }
        public void Dispose() { stopped.Cancel(); }
        [StructLayout(LayoutKind.Sequential)] private struct SampleSpec { internal int Format; internal uint Rate; internal byte Channels; }
        [StructLayout(LayoutKind.Sequential)] private struct BufferAttr { internal uint MaxLength, Target, Prebuffer, Minimum, Fragment; }
        [DllImport(Library)] private static extern IntPtr pa_mainloop_new();
        [DllImport(Library)] private static extern void pa_mainloop_free(IntPtr loop);
        [DllImport(Library)] private static extern IntPtr pa_mainloop_get_api(IntPtr loop);
        [DllImport(Library)] private static extern int pa_mainloop_iterate(IntPtr loop, int block, out int result);
        [DllImport(Library)] private static extern IntPtr pa_context_new(IntPtr api, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Library)] private static extern int pa_context_connect(IntPtr context, IntPtr server, int flags, IntPtr spawn);
        [DllImport(Library)] private static extern int pa_context_get_state(IntPtr context);
        [DllImport(Library)] private static extern void pa_context_disconnect(IntPtr context);
        [DllImport(Library)] private static extern void pa_context_unref(IntPtr context);
        [DllImport(Library)] private static extern IntPtr pa_stream_new(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref SampleSpec format, IntPtr map);
        [DllImport(Library)] private static extern int pa_stream_connect_record(IntPtr stream, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, ref BufferAttr buffer, int flags);
        [DllImport(Library)] private static extern int pa_stream_get_state(IntPtr stream);
        [DllImport(Library)] private static extern UIntPtr pa_stream_readable_size(IntPtr stream);
        [DllImport(Library)] private static extern int pa_stream_peek(IntPtr stream, out IntPtr data, out UIntPtr size);
        [DllImport(Library)] private static extern int pa_stream_drop(IntPtr stream);
        [DllImport(Library)] private static extern int pa_stream_disconnect(IntPtr stream);
        [DllImport(Library)] private static extern void pa_stream_unref(IntPtr stream);
    }
}
