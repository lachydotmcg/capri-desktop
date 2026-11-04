using System;
using System.Diagnostics;
using System.Threading;
using Tmds.DBus.Protocol;

namespace CapriDesktop
{
    internal sealed class Awake : IDisposable
    {
        private readonly object gate = new object();
        private readonly bool mac;
        private bool wanted, starting, closed;
        private DateTime retry;
        private IDisposable held, pending;
        internal Func<string, string[], IDisposable> Run;
        internal Func<IDisposable> Inhibit = ScreenSaver.Open;
        internal Action<Action> Queue = action => ThreadPool.QueueUserWorkItem(_ => action());

        internal Awake(bool mac) { this.mac = mac; Run = StartProcess; }

        internal void Set(bool on)
        {
            IDisposable old = null;
            IDisposable waiting = null;
            bool start = false;
            lock (gate)
            {
                if (closed) return;
                if (!on) retry = DateTime.MinValue;
                wanted = on;
                if (held is Inhibitor process && !process.Live) { old = held; held = null; }
                if (!on) { old = held ?? old; held = null; waiting = pending; pending = null; }
                else if (held == null && !starting && DateTime.UtcNow >= retry) { starting = true; start = true; }
            }
            old?.Dispose();
            if (waiting != old) waiting?.Dispose();
            if (start) Queue(Start);
        }

        private void Start()
        {
            IDisposable next = null;
            try
            {
                lock (gate) { if (closed || !wanted) { starting = false; return; } }
                next = mac ? Run("caffeinate", new[] { "-dims", "-w", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }) :
                    Run("systemd-inhibit", new[] { "--what=idle:sleep", "--who=Capri", "--why=Capri is doing your job", "--mode=block", "sleep", "infinity" });
                bool fallback;
                lock (gate) fallback = next == null && !mac && wanted && !closed;
                if (fallback) next = Inhibit();
            }
            catch (Exception) { }
            lock (gate)
            {
                starting = false;
                pending = null;
                if (!closed && wanted && next != null) { held = next; next = null; }
                else if (wanted) retry = DateTime.UtcNow.AddSeconds(30);
            }
            next?.Dispose();
        }

        public void Dispose()
        {
            IDisposable old, waiting;
            lock (gate) { closed = true; wanted = false; old = held; held = null; waiting = pending; pending = null; }
            old?.Dispose();
            if (waiting != old) waiting?.Dispose();
        }

        private IDisposable StartProcess(string name, string[] args)
        {
            Process process = null;
            Inhibitor inhibitor = null;
            try
            {
                var start = new ProcessStartInfo(name) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (string arg in args) start.ArgumentList.Add(arg);
                process = Process.Start(start);
                if (process == null) return null;
                inhibitor = new Inhibitor(process);
                bool cancel;
                lock (gate) { cancel = closed || !wanted; if (!cancel) pending = inhibitor; }
                if (cancel || process.WaitForExit(150)) { inhibitor.Dispose(); return null; }
                return inhibitor;
            }
            catch (Exception) { if (inhibitor != null) inhibitor.Dispose(); else process?.Dispose(); return null; }
        }

        private sealed class Inhibitor : IDisposable
        {
            private Process process;
            internal Inhibitor(Process process) { this.process = process; }
            internal bool Live { get { try { return process != null && !process.HasExited; } catch (Exception) { return false; } } }
            public void Dispose()
            {
                Process old = Interlocked.Exchange(ref process, null);
                if (old == null) return;
                try { if (!old.HasExited) old.Kill(true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally { old.Dispose(); }
            }
        }
    }

    internal sealed class ScreenSaver : IDisposable
    {
        private Connection connection;
        private readonly string path;
        private uint cookie;

        private ScreenSaver(Connection connection, string path) { this.connection = connection; this.path = path; }

        internal static IDisposable Open()
        {
            Connection connection = null;
            try
            {
                string address = Tmds.DBus.Protocol.Address.Session;
                if (string.IsNullOrEmpty(address)) return null;
                connection = new Connection(address);
                connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                foreach (string path in new[] { "/org/freedesktop/ScreenSaver", "/ScreenSaver" })
                {
                    var held = new ScreenSaver(connection, path);
                    try
                    {
                        held.cookie = connection.CallMethodAsync(held.Message("Inhibit"), (message, _) => message.GetBodyReader().ReadUInt32()).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        connection = null;
                        return held;
                    }
                    catch (DBusException) { }
                }
            }
            catch (Exception) { }
            finally { connection?.Dispose(); }
            return null;
        }

        private MessageBuffer Message(string member)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.freedesktop.ScreenSaver", path: path, @interface: "org.freedesktop.ScreenSaver", member: member, signature: member == "Inhibit" ? "ss" : "u");
            if (member == "Inhibit") { writer.WriteString("Capri"); writer.WriteString("Capri is doing your job"); }
            else writer.WriteUInt32(cookie);
            return writer.CreateMessage();
        }

        public void Dispose()
        {
            Connection old = connection;
            if (old == null) return;
            try { old.CallMethodAsync(Message("UnInhibit")).WaitAsync(TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult(); }
            catch (Exception) { }
            finally { connection = null; old.Dispose(); }
        }
    }

    internal sealed partial class Pet
    {
        internal Awake Wakeful;
        private readonly object workGate = new object();
        private int workCount;
        internal bool KeepAwake { get { return !Saved.NoKeepAwake; } }
        internal bool JobActive { get { return Volatile.Read(ref workCount) > 0 || Security.Holding != null || glancing; } }

        private void StartAwake()
        {
            Wakeful = new Awake(OperatingSystem.IsMacOS());
            if (Program.Smoke != null) { Wakeful.Run = (_, _) => null; Wakeful.Inhibit = () => null; Wakeful.Queue = run => run(); }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAwake();
            desktop.Exit += (_, _) => StopAwake();
            Closed += (_, _) => StopAwake();
            Func<string, ToolArguments, string> act = Brain.Act;
            Brain.Act = (tool, args) =>
            {
                BeginWork();
                try { return act(tool, args); }
                finally { EndWork(); }
            };
        }

        internal void BeginWork() { lock (workGate) { workCount++; CheckKeepAwake(); } }
        internal void EndWork() { lock (workGate) { workCount = Math.Max(0, workCount - 1); CheckKeepAwake(); } }
        internal void SetKeepAwake(bool on) { Saved.NoKeepAwake = !on; CheckKeepAwake(); Save(); }
        internal void CheckKeepAwake() { lock (workGate) Wakeful?.Set(KeepAwake && !quitting && JobActive); }
        internal void StopAwake() { lock (workGate) Wakeful?.Dispose(); }
    }
}
