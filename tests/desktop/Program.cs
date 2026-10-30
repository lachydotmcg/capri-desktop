using System.Runtime.CompilerServices;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Platform;

namespace CapriDesktop
{
    // Match the existing shared Hands contract; do not load models, audio or a UI.
    internal sealed class OpenWindow
    {
        internal readonly IntPtr Handle;
        internal readonly string Title, Program;
        internal OpenWindow(IntPtr handle, string title, string program) { Handle = handle; Title = title; Program = program; }
    }

    internal static class DesktopTests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual);
        private static void Throws(Action action, string message)
        {
            try { action(); }
            catch (Exception) { checks++; return; }
            throw new Exception(message);
        }

        private static JsonObject Output(int id = 0, string name = "DP-1", int x = 0, int y = 0, double scale = 1, int transform = 0) => new JsonObject
        {
            ["id"] = id, ["name"] = name, ["x"] = x, ["y"] = y, ["width"] = 1920, ["height"] = 1080,
            ["scale"] = scale, ["transform"] = transform, ["activeWorkspace"] = new JsonObject { ["id"] = 1, ["name"] = "1" },
            ["specialWorkspace"] = new JsonObject { ["id"] = 0, ["name"] = "" }, ["reserved"] = new JsonArray(0, 30, 0, 0), ["focused"] = id == 0
        };
        private static JsonObject Window(string address = "0x123", int pid = 43210, int workspace = 1) => new JsonObject
        {
            ["address"] = address, ["pid"] = pid, ["mapped"] = true, ["hidden"] = false,
            ["class"] = "fixture", ["title"] = "Disposable synthetic window", ["at"] = new JsonArray(100, 200), ["size"] = new JsonArray(800, 600),
            ["monitor"] = 0, ["workspace"] = new JsonObject { ["id"] = workspace, ["name"] = workspace.ToString() },
            ["floating"] = false, ["fullscreen"] = 0, ["focusHistoryID"] = 0, ["grouped"] = new JsonArray()
        };

        private sealed class Fake : IHyprlandTransport
        {
            internal string Session = "fixture-session";
            public string SessionId => Session;
            internal JsonArray Outputs = new JsonArray(Output());
            internal JsonArray Windows = new JsonArray(Window());
            internal long Active = 0x123;
            internal Point Pointer = new Point(20, 20);
            internal bool Locked, Effect = true, Rejected;
            internal string Dialect = "ok";
            internal List<string> Dispatches = new List<string>();
            internal Action<string> Before;
            internal Action<string> AfterDispatch;
            public Task<string> RequestAsync(string command, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Before?.Invoke(command);
                if (command.StartsWith("/dispatch ", StringComparison.Ordinal))
                {
                    Dispatches.Add(command);
                    if (Rejected) return Task.FromResult("invalid dispatcher");
                    if (Effect)
                    {
                        if (command.Contains("window.close") || command.Contains("closewindow")) Windows.Clear();
                        if (command.Contains("special:minimized")) Windows[0]["workspace"] = new JsonObject { ["id"] = -99, ["name"] = "special:minimized" };
                        else if (command.Contains("window.move")) Windows[0]["workspace"] = new JsonObject { ["id"] = 1, ["name"] = "1" };
                        if (command.Contains("dsp.focus") || command.Contains("focuswindow")) Active = 0x123;
                        if (command.Contains("cursor.move")) Pointer = new Point(100, 120);
                    }
                    AfterDispatch?.Invoke(command);
                    return Task.FromResult("ok");
                }
                return Task.FromResult(command switch
                {
                    "j/monitors" => Outputs.ToJsonString(), "j/clients" => Windows.ToJsonString(),
                    "j/activewindow" => Active == 0 ? "{}" : "{\"address\":\"0x" + Active.ToString("x") + "\"}",
                    "j/cursorpos" => JsonSerializer.Serialize(new { x = Pointer.X, y = Pointer.Y }),
                    "j/locked" => JsonSerializer.Serialize(new { locked = Locked }),
                    "/eval return type(hl.dsp.focus)" => Dialect,
                    _ => throw new Exception("Unexpected query: " + command)
                });
            }
            public async IAsyncEnumerable<string> Events([EnumeratorCancellation] CancellationToken token)
            {
                await Task.Delay(Timeout.Infinite, token);
                yield break;
            }
        }

        private static HyprlandDesktop Service(Fake fake, Func<int, string> identity = null, Func<long> clock = null) =>
            new HyprlandDesktop(fake, false, identity ?? (pid => pid + ":fixture-start:owner"), clock ?? (() => 1000), (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        private static void Geometry()
        {
            for (int rotation = 0; rotation < 8; rotation++)
            {
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    HyprlandOutput output = HyprlandJson.Outputs(new JsonArray(Output(x: -1500, y: -200, scale: scale, transform: rotation)).ToJsonString())[0];
                    Equal((rotation % 2 == 0 ? 1920 : 1080) / scale, output.Logical.Width, "Oriented logical width");
                    Equal((rotation % 2 == 0 ? 1080 : 1920) / scale, output.Logical.Height, "Oriented logical height");
                    PixelRect pixels = new PixelRect(-2880, -300, rotation % 2 == 0 ? 1920 : 1080, rotation % 2 == 0 ? 1080 : 1920);
                    HyprlandDisplayMap map = new HyprlandDisplayMap(output, pixels);
                    Equal(pixels, map.ToAvalonia(output.Logical), "Whole output map");
                    Point centre = output.Logical.Center;
                    Point roundtrip = map.ToLogical(map.ToAvalonia(centre));
                    Check(Math.Abs(roundtrip.X - centre.X) < 0.001 && Math.Abs(roundtrip.Y - centre.Y) < 0.001, "No repeated scale/origin/rotation on roundtrip");
                    Check(map.ToAvalonia(output.WorkingArea).Y > pixels.Y, "Native reserved area is mapped");
                }
            }
            Throws(() => HyprlandJson.Outputs(new JsonArray(Output(scale: 0)).ToJsonString()), "Reject zero scale");
            Throws(() => HyprlandJson.Outputs(new JsonArray(Output(transform: 8)).ToJsonString()), "Reject invalid transform");
            Throws(() => HyprlandJson.Outputs(new JsonArray(Output(), Output()).ToJsonString()), "Reject duplicate output");
            Throws(() => HyprlandJson.Address("0x123;exec bad"), "Reject interpolated selector");
            Throws(() => HyprlandJson.Locked("{}"), "Unknown lock state fails closed");
            Throws(() => HyprlandJson.Windows(new JsonArray(Window(), Window()).ToJsonString()), "Reject duplicate client identity");
            Equal(0L, HyprlandJson.Active("{}"), "No active window");
        }

        private static async Task VisibilityAndLedges()
        {
            Fake fake = new Fake();
            fake.Windows.Add(Window("0x456", 43211, 2));
            fake.Windows.Add(Window("0x789", Environment.ProcessId));
            using HyprlandDesktop service = Service(fake);
            HyprlandSnapshot snapshot = await service.CaptureSnapshotAsync(default);
            Check(snapshot.Visible(snapshot.Windows[0]), "Active workspace visible");
            Check(!snapshot.Visible(snapshot.Windows[1]), "Other workspace excluded");
            HyprlandDesk desk = new HyprlandDesk(service);
            desk.Screens.Add(new PixelRect(0, 0, 1920, 1080));
            desk.BindScreens(new[] { new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), true) });
            List<Spot> windows = desk.Windows(Array.Empty<long>());
            Equal(1, windows.Count, "Native window excludes own process and inactive workspace");
            Equal(200, windows[0].Bounds.Y, "Native ledge geometry");
            List<Ledge> ledges = Perches.Find(windows, desk.WorkAreas);
            Check(ledges.Any(l => l.Window == 0x123 && l.Y == 200), "Native ledge joins original Perches");
            fake.Windows[0]["at"] = new JsonArray(100, 300);
            await service.CaptureSnapshotAsync(default);
            Equal(300, desk.Find(0x123).Bounds.Y, "Moved native window tracks ledge");
            fake.Windows[0]["fullscreen"] = 2;
            fake.Windows[0]["at"] = new JsonArray(0, 0);
            fake.Windows[0]["size"] = new JsonArray(1920, 1080);
            await service.CaptureSnapshotAsync(default);
            Check(!desk.Find(0x123).Seat && desk.Covering(desk.Windows(Array.Empty<long>())), "Fullscreen inhibits seating and triggers cover");
            fake.Outputs[0]["specialWorkspace"] = new JsonObject { ["id"] = -99 };
            snapshot = await service.CaptureSnapshotAsync(default);
            Check(!snapshot.Visible(snapshot.Windows[0]), "Special workspace hides ordinary ledge");
            fake.Outputs[0]["specialWorkspace"] = new JsonObject { ["id"] = 0 };
            fake.Locked = true;
            await service.CaptureSnapshotAsync(default);
            Equal(0, desk.Windows(Array.Empty<long>()).Count, "Locked session has no windows");
            Check(desk.Pointer() == null, "Locked session has no pointer");
        }

        private static async Task WindowActions()
        {
            Fake fake = new Fake { Active = 0 };
            using (HyprlandDesktop service = Service(fake))
            {
                Check(await service.SteerAsync(service.ListWindows().Single(), "switch_to", default), "Parent switch_to uses native focus");
                Equal("/dispatch hl.dsp.focus({window=\"address:0x123\"})", fake.Dispatches.Single(), "Exact address Lua dispatch");
                Check(await service.SteerAsync(service.ListWindows().Single(), "minimize", default), "Minimize effect verified");
                Check(await service.SteerAsync(service.ListWindows().Single(), "focus", default), "Minimize restores original workspace");
                Check(await service.SteerAsync(service.ListWindows().Single(), "close", default), "Graceful close verifies disappearance");
                Check(fake.Dispatches.All(c => !c.Contains("kill")), "Ordinary close never force kills");
            }
            fake = new Fake { Effect = false };
            using (HyprlandDesktop service = Service(fake))
            {
                OpenWindow selected = service.ListWindows().Single();
                Check(!await service.SteerAsync(selected, "close", default), "Unsaved prompt is not false close success");
                Equal(1, fake.Dispatches.Count, "No blind close retry");
                Check(!await service.SteerAsync(selected, "close", default), "Consumed selection cannot be reused");
                Equal(1, fake.Dispatches.Count, "No dispatch on reused ticket");
            }
            fake = new Fake { Rejected = true };
            using (HyprlandDesktop service = Service(fake))
                Check(!await service.SteerAsync(service.ListWindows().Single(), "minimize", default), "Rejected action fails");
            fake = new Fake { Active = 0, Dialect = "Unknown request" };
            using (HyprlandDesktop service = Service(fake))
            {
                Check(await service.SteerAsync(service.ListWindows().Single(), "focus", default), "Legacy native dispatcher route");
                Equal("/dispatch focuswindow address:0x123", fake.Dispatches.Single(), "Legacy still exact address");
            }
            fake = new Fake { Active = 0, Dialect = "eval is only supported with the lua config manager" };
            using (HyprlandDesktop service = Service(fake))
                Check(await service.SteerAsync(service.ListWindows().Single(), "focus", default), "Hyprlang config dispatch is detected on current compositor");
            fake = new Fake();
            using (HyprlandDesktop service = Service(fake))
            {
                Check(!await service.SteerAsync(service.ListWindows().Single(), "close_tab", default), "No false native tab support");
                Equal(0, fake.Dispatches.Count, "Unsupported action sends nothing");
            }
        }

        private static async Task Revocation()
        {
            foreach (string change in new[] { "pid", "geometry", "layout", "workspace", "locked", "process", "expiry", "group" })
            {
                Fake fake = new Fake(); long time = 1000; string identity = "first";
                using HyprlandDesktop service = Service(fake, _ => identity, () => time);
                OpenWindow selected = service.ListWindows().Single();
                switch (change)
                {
                    case "pid": fake.Windows[0]["pid"] = 999; break;
                    case "geometry": fake.Windows[0]["at"] = new JsonArray(110, 200); break;
                    case "layout": fake.Outputs[0]["scale"] = 1.25; break;
                    case "workspace": fake.Windows[0]["workspace"]["id"] = 2; break;
                    case "locked": fake.Locked = true; break;
                    case "process": identity = "reused-pid"; break;
                    case "expiry": time += 16000; break;
                    case "group": fake.Windows[0]["grouped"] = new JsonArray("0x123", "0x456"); break;
                }
                Check(!await service.SteerAsync(selected, "close", default), change + " revokes target");
                Equal(0, fake.Dispatches.Count, change + " sends no mutation");
            }
            Fake cancelled = new Fake();
            using (HyprlandDesktop service = Service(cancelled))
            {
                OpenWindow selected = service.ListWindows().Single();
                using CancellationTokenSource token = new CancellationTokenSource(); token.Cancel();
                try { await service.SteerAsync(selected, "close", token.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { checks++; }
                Equal(0, cancelled.Dispatches.Count, "Stop before dispatch sends nothing");
            }
            Fake mixed = new Fake();
            int reads = 0;
            mixed.Before = command => { if (command == "j/monitors" && ++reads == 2) mixed.Outputs[0]["x"] = 10; };
            using (HyprlandDesktop service = Service(mixed))
            {
                Check(service.ListWindows().Count == 0, "Layout transition during capture is rejected");
                Check(service.LastError.Contains("changed"), "Layout transition has actionable error");
            }
            Fake duringDispatch = new Fake();
            using (HyprlandDesktop service = Service(duringDispatch))
            {
                OpenWindow selected = service.ListWindows().Single();
                using CancellationTokenSource token = new CancellationTokenSource();
                duringDispatch.Before = command => { if (command.StartsWith("/dispatch ")) token.Cancel(); };
                try { await service.SteerAsync(selected, "close", token.Token); throw new Exception("Cancellation ignored after dispatch"); }
                catch (OperationCanceledException) { checks++; }
                Equal(1, duringDispatch.Dispatches.Count, "Stop after dispatch never retries uncertain effect");
                Check(service.LastError.Contains("may have occurred"), "Stop reports possible already-dispatched effect");
            }
        }

        private static async Task SocketTransport()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "capri-desktop-fixture-" + Guid.NewGuid().ToString("N"));
            string signature = "fixture", oldSignature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");
            string sockets = Path.Combine(temporary, "hypr", signature);
            Directory.CreateDirectory(sockets);
            Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", signature);
            try
            {
                using Socket command = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                command.Bind(new UnixDomainSocketEndPoint(Path.Combine(sockets, ".socket.sock"))); command.Listen(1);
                HyprlandIpc ipc = new HyprlandIpc(temporary, signature);
                Task server = Task.Run(async () =>
                {
                    using Socket peer = await command.AcceptAsync();
                    byte[] buffer = new byte[128];
                    int count = await peer.ReceiveAsync(buffer);
                    Equal("j/clients", Encoding.UTF8.GetString(buffer, 0, count), "IPC sends the expected read-only command");
                    await peer.SendAsync(Encoding.UTF8.GetBytes("[]"));
                });
                Equal("[]", await ipc.RequestAsync("j/clients", default), "Bounded native socket response");
                await server;
                using Socket events = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                events.Bind(new UnixDomainSocketEndPoint(Path.Combine(sockets, ".socket2.sock"))); events.Listen(1);
                using CancellationTokenSource stop = new CancellationTokenSource();
                TaskCompletionSource connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task eventServer = Task.Run(async () =>
                {
                    using Socket peer = await events.AcceptAsync();
                    await peer.SendAsync(Encoding.UTF8.GetBytes("monitoraddedv2>>1,DP-1,fixture\nworkspacev2>>2,2\n"));
                    connected.SetResult();
                    try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
                });
                await using var stream = ipc.Events(stop.Token).GetAsyncEnumerator();
                Check(await stream.MoveNextAsync(), "Event stream first line");
                Equal("monitoraddedv2>>1,DP-1,fixture", stream.Current, "Hotplug event frame");
                Check(await stream.MoveNextAsync(), "Event stream second line");
                Equal("workspacev2>>2,2", stream.Current, "Workspace event frame");
                await connected.Task; stop.Cancel();
                try { await stream.MoveNextAsync(); throw new Exception("Cancelled stream remained open"); }
                catch (OperationCanceledException) { checks++; }
                await eventServer;
                Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", "different-session");
                try { await ipc.RequestAsync("j/clients", default); throw new Exception("Session mismatch accepted"); }
                catch (InvalidOperationException) { checks++; }
            }
            finally { Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", oldSignature); Directory.Delete(temporary, true); }
        }

        private static async Task PointerAndFailure()
        {
            Fake fake = new Fake();
            using (HyprlandDesktop service = Service(fake))
            {
                Check(await service.MovePointerAsync(100, 120, default), "Native pointer effect verified");
                Equal("/dispatch hl.dsp.cursor.move({x=100,y=120})", fake.Dispatches.Single(), "Logical pointer is not scaled again");
                Check(!await service.MovePointerAsync(-10000, 120, default), "Off-output pointer rejected");
                Equal(1, fake.Dispatches.Count, "Off-output sends no mutation");
                Throws(() => service.Query("/dispatch exec bad"), "Query cannot be used as arbitrary dispatch");
                service.Dispose();
                Throws(() => service.Query("j/clients"), "Disposed connection is unavailable");
            }
            fake = new Fake { Effect = false };
            using (HyprlandDesktop service = Service(fake))
                Check(!await service.MovePointerAsync(100, 120, default), "Accepted but ineffective pointer is not success");
        }

        private static void Envelope()
        {
            Rect[] frames = { new Rect(30, -13.2, 166.5, 266.4), new Rect(13.75, 22.5, 180, 240) };
            Point centre = new Point(120, 120);
            Rect envelope = DesktopEnvelope.Bounds(frames, centre, .65, 2.2, 8);
            foreach (double angle in new[] { -.65, -.3, 0, .3, .65 })
                foreach (Rect frame in frames)
                    foreach (Point corner in new[] { frame.TopLeft, frame.TopRight, frame.BottomLeft, frame.BottomRight })
                    {
                        double rad = angle * Math.PI / 180, x = corner.X - centre.X, y = corner.Y - centre.Y;
                        foreach (double breathe in new[] { -2.2, 2.2 })
                            Check(envelope.Contains(new Point(centre.X + x * Math.Cos(rad) - y * Math.Sin(rad), centre.Y + x * Math.Sin(rad) + y * Math.Cos(rad) + breathe)), "All FX/breathing corners fit surface");
                    }
        }

        private sealed class ReorderedTransport : IHyprlandTransport
        {
            internal readonly Fake Inner = new Fake();
            internal readonly TaskCompletionSource OlderBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource ReleaseOlder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private int monitors;
            public string SessionId => Inner.SessionId;
            public async Task<string> RequestAsync(string command, CancellationToken token)
            {
                string response = await Inner.RequestAsync(command, token);
                if (command == "j/monitors" && ++monitors == 2)
                {
                    OlderBlocked.SetResult();
                    await ReleaseOlder.Task.WaitAsync(token);
                }
                return response;
            }
            public IAsyncEnumerable<string> Events(CancellationToken token) => Inner.Events(token);
        }

        // Keep the independently reproduced regressions in the normal portable suite.
        private static async Task SnapshotPublication()
        {
            ReorderedTransport fake = new ReorderedTransport();
            using HyprlandDesktop service = new HyprlandDesktop(fake, false, pid => pid + ":fixture", () => 1000);
            Task<HyprlandSnapshot> older = service.CaptureSnapshotAsync(default);
            await fake.OlderBlocked.Task;
            fake.Inner.Locked = true;
            Task<HyprlandSnapshot> newer = service.CaptureSnapshotAsync(default);
            using CancellationTokenSource stop = new CancellationTokenSource();
            Task<HyprlandSnapshot> cancelledWaiter = service.CaptureSnapshotAsync(stop.Token);
            stop.Cancel();
            try { await cancelledWaiter; throw new Exception("Queued capture ignored cancellation"); }
            catch (OperationCanceledException) { checks++; }
            fake.ReleaseOlder.SetResult();
            await Task.WhenAll(older, newer);
            Check(newer.Result.Locked && service.Snapshot.Locked, "An older unlocked capture cannot replace a newer locked capture");
            Check(older.Result.Locked, "Lock during final monitor read withholds the old capture's desktop access");
            Check(service.Pointer() == null, "Published locked state suppresses pointer consumers");
            fake.Inner.Locked = false;
            Check(!(await service.CaptureSnapshotAsync(default)).Locked, "A complete fresh unlocked capture restores availability");
        }

        private static async Task SpanningFloating()
        {
            Fake fake = new Fake();
            JsonObject right = Output(1, "DP-2", x: 1920);
            right["activeWorkspace"] = new JsonObject { ["id"] = 2, ["name"] = "2" };
            fake.Outputs.Add(right);
            fake.Windows[0]["floating"] = true;
            fake.Windows[0]["at"] = new JsonArray(1800, 200);
            using HyprlandDesktop service = Service(fake);
            await service.CaptureSnapshotAsync(default);
            HyprlandDesk desk = new HyprlandDesk(service);
            desk.BindScreens(new[] {
                new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), true),
                new Screen(1, new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1080), false)
            });
            List<Spot> windows = desk.Windows(Array.Empty<long>());
            Equal(2, windows.Count, "Visible floating window retains both output segments across distinct workspaces");
            Equal(new PixelRect(1920, 200, 680, 600), desk.Find(0x123, new PixelPoint(2200, 200)).Bounds, "Near-window lookup uses its neighboring segment");
            fake.Windows[0]["floating"] = false;
            await service.CaptureSnapshotAsync(default);
            Equal(1, desk.Windows(Array.Empty<long>()).Count, "Tiled window does not span another output workspace");
            fake.Windows[0]["floating"] = true;
            fake.Windows[0]["fullscreen"] = 2;
            await service.CaptureSnapshotAsync(default);
            Equal(1, desk.Windows(Array.Empty<long>()).Count, "Fullscreen window stays on its owning output");
            fake.Windows[0]["fullscreen"] = 0;
            fake.Windows[0]["workspace"]["id"] = 3;
            await service.CaptureSnapshotAsync(default);
            Equal(0, desk.Windows(Array.Empty<long>()).Count, "Floating window on an invisible workspace remains hidden");
            fake.Windows[0]["pinned"] = true;
            await service.CaptureSnapshotAsync(default);
            Equal(2, desk.Windows(Array.Empty<long>()).Count, "Pinned window retains its cross-output geometry");
        }

        private static async Task RestoreRebind()
        {
            foreach (string change in new[] { "pid", "birth", "class", "session", "layout", "lock", "workspace", "group", "hidden", "missing" })
            {
                Fake fake = new Fake();
                string identity = "original-process-birth";
                using HyprlandDesktop service = Service(fake, _ => identity);
                Check(await service.SteerAsync(service.ListWindows().Single(), "minimize", default), "Minimize before " + change + " restore fixture");
                OpenWindow selected = service.ListWindows().Single();
                fake.Dispatches.Clear();
                fake.AfterDispatch = command =>
                {
                    if (!command.Contains("window.move") || command.Contains("special:minimized")) return;
                    switch (change)
                    {
                        case "pid": fake.Windows[0]["pid"] = 999; break;
                        case "birth": identity = "reused-process-birth"; break;
                        case "class": fake.Windows[0]["class"] = "replacement-app"; break;
                        case "session": fake.Session = "replacement-session"; break;
                        case "layout": fake.Outputs[0]["x"] = -200; break;
                        case "lock": fake.Locked = true; break;
                        case "workspace": fake.Windows[0]["workspace"]["id"] = 7; break;
                        case "group": fake.Windows[0]["grouped"] = new JsonArray("0x123", "0x456"); break;
                        case "hidden": fake.Windows[0]["hidden"] = true; break;
                        case "missing": fake.Windows.Clear(); break;
                    }
                };
                Check(!await service.SteerAsync(selected, "focus", default), "Restore " + change + " change prevents false success");
                Equal(1, fake.Dispatches.Count, "Restore " + change + " change stops before second mutation without retry");
                Check(fake.Dispatches.All(c => !c.Contains("dsp.focus")), "No focus after restore " + change + " change");
            }
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Contains("--read-only-host"))
                {
                    using HyprlandDesktop service = new HyprlandDesktop(new HyprlandIpc(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")), false);
                    HyprlandSnapshot state = await service.CaptureSnapshotAsync(default);
                    // Keep owner titles, addresses, PIDs, account and session identifiers out of the receipt.
                    Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", mode = "read-only", outputs = state.Outputs.Length, mappedWindows = state.Windows.Length,
                        visibleWindows = state.Windows.Count(state.Visible), state.Locked, pointerOnOutput = state.Outputs.Any(m => m.Logical.Contains(state.Pointer)), version = JsonDocument.Parse(service.Query("j/version")).RootElement.GetProperty("version").GetString() }));
                    return 0;
                }
                Geometry(); await VisibilityAndLedges(); await WindowActions(); await Revocation(); await PointerAndFailure(); Envelope(); await SocketTransport();
                await SnapshotPublication(); await SpanningFloating(); await RestoreRebind();
                Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, scope = "synthetic native desktop fixtures; no live mutation, pixels, audio or input" }));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
