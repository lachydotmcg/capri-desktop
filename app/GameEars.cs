using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class GameEars : IDisposable
    {
        private readonly int pid;
        private readonly object gate = new object();
        private readonly double[] recent = new double[61];
        private readonly double[] sorted = new double[61];
        private Thread worker;
        private ManualResetEvent stop;
        private bool disposed;
        private volatile bool available;
        private int samples, filled, next;
        private double level, previous, lastLoud = double.NegativeInfinity;
        private bool armed = true;

        internal GameEars(int processId)
        {
            if (processId <= 0) throw new ArgumentOutOfRangeException("processId");
            pid = processId;
        }

        internal double Level { get { return Interlocked.CompareExchange(ref level, 0, 0); } }
        internal bool Listening { get { lock (gate) return worker != null && worker.IsAlive; } }
        internal bool Available { get { return available; } }
        internal int Samples { get { return Interlocked.CompareExchange(ref samples, 0, 0); } }
        internal event Action<double> Loud;

        internal void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("GameEars");
                if (worker != null) return;
                filled = 0;
                next = 0;
                previous = 0;
                lastLoud = double.NegativeInfinity;
                armed = true;
                available = false;
                Interlocked.Exchange(ref level, 0);
                Interlocked.Exchange(ref samples, 0);
                stop = new ManualResetEvent(false);
                worker = new Thread(Pump);
                worker.Name = "Capri game levels";
                worker.IsBackground = true;
                worker.SetApartmentState(ApartmentState.MTA);
                try { worker.Start(); }
                catch { worker = null; stop.Dispose(); stop = null; throw; }
            }
        }

        internal void Stop()
        {
            Thread ending;
            lock (gate)
            {
                ending = worker;
                if (stop != null) stop.Set();
            }
            if (ending != null && ending != Thread.CurrentThread) ending.Join();
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
            Stop();
        }

        private void Pump()
        {
            List<Session> sessions = new List<Session>();
            bool initialized = false;
            try
            {
                initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
                double refreshAt = 0;
                while (!stop.WaitOne(0))
                {
                    double tick = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                    try
                    {
                        if (initialized && tick >= refreshAt)
                        {
                            refreshAt = tick + 2;
                            available = Refresh(sessions);
                        }
                        double peak = 0;
                        for (int sessionIndex = sessions.Count - 1; sessionIndex >= 0; sessionIndex--)
                        {
                            float value;
                            bool read = false;
                            try { read = sessions[sessionIndex].Meter.GetPeakValue(out value) >= 0; }
                            catch (Exception) { value = 0; }
                            if (read) { if (!float.IsNaN(value) && value > peak) peak = value; }
                            else { Drop(sessions[sessionIndex].Owner); sessions.RemoveAt(sessionIndex); }
                        }
                        Hear(peak, tick);
                    }
                    catch (Exception) { Interlocked.Exchange(ref level, 0); available = false; }
                    Interlocked.Increment(ref samples);
                    double elapsed = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency - tick;
                    if (stop.WaitOne(Math.Max(1, (int)Math.Ceiling(1000.0 / 30 - elapsed * 1000)))) break;
                }
            }
            catch (Exception) { }
            finally
            {
                Clear(sessions);
                if (initialized) { try { CoUninitialize(); } catch (Exception) { } }
                Interlocked.Exchange(ref level, 0);
                lock (gate)
                {
                    try { stop.Dispose(); } catch (Exception) { }
                    stop = null;
                    worker = null;
                }
            }
        }

        internal void Hear(double value, double at)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) value = 0;
            Interlocked.Exchange(ref level, value);
            Array.Copy(recent, sorted, filled);
            Array.Sort(sorted, 0, filled);
            double median = filled == 0 ? 0 : filled % 2 == 0 ? (sorted[filled / 2 - 1] + sorted[filled / 2]) * 0.5 : sorted[filled / 2];
            double threshold = Math.Max(0.04, median * 3 + 0.015);
            if (value < threshold * 0.6) armed = true;
            bool spike = filled >= 6 && armed && value > threshold && value > previous * 1.3 + 0.01 && at - lastLoud >= 0.18;
            if (spike) { armed = false; lastLoud = at; }
            previous = value;
            recent[next] = value;
            next = (next + 1) % recent.Length;
            if (filled < recent.Length) filled++;
            if (!spike) return;
            Action<double> loud = Loud;
            if (loud == null) return;
            foreach (Action<double> handler in loud.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception) { }
            }
        }

        private bool Refresh(List<Session> sessions)
        {
            HashSet<int> family = Family(pid, Parents());
            List<Session> found = new List<Session>();
            ISoundDevices devices = null;
            IGameDevices collection = null;
            IntPtr collectionPointer = IntPtr.Zero;
            bool opened = false;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                if (devices.EnumAudioEndpoints(0, 1, out collectionPointer) < 0 || collectionPointer == IntPtr.Zero) return false;
                collection = (IGameDevices)Marshal.GetObjectForIUnknown(collectionPointer);
                int deviceCount;
                if (collection.GetCount(out deviceCount) < 0) return false;
                for (int deviceIndex = 0; deviceIndex < deviceCount && !stop.WaitOne(0); deviceIndex++)
                {
                    ISoundDevice endpoint = null;
                    object manager = null;
                    IGameSessions entries = null;
                    try
                    {
                        if (collection.Item(deviceIndex, out endpoint) < 0 || endpoint == null) continue;
                        Guid managerId = typeof(IGameSessionManager).GUID;
                        if (endpoint.Activate(ref managerId, 23, IntPtr.Zero, out manager) < 0 || manager == null) continue;
                        if (((IGameSessionManager)manager).GetSessionEnumerator(out entries) < 0 || entries == null) continue;
                        int sessionCount;
                        if (entries.GetCount(out sessionCount) < 0) continue;
                        opened = true;
                        for (int entryIndex = 0; entryIndex < sessionCount && !stop.WaitOne(0); entryIndex++)
                        {
                            object control = null;
                            try
                            {
                                if (entries.GetSession(entryIndex, out control) < 0 || control == null) continue;
                                IGameSession session = control as IGameSession;
                                int processId, state;
                                if (session == null || session.GetProcessId(out processId) < 0 || !family.Contains(processId) || session.GetState(out state) < 0 || state == 2) continue;
                                IGameMeter meter = control as IGameMeter;
                                if (meter == null) continue;
                                found.Add(new Session { Owner = control, Meter = meter });
                                control = null;
                            }
                            catch (Exception) { }
                            finally { Drop(control); }
                        }
                    }
                    catch (Exception) { }
                    finally { Drop(entries); Drop(manager); Drop(endpoint); }
                }
                Clear(sessions);
                sessions.AddRange(found);
                found.Clear();
                return opened;
            }
            finally
            {
                Clear(found);
                Drop(collection);
                if (collectionPointer != IntPtr.Zero) { try { Marshal.Release(collectionPointer); } catch (Exception) { } }
                Drop(devices);
            }
        }

        internal static HashSet<int> Family(int processId, Dictionary<int, int> parents)
        {
            HashSet<int> family = new HashSet<int>();
            family.Add(processId);
            bool added;
            do
            {
                added = false;
                foreach (KeyValuePair<int, int> parent in parents)
                    if (parent.Key > 0 && family.Contains(parent.Value) && family.Add(parent.Key)) added = true;
            }
            while (added);
            return family;
        }

        private static Dictionary<int, int> Parents()
        {
            Dictionary<int, int> parents = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return parents;
            try
            {
                ProcessEntry entry = new ProcessEntry { Size = Marshal.SizeOf(typeof(ProcessEntry)) };
                if (Process32FirstW(snapshot, ref entry))
                    do { parents[entry.Id] = entry.Parent; }
                    while (Process32NextW(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            return parents;
        }

        private static void Clear(List<Session> sessions)
        {
            foreach (Session session in sessions) Drop(session.Owner);
            sessions.Clear();
        }

        private static void Drop(object value)
        {
            if (value == null) return;
            try { Marshal.ReleaseComObject(value); }
            catch (Exception) { }
        }

        private sealed class Session
        {
            internal object Owner;
            internal IGameMeter Meter;
        }

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, int kind);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(int flags, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal int Size, Usage, Id;
            internal UIntPtr Heap;
            internal int Module, Threads, Parent, Priority, Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string File;
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGameDevices
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out ISoundDevice device);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGameSessionManager
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr session, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr session, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IGameSessions sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGameSessions
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGameSession
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr identifier);
            [PreserveSig] int GetProcessId(out int processId);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGameMeter
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }
    }
}
