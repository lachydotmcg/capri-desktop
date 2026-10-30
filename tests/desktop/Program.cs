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

        private static async Task PetPlacement()
        {
            PixelRect area = new PixelRect(-1920, -200, 1920, 1080);
            Point origin = PetPlacementGeometry.Restore(area, 209, 299, 1, 1);
            Equal(new Point(-209, 581), origin, "Full FX envelope restores within negative-origin work area");
            Check(PetPlacementGeometry.Contains(area, origin, 209, 299), "Envelope wholly visible after restore");
            Equal(new Point(1, 1), PetPlacementGeometry.Fraction(area, 209, 299, origin), "Saved output fractions roundtrip");
            Point changed = PetPlacementGeometry.Restore(new PixelRect(3840, 30, 1280, 690), 209, 299, 1, 1);
            Equal(new Point(4911, 421), changed, "Saved fraction follows resized reserved work area");
            Equal(new Point(-1920, -200), PetPlacementGeometry.Restore(area, 5000, 5000, 1, 1), "Oversized envelope chooses reachable origin");
            Check(double.IsFinite(PetPlacementGeometry.Restore(area, 209, 299, double.NaN, double.PositiveInfinity).X), "Invalid saved fraction cannot produce invalid native position");
            Check(!PetPlacementGeometry.Contains(area, new Point(-1, 0), 209, 299), "Center-only placement does not qualify full envelope");
            Fake fake = new Fake { Effect = false };
            JsonObject otherOutput = Output(id: 1, name: "DP-2", x: 1920);
            otherOutput["activeWorkspace"] = new JsonObject { ["id"] = 3, ["name"] = "3" };
            fake.Outputs.Add(otherOutput);
            fake.Windows[0]["pid"] = Environment.ProcessId;
            fake.Windows[0]["class"] = "Capri";
            fake.Windows[0]["title"] = "Capri";
            fake.Windows.Add(Window("0x456", Environment.ProcessId));
            fake.Windows[1]["class"] = "Capri";
            fake.Windows[1]["title"] = "Mainframe";
            using HyprlandDesktop service = Service(fake);
            await service.CaptureSnapshotAsync(default);
            HyprlandWindow pet = await service.PetSurfaceAsync(default);
            Equal(0x123L, pet.Address, "Only the own pet role is selected; Mainframe remains focusable");
            await service.ConfigurePetAsync(pet, default);
            Check(fake.Dispatches.All(c => c.Contains("address:0x123") && !c.Contains("0x456")), "Policy only touches owned pet surface");
            Check(fake.Dispatches.Any(c => c.Contains("no_anim") && c.Contains("true")), "Restart policy disables pet position interpolation");
            Check(fake.Dispatches.Any(c => c.Contains("decorate") && c.Contains("false")), "Restart policy preserves transparent upstream appearance");
            fake.Dispatches.Clear();
            using HyprlandDesktop ownOnly = Service(fake);
            Check(ownOnly.Snapshot == null, "Own placement fixture has no whole-scene action snapshot");
            await ownOnly.MovePetAsync(pet, new Point(4000.3, 400.7), 3, default);
            Equal(2, fake.Dispatches.Count, "Cross-output placement assigns destination active workspace before native origin");
            Check(fake.Dispatches[0].Contains("workspace=\"3\"") && fake.Dispatches[0].Contains("follow=false"), "Placement does not switch user's focus/workspace");
            Check(fake.Dispatches[1].Contains("x=4000,y=401"), "Only final compositor coordinates are rounded");
            fake.Windows[0]["pid"] = Environment.ProcessId + 1;
            fake.Dispatches.Clear();
            try { await service.MovePetAsync(pet, new Point(1, 2), 3, default); Check(false, "Reused foreign address must fail"); }
            catch (InvalidOperationException) { Check(true, "Reused foreign address rejected"); }
            Equal(0, fake.Dispatches.Count, "No mutation reaches another process");
            Fake visual = new Fake();
            long now = 1000;
            using HyprlandDesktop visualService = Service(visual, clock: () => now);
            await visualService.CaptureSnapshotAsync(default);
            HyprlandDesk desk = new HyprlandDesk(visualService);
            Screen[] screens = { new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), true) };
            desk.BindScreens(screens);
            Equal(1, desk.Maps.Length, "Verified visual output map is bound");
            now = 3000;
            Check(visualService.Snapshot == null, "Expired action snapshot stays unavailable");
            desk.BindScreens(screens);
            Equal(1, desk.Maps.Length, "Window motion cannot erase last verified visual mapping");
            visual.Outputs.Clear();
            visual.Outputs.Add(Output(id: 2, x: 1920));
            await visualService.CaptureSnapshotAsync(default);
            desk.BindScreens(screens);
            Equal(0, desk.Maps.Length, "A verified output removal replaces cached visual geometry");

        }

        private static async Task UnmappedPetRecovery()
        {
            Fake fake = new Fake { Effect = false };
            fake.Outputs = new JsonArray(Output(x: -1280, y: -80, scale: 1.5));
            fake.Windows = new JsonArray(Window(pid: Environment.ProcessId, workspace: 9), Window("0x456", Environment.ProcessId));
            fake.Windows[0]["class"] = "Capri";
            fake.Windows[0]["title"] = "Capri";
            fake.Windows[0]["floating"] = true;
            fake.Windows[0]["at"] = new JsonArray(9000, 9000);
            fake.Windows[0]["size"] = new JsonArray(219, 321);
            fake.Windows[1]["class"] = "Capri";
            fake.Windows[1]["title"] = "Capri Mainframe";
            using HyprlandDesktop service = Service(fake);
            await service.CaptureSnapshotAsync(default);
            HyprlandDesk desk = new HyprlandDesk(service);
            Screen[] screens = { new Screen(1.5, new PixelRect(-1920, -120, 1920, 1080), new PixelRect(-1920, -75, 1920, 1035), true) };
            desk.BindScreens(screens);
            Equal(0, desk.Maps.Length, "Nameless scaled screen cannot establish an input mapping from different bounds");
            Check(desk.ActiveMap == null && desk.Pointer() == null && desk.WorkAt(new PixelPoint(-1000, 20)) == null,
                "Unmatched output keeps pointer, active mapping and mapped work area unavailable");
            using HyprlandPetSurface surface = new HyprlandPetSurface(service);
            fake.Rejected = true;
            await surface.QueueRecovery();
            Check(surface.Error != null, "A refused recovery remains a visible error instead of success");
            fake.Rejected = false;
            fake.Dispatches.Clear();
            await surface.QueueRecovery();
            Check(surface.Error == null, "A later recovery attempt succeeds after a transient compositor failure");
            Check(fake.Dispatches.All(c => c.Contains("address:0x123") && !c.Contains("0x456")), "Unmapped recovery only mutates this process's pet, never Mainframe");
            Check(fake.Dispatches.Any(c => c.Contains("workspace=\"1\"") && c.Contains("follow=false")), "Unmapped recovery uses the current visible workspace without following focus");
            Check(fake.Dispatches.Last().Contains("x=-750,y=134"), "Recovery derives center from actual compositor envelope and scaled reserved work area");
            HyprlandOutput output = service.Snapshot.Outputs[0];
            Check(output.WorkingArea.Contains(new Rect(-750, 134, 219, 321)), "Recovered full compositor envelope fits the output work area");
            desk.BindScreens(screens);
            Equal(0, desk.Maps.Length, "Successful visibility rescue does not create an input map");
            Check(desk.Pointer() == null, "Pointer targeting remains unavailable after rescue");
            fake.Windows[0]["at"] = new JsonArray(-750, 134);
            fake.Windows[0]["workspace"] = new JsonObject { ["id"] = 1, ["name"] = "1" };
            fake.Dispatches.Clear();
            await surface.QueueRecovery();
            Equal(0, fake.Dispatches.Count, "Repeated recovery leaves a fully visible pet in place");
            await surface.QueueRecovery(true);
            Check(fake.Dispatches.Single().Contains("x=-750,y=-50"), "Bring retries at the focused output's top work area even without a map");
            fake.Dispatches.Clear();
            fake.Locked = true;
            await surface.QueueRecovery(true);
            Check(surface.Error.Contains("locked"), "Recovery reports the current lock state");
            Equal(0, fake.Dispatches.Count, "A locked desktop receives no recovery move");
            fake.Locked = false;
            fake.Outputs.Clear();
            await surface.QueueRecovery(true);
            Check(surface.Error.Contains("No visible output"), "No-output recovery remains retryable with an explicit status");
            fake.Outputs.Add(Output(x: -1280, y: -80, scale: 1.5));
            int monitorReads = 0;
            fake.Before = command =>
            {
                if (command == "j/monitors" && ++monitorReads == 2) fake.Outputs[0]["reserved"] = new JsonArray(0, 90, 0, 0);
            };
            await surface.QueueRecovery(true);
            Check(surface.Error.Contains("layout or pet size changed"), "A layout change during recovery requires another observation");
            Equal(0, fake.Dispatches.Count, "Stale recovery geometry is never dispatched");
            fake.Before = null;
            await surface.QueueRecovery(true);
            Check(surface.Error == null && fake.Dispatches.Single().Contains("x=-750,y=10"), "Retry uses the new work area, not stale geometry");
            fake.Dispatches.Clear();
            monitorReads = 0;
            fake.Before = command =>
            {
                if (command == "j/monitors" && ++monitorReads == 1) fake.Windows[0]["size"] = new JsonArray(319, 421);
            };
            await surface.QueueRecovery(true);
            Check(surface.Error.Contains("layout or pet size changed") && fake.Dispatches.Count == 0, "A pet size change cannot dispatch a stale envelope");
            fake.Before = null;
            await surface.QueueRecovery(true);
            Check(surface.Error == null && fake.Dispatches.Single().Contains("x=-800,y=10"), "A later attempt uses the actual changed envelope");
            fake.Dispatches.Clear();
            surface.Suspend(true);
            await surface.QueueRecovery(true);
            Equal(0, fake.Dispatches.Count, "Hiding suspends recovery requests");
            surface.Suspend(false);
            fake.Windows[0]["hidden"] = true;
            await surface.QueueRecovery();
            Check(surface.Error.Contains("hidden") && fake.Dispatches.Count == 0, "A compositor-hidden pet is reported honestly, not treated as visibly recovered");
            fake.Windows[0]["hidden"] = false;
            fake.Outputs[0]["scale"] = 1;
            fake.Outputs[0]["x"] = -1920;
            fake.Outputs[0]["y"] = -120;
            await service.CaptureSnapshotAsync(default);
            desk.BindScreens(screens);
            Equal(1, desk.Maps.Length, "A later verified output match restores native mapping after recovery");
        }

        private static async Task MainframePlacement()
        {
            HyprlandOutput plain = HyprlandJson.Outputs(new JsonArray(Output()).ToJsonString())[0];
            Screen exact = new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 30, 1920, 1050), true);
            Equal(exact, MainframePlacementGeometry.Match(plain, new[] { exact }), "Mainframe accepts a unique exact rectangle when display names are missing");
            Check(MainframePlacementGeometry.Match(plain, new[] { exact, exact }) == null, "Ambiguous display rectangles do not create a Mainframe map");
            Fake fake = new Fake { Effect = false };
            fake.Outputs = new JsonArray(Output(x: -1280, y: -80, scale: 1.5));
            fake.Windows = new JsonArray(Window(pid: Environment.ProcessId), Window("0x456", Environment.ProcessId, 9));
            fake.Windows[0]["class"] = "Capri"; fake.Windows[0]["title"] = "Capri";
            fake.Windows[1]["class"] = "Capri"; fake.Windows[1]["title"] = "Capri Mainframe";
            fake.Windows[1]["size"] = new JsonArray(1000, 600); fake.Windows[1]["at"] = new JsonArray(9000, 9000);
            using HyprlandDesktop service = Service(fake);
            HyprlandOwnedPlacement seen = await service.ObserveOwnedWindowAsync("Capri Mainframe", default);
            Equal(0x456L, seen.Window.Address, "Mainframe observation selects its distinct owned role");
            Check(service.Snapshot == null, "Owned-window observation does not manufacture a whole-scene action snapshot");
            Check(MainframePlacementGeometry.Match(seen.Output, new[] { exact }) == null, "Scaled output with unavailable names stays unmapped");
            MainframeViewport fit = MainframePlacementGeometry.Fit(seen.Output, new Size(1600, 1000), new Size(800, 500),
                new Size(800, 500), new Size(280, 240), null, 2);
            Equal(new Size(628, 333), fit.Dip, "Unmapped viewport fits using measured logical-to-DIP dimensions without applying DPI twice");
            Equal(new Size(1256, 666), fit.Logical, "The bounded viewport has an explicit compositor size request");
            Equal(new Size(628, 333), MainframePlacementGeometry.Fit(seen.Output, new Size(1600, 1000), new Size(800, 500),
                new Size(800, 500), new Size(280, 240), null, 1).Dip, "Measured fallback does not infer output scale from global DPI");
            Equal(new Size(1256, 666), MainframePlacementGeometry.Fit(seen.Output, new Size(1256, 666), new Size(800, 500),
                new Size(800, 500), new Size(280, 240), null, 1, new Vector(2, 2)).Logical, "Pending Avalonia configure cannot feed a resized window back into the measured scale");
            Throws(() => MainframePlacementGeometry.Fit(seen.Output, new Size(0, 1), new Size(800, 500), new Size(800, 500), new Size(280, 240), null, 1), "Unrendered window dimensions cannot be used as a placement ratio");
            Point origin = MainframePlacementGeometry.Center(seen.Output.WorkingArea, seen.Window.Logical.Size);
            Equal(new Point(-1140, -5), origin, "Mainframe is centered using the actual compositor envelope and reserved work area");
            fake.AfterDispatch = command =>
            {
                if (command.Contains("window.float")) fake.Windows[1]["floating"] = true;
                if (command.Contains("workspace=\"1\"")) fake.Windows[1]["workspace"] = new JsonObject { ["id"] = 1, ["name"] = "1" };
                if (command.Contains("x=-1140,y=-5")) fake.Windows[1]["at"] = new JsonArray(-1140, -5);
            };
            await service.PlaceOwnedWindowAsync("Capri Mainframe", origin, seen.Workspace, default, seen);
            Check(fake.Dispatches.All(c => c.Contains("address:0x456") && !c.Contains("address:0x123")), "Mainframe placement never mutates the pet window");
            Check(fake.Dispatches.Any(c => c.Contains("follow=false")), "Mainframe moves to the visible workspace without switching desktop focus");
            Check(fake.Dispatches.All(c => !c.Contains("set_prop") && !c.Contains("focus")), "Mainframe does not receive pet policy or helper-level focus changes");
            Check((await service.ObserveOwnedWindowAsync("Capri Mainframe", default)).Visible, "The actual owned window is fully inside the focused work area after placement");
            Equal(0x123L, fake.Active, "Native placement preserves focus for the view's explicit activation step");
            fake.Outputs[0]["scale"] = 1;
            seen = await service.ObserveOwnedWindowAsync("Capri Mainframe", default);
            fake.Windows[1]["size"] = new JsonArray(942, 522);
            fake.Windows[1]["floating"] = false;
            fake.Dispatches.Clear();
            fake.AfterDispatch = command =>
            {
                if (command.Contains("window.float")) fake.Windows[1]["floating"] = true;
                if (command.Contains("window.resize") && command.Contains("x=384,y=804")) fake.Windows[1]["size"] = new JsonArray(384, 804);
                if (command.Contains("workspace=\"3\"")) fake.Windows[1]["workspace"] = new JsonObject { ["id"] = 3, ["name"] = "3" };
                if (command.Contains("window.move") && command.Contains("x=-512,y=73")) fake.Windows[1]["at"] = new JsonArray(-512, 73);
                if (command.Contains("window.move") && command.Contains("x=900,y=50")) fake.Windows[1]["at"] = new JsonArray(900, 50);
            };
            await service.PlaceOwnedWindowAsync("Capri Mainframe", new Point(-512, 73), 1, default, seen, new Size(384, 804));
            int floated = fake.Dispatches.FindIndex(c => c.Contains("window.float"));
            int resized = fake.Dispatches.FindIndex(c => c.Contains("window.resize") && c.Contains("relative=false"));
            int moved = fake.Dispatches.FindIndex(c => c.Contains("window.move"));
            Check(floated >= 0 && resized > floated && moved > resized, "Initial tiling is followed by an exact requested resize before Mainframe is positioned");
            Equal(new Size(384, 804), (await service.ObserveOwnedWindowAsync("Capri Mainframe", default)).Window.Logical.Size, "Requested primary viewport replaces the demonstrated 942 by 522 tiled configure");
            fake.Dispatches.Clear();
            JsonObject second = Output(id: 1, name: "DP-2", x: 640); second["activeWorkspace"] = new JsonObject { ["id"] = 3, ["name"] = "3" };
            fake.Outputs.Add(second);
            await service.PlaceOwnedWindowAsync("Capri Mainframe", new Point(900, 50), 3, default);
            Check(fake.Dispatches.Any(c => c.Contains("workspace=\"3\"") && c.Contains("follow=false")), "Generic owned overlays may use a visible nonfocused output without changing focus");
            fake.Outputs.RemoveAt(1);
            seen = await service.ObserveOwnedWindowAsync("Capri Mainframe", default);
            fake.Dispatches.Clear(); fake.Locked = true;
            try { await service.PlaceOwnedWindowAsync("Capri Mainframe", origin, 1, default, seen); Check(false, "Locked placement must fail"); }
            catch (InvalidOperationException) { Check(true, "Mainframe placement rejects a locked desktop"); }
            Equal(0, fake.Dispatches.Count, "A locked desktop receives no owned-window mutation");
            fake.Locked = false;
            fake.Windows[1]["floating"] = false;
            fake.AfterDispatch = _ => fake.Locked = true;
            try { await service.PlaceOwnedWindowAsync("Capri Mainframe", origin, 1, default, seen); Check(false, "Mid-placement lock must fail"); }
            catch (InvalidOperationException) { Check(true, "Lock changes after floating stop subsequent placement operations"); }
            Equal(1, fake.Dispatches.Count, "No workspace or position write follows the newly observed lock");
            fake.Locked = false; fake.AfterDispatch = null; fake.Dispatches.Clear();
            fake.Outputs[0]["reserved"] = new JsonArray(0, 100, 0, 0);
            try { await service.PlaceOwnedWindowAsync("Capri Mainframe", origin, 1, default, seen); Check(false, "Stale work area must fail"); }
            catch (InvalidOperationException) { Check(true, "A changed work area invalidates the observed placement"); }
            Equal(0, fake.Dispatches.Count, "Stale Mainframe geometry is never dispatched");
            fake.Windows[1]["pid"] = Environment.ProcessId + 1;
            try { await service.PlaceOwnedWindowAsync("Capri Mainframe", origin, 1, default); Check(false, "Foreign title must fail"); }
            catch (InvalidOperationException) { Check(true, "Matching titles in another process cannot be moved"); }
            Equal(0, fake.Dispatches.Count, "Foreign window receives no placement mutation");
        }

        private static async Task OwnedPlacementPostconditions()
        {
            Fake fake = new Fake { Effect = false };
            fake.Windows = new JsonArray(Window(pid: Environment.ProcessId));
            fake.Windows[0]["title"] = "Capri Mainframe";
            fake.Windows[0]["floating"] = true;
            fake.Windows[0]["at"] = new JsonArray(0, 0);
            fake.Windows[0]["size"] = new JsonArray(300, 200);
            using HyprlandDesktop service = Service(fake);
            try { await service.PlaceOwnedWindowAsync("Capri Mainframe", new Point(100, 200), 1, default); Check(false, "Acknowledgment alone must not complete placement"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("did not reach"), "A successful move acknowledgment without an observed effect fails placement"); }
            Equal(1, fake.Dispatches.Count, "Postcondition observation never replays a move whose effect is uncertain");
            int settled = 0; bool dispatched = false;
            fake.AfterDispatch = _ => dispatched = true;
            fake.Before = command =>
            {
                if (dispatched && command == "j/clients" && ++settled == 2) fake.Windows[0]["at"] = new JsonArray(100, 200);
            };
            await service.PlaceOwnedWindowAsync("Capri Mainframe", new Point(100, 200), 1, default);
            Equal(2, settled, "Placement waits for a bounded later observation of the requested origin");
            fake.Before = null;
            foreach (string mismatch in new[] { "workspace", "size", "hidden", "address", "lock" })
            {
                fake.Locked = false;
                fake.Windows[0]["address"] = "0x123";
                fake.Windows[0]["workspace"] = new JsonObject { ["id"] = 1, ["name"] = "1" };
                fake.Windows[0]["size"] = new JsonArray(300, 200);
                fake.Windows[0]["hidden"] = false;
                fake.AfterDispatch = _ =>
                {
                    fake.Windows[0]["at"] = new JsonArray(200, 250);
                    if (mismatch == "workspace") fake.Windows[0]["workspace"] = new JsonObject { ["id"] = 9, ["name"] = "9" };
                    if (mismatch == "size") fake.Windows[0]["size"] = new JsonArray(350, 200);
                    if (mismatch == "hidden") fake.Windows[0]["hidden"] = true;
                    if (mismatch == "address") fake.Windows[0]["address"] = "0x999";
                    if (mismatch == "lock") fake.Locked = true;
                };
                try { await service.PlaceOwnedWindowAsync("Capri Mainframe", new Point(200, 250), 1, default, requestedSize: new Size(300, 200)); Check(false, "Changed " + mismatch + " must prevent placement success"); }
                catch (InvalidOperationException) { Check(true, "Post-move " + mismatch + " mismatch prevents a success/focus receipt"); }
            }
        }

        private static async Task OwnedChatFocus()
        {
            Fake Fixture()
            {
                var fake = new Fake { Active = 0x777 };
                fake.Windows = new JsonArray(Window(pid: Environment.ProcessId));
                fake.Windows[0]["title"] = "Type to Capri";
                return fake;
            }
            Fake fake = Fixture();
            using (HyprlandDesktop service = Service(fake))
            {
                int keyboardReads = 0;
                await service.WaitForOwnedInputFocusAsync("Type to Capri", 0x777, _ => Task.FromResult(++keyboardReads >= 3), default);
                Equal(3, keyboardReads, "Typer waits for actual X11 keyboard focus after compositor focus");
                Equal(1, fake.Dispatches.Count, "Explicit Type sends one owned focus request");
                Check(fake.Dispatches[0].Contains("address:0x123"), "Focus request names the exact observed owned address");
                fake.Dispatches.Clear();
                await service.WaitForOwnedInputFocusAsync("Type to Capri", 0x123, _ => Task.FromResult(true), default);
                Equal(0, fake.Dispatches.Count, "Already focused typer does not replay activation or move the pointer");
            }
            fake = Fixture();
            JsonObject other = Output(id: 1, name: "DP-2", x: 1920);
            other["activeWorkspace"] = new JsonObject { ["id"] = 2, ["name"] = "2" };
            fake.Outputs.Add(other);
            fake.Windows[0]["at"] = new JsonArray(2000, 200);
            fake.Windows[0]["workspace"] = new JsonObject { ["id"] = 2, ["name"] = "2" };
            fake.Windows[0]["monitor"] = 1;
            fake.AfterDispatch = _ => { fake.Outputs[0]["focused"] = false; fake.Outputs[1]["focused"] = true; };
            using (HyprlandDesktop service = Service(fake))
            {
                await service.WaitForOwnedInputFocusAsync("Type to Capri", 0x777, _ => Task.FromResult(true), default);
                Equal(1, fake.Dispatches.Count, "Explicit Type may focus a fully visible pet output from Mainframe on another monitor");
            }
            foreach (string mismatch in new[] { "owner", "locked", "hidden", "workspace", "foreign", "ambiguous", "keyboard", "ack", "address", "session", "changed-after-keyboard", "cancel" })
            {
                fake = Fixture();
                using var cancellation = new CancellationTokenSource();
                if (mismatch == "owner") fake.Active = 0x999;
                if (mismatch == "locked") fake.Locked = true;
                if (mismatch == "hidden") fake.Windows[0]["hidden"] = true;
                if (mismatch == "workspace") fake.Windows[0]["workspace"] = new JsonObject { ["id"] = 9, ["name"] = "9" };
                if (mismatch == "foreign") fake.Windows[0]["pid"] = Environment.ProcessId + 1;
                if (mismatch == "ambiguous") { JsonNode duplicate = fake.Windows[0].DeepClone(); duplicate["address"] = "0x456"; fake.Windows.Add(duplicate); }
                if (mismatch == "ack") fake.Effect = false;
                fake.AfterDispatch = _ =>
                {
                    if (mismatch == "address") fake.Windows[0]["address"] = "0x999";
                    if (mismatch == "session") fake.Session = "replacement";
                    if (mismatch == "cancel") cancellation.Cancel();
                };
                using HyprlandDesktop service = Service(fake);
                try
                {
                    await service.WaitForOwnedInputFocusAsync("Type to Capri", 0x777, _ =>
                    {
                        if (mismatch == "changed-after-keyboard") fake.Active = 0x999;
                        return Task.FromResult(mismatch != "keyboard");
                    }, cancellation.Token);
                    Check(false, "Focus must reject " + mismatch);
                }
                catch (Exception error) when (error is InvalidOperationException || error is OperationCanceledException)
                { Check(true, "Focus rejects " + mismatch + " without a false success receipt"); }
                Check(fake.Dispatches.Count <= 1, "Focus never replays an uncertain request for " + mismatch);
            }
            fake = Fixture(); fake.Windows[0]["title"] = "Talk to Capri";
            using (HyprlandDesktop service = Service(fake))
            {
                await service.ConfigurePassiveChatAsync("Talk to Capri", default);
                Check(fake.Dispatches.Single().Contains("no_focus") && !fake.Dispatches[0].Contains("dsp.focus("), "Passive pill policy prevents keyboard focus without dispatching activation");
                await service.ConfigurePassiveChatAsync("Talk to Capri", default);
                Equal(1, fake.Dispatches.Count, "Animation placement reuses the scoped passive policy");
                service.ForgetPassiveChatPolicy("Talk to Capri");
                await service.ConfigurePassiveChatAsync("Talk to Capri", default);
                Equal(2, fake.Dispatches.Count, "Remapping reapplies passive focus policy even for a retained XID");
                foreach (string title in new[] { "Type to Capri", "Capri Mainframe", "Capri" })
                {
                    try { await service.ConfigurePassiveChatAsync(title, default); Check(false, "Interactive and pet roles must not receive passive chat policy"); }
                    catch (ArgumentException) { Check(true, "Passive policy excludes " + title); }
                }
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
                await SnapshotPublication(); await SpanningFloating(); await RestoreRebind(); await PetPlacement(); await UnmappedPetRecovery(); await MainframePlacement(); await OwnedPlacementPostconditions(); await OwnedChatFocus();
                Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, scope = "synthetic native desktop fixtures; no live mutation, pixels, audio or input" }));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
