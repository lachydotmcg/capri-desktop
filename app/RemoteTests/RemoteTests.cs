using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // Capri Remote's tests, run by app/RemoteTests/run.ps1. Not part of Capri.exe: it builds on its own from
    // app/RemoteCrypto.cs, app/Remote.cs, app/RemoteLink.cs, app/RemoteQr.cs and app/RemoteVectors.cs.
    //   RemoteTests vectors <vectors.json>          every crypto vector, byte for byte
    //   RemoteTests units                           relay hosts, phone names and the DPAPI round trip
    //   RemoteTests qr <folder>                     QR codes for every version and level, for check-qr.py to read back
    //   RemoteTests pc <relay> <state> [beat ms]    the real RemoteLink, driven by JSON lines (remote-e2e.mjs)
    //   RemoteTests forget <state> <device>         drops a phone from a saved state without telling the relay
    //   RemoteTests live <relay> <state> [beat ms]  pc, with the screen view and call on a drawn desktop (RemoteLiveTests.cs)
    internal static partial class RemoteTests
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 2 && args[0] == "vectors") return Vectors(args[1]);
            if (args.Length == 1 && args[0] == "units") return Units();
            if (args.Length == 2 && args[0] == "qr") return Qr(args[1]);
            if ((args.Length == 3 || args.Length == 4) && args[0] == "pc") return Pc(args[1], args[2], args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 30000);
            if ((args.Length == 3 || args.Length == 4) && args[0] == "live") return Pc(args[1], args[2], args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 30000, true);
            if (args.Length == 3 && args[0] == "forget") return Forget(args[1], args[2]);
            Console.Error.WriteLine("usage: RemoteTests vectors <vectors.json> | units | qr <folder> | pc|live <relay> <state> [beat ms] | forget <state> <device>");
            return 2;
        }

        private static int Units()
        {
            List<string> failed = new List<string>();
            int checks = 0;
            Action<bool, string> check = delegate (bool ok, string what)
            {
                checks++;
                if (!ok) failed.Add(what);
            };
            string[][] hosts =
            {
                new[] { "", "wss://capri-remote.lachydotmcg.workers.dev" },
                new[] { "capri-remote.lachydotmcg.workers.dev", "wss://capri-remote.lachydotmcg.workers.dev" },
                new[] { " https://Capri-Remote.Lachydotmcg.Workers.Dev/ ", "wss://capri-remote.lachydotmcg.workers.dev" },
                new[] { "wss://relay.example:8443", "wss://relay.example:8443" },
                new[] { "127.0.0.1:8787", "ws://127.0.0.1:8787" },
                new[] { "ws://localhost:8787/", "ws://localhost:8787" },
                new[] { "ws://[::1]:443", "ws://[::1]:443" },
                new[] { "ws://relay.example", null },
                new[] { "http://relay.example", null },
                new[] { "ftp://relay.example", null },
                new[] { "relay.example/v1/rooms", null },
                new[] { "user@relay.example", null },
                new[] { "relay.example?x=1", null },
            };
            foreach (string[] host in hosts)
            {
                string got = RemoteLink.RelayBase(host[0]);
                check(got == host[1], "relay host \"" + host[0] + "\" gave " + (got ?? "null") + ", not " + (host[1] ?? "null"));
            }
            string smile = char.ConvertFromUtf32(0x1F600);
            string[][] names =
            {
                new[] { "Pixel 8", "Pixel 8" },
                new[] { "  Lachy's" + (char)0x202E + "  iPhone" + (char)7 + " ", "Lachy's iPhone" },
                new[] { "" + (char)0x200B + (char)0x2066 + (char)0, "Unnamed phone" },
                new[] { "Line\nbreak\ttab", "Line break tab" },
                new[] { new string('x', 63) + smile, new string('x', 63) },
                new[] { smile + " phone", smile + " phone" },
                new[] { "lone " + (char)0xD83D + " half", "lone half" },
                new[] { "tag" + char.ConvertFromUtf32(0xE0041) + "s" + char.ConvertFromUtf32(0xF0000), "tags" },
            };
            foreach (string[] name in names)
            {
                string got = RemoteMessages.CleanName(name[0]);
                check(got == name[1], "phone name gave \"" + got + "\", not \"" + name[1] + "\"");
            }
            RemoteState state = RemoteState.Fresh();
            state.Used = true;
            state.Devices.Add(new RemoteDevice { Id = RemoteCrypto.B64u(RemoteCrypto.Random(16)), Key = RemoteCrypto.Random(32), Name = "Test Phone", Added = new DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc) });
            string sealedState = state.Seal();
            RemoteState back = RemoteState.Unseal(sealedState);
            check(back != null && back.ToJson() == state.ToJson(), "DPAPI round trip changed the state");
            check(sealedState.IndexOf(state.Token, StringComparison.Ordinal) < 0 && sealedState.IndexOf(state.Room, StringComparison.Ordinal) < 0, "the sealed state shows the room or token");
            byte[] raw = Convert.FromBase64String(sealedState);
            raw[raw.Length / 2] ^= 1;
            check(RemoteState.Unseal(Convert.ToBase64String(raw)) == null, "a damaged sealed state opened");
            check(RemoteState.Unseal("not base64!") == null && RemoteState.Unseal(null) == null, "junk opened as a state");
            foreach (string line in failed) Console.WriteLine("FAIL " + line);
            Console.WriteLine((failed.Count == 0 ? "PASS" : "FAIL") + " units: " + (checks - failed.Count) + " of " + checks + " checks (relay hosts, phone names, DPAPI round trip)");
            return failed.Count == 0 ? 0 : 1;
        }

        // The real RemoteLink on a relay. Each line in is one JSON command; each line out is one JSON event.
        private static int Pc(string host, string statePath, int beat, bool live = false)
        {
            string relay = RemoteLink.RelayBase(host);
            if (relay == null)
            {
                Console.Error.WriteLine("not a relay host: " + host);
                return 2;
            }
            RemoteState state = File.Exists(statePath) ? RemoteState.Unseal(File.ReadAllText(statePath)) : null;
            if (state == null)
            {
                state = RemoteState.Fresh();
                File.WriteAllText(statePath, state.Seal());
            }
            object output = new object();
            Action<RemoteJson.Writer> say = delegate (RemoteJson.Writer line)
            {
                lock (output)
                {
                    Console.Out.WriteLine(line.ToString());
                    Console.Out.Flush();
                }
            };
            RemoteLink link = null;
            link = new RemoteLink(state, relay, "Test PC", "capri/test", delegate (RemoteState saved) { lock (output) File.WriteAllText(statePath, saved.Seal()); });
            link.BeatMs = beat;
            LiveRig rig = live ? new LiveRig(link, say) : null;
            link.Happened += delegate (string what, string about)
            {
                RemoteJson.Writer line = new RemoteJson.Writer().Text("e", what);
                if (about != null) line.Text("about", about);
                if (what == "status") line.Text("status", link.Status);
                if (what == "qr" && link.Qr != null) line.Text("url", link.Qr);
                RemoteAsk ask = what == "ask" ? link.Asking : null;
                if (ask != null) line.Text("name", ask.Name).Text("code", ask.Code).Text("app", ask.App);
                say(line.Number("online", link.PhonesOnline));
            };
            link.Said += delegate (string device, string id, string text) { say(new RemoteJson.Writer().Text("e", "said").Text("device", device).Text("id", id).Text("text", text)); };
            link.Start();
            string input;
            while ((input = Console.In.ReadLine()) != null)
            {
                RemoteJson.Map command = RemoteJson.ParseMap(input);
                if (command == null) continue;
                string device = command.Text("device"), re = command.Text("re");
                switch (command.Text("c"))
                {
                    case "code": say(new RemoteJson.Writer().Text("e", "code").Flag("ok", link.NewCode() != null)); break;
                    case "close-code": link.CloseCode(); break;
                    case "allow": say(new RemoteJson.Writer().Text("e", "allow").Flag("ok", link.Allow())); break;
                    case "deny": say(new RemoteJson.Writer().Text("e", "deny").Flag("ok", link.Deny())); break;
                    case "revoke": link.Revoke(device); break;
                    case "typing": link.Typing(device, command.Is("on", true), re); break;
                    case "tool": link.Tool(device, command.Text("id"), command.Text("name"), command.Text("summary"), command.Text("state"), re); break;
                    case "error": link.Error(device, command.Text("code"), re); break;
                    case "reply":
                    {
                        // Her words as they stream: each part on its own, a few milliseconds apart, then done.
                        double gap;
                        if (!command.Number("gap", out gap)) gap = 0;
                        List<object> pieces = command.Items("parts") ?? new List<object>();
                        foreach (object piece in pieces)
                        {
                            link.Reply(device, command.Text("id"), re, piece as string, false);
                            if (gap > 0) Thread.Sleep((int)gap);
                        }
                        link.Reply(device, command.Text("id"), re, "", true);
                        break;
                    }
                    case "state":
                    {
                        List<string> ids = new List<string>(), sessions = new List<string>();
                        foreach (RemoteDevice paired in link.Devices())
                        {
                            ids.Add(paired.Id);
                            if (link.HasSession(paired.Id)) sessions.Add(paired.Id);
                        }
                        say(new RemoteJson.Writer().Text("e", "state").Text("status", link.Status).Flag("ready", link.Ready).Number("online", link.PhonesOnline).Texts("devices", ids).Texts("sessions", sessions));
                        break;
                    }
                    default:
                        if (rig == null || !rig.Command(command)) say(new RemoteJson.Writer().Text("e", "unknown").Text("c", command.Text("c") ?? ""));
                        break;
                    case "quit":
                        if (rig != null) rig.Dispose();
                        link.Stop();
                        Thread.Sleep(1000);
                        say(new RemoteJson.Writer().Text("e", "bye"));
                        return 0;
                }
            }
            link.Stop();
            return 0;
        }

        private static int Forget(string statePath, string device)
        {
            RemoteState state = RemoteState.Unseal(File.ReadAllText(statePath));
            RemoteDevice found = state == null ? null : state.Find(device);
            if (found == null)
            {
                Console.Error.WriteLine("no such phone in " + statePath);
                return 1;
            }
            state.Devices.Remove(found);
            File.WriteAllText(statePath, state.Seal());
            Console.WriteLine("forgot " + device);
            return 0;
        }

        private static int Vectors(string path)
        {
            int checks;
            List<string> failed = RemoteVectors.Check(File.ReadAllText(path), out checks);
            foreach (string line in failed) Console.WriteLine("FAIL " + line);
            Console.WriteLine((failed.Count == 0 ? "PASS" : "FAIL") + " vectors: " + (checks - failed.Count) + " of " + checks + " checks match " + path);
            return failed.Count == 0 ? 0 : 1;
        }

        // A QR code at its fullest for each version and level (so each lands on exactly that version), plus a real pairing URL.
        private static int Qr(string folder)
        {
            Directory.CreateDirectory(folder);
            Random dice = new Random(7);
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_#&=:/.";
            List<string> manifest = new List<string>();
            int made = 0, wrong = 0, urls = 30;
            string url = null;
            List<KeyValuePair<string, RemoteQr.Level>> cases = new List<KeyValuePair<string, RemoteQr.Level>>();
            for (int i = 0; i < urls; i++)
            {
                url = RemoteCrypto.QrUrl(RemoteCrypto.B64u(RemoteCrypto.Random(16)), RemoteCrypto.B64u(RemoteCrypto.Random(16)), RemoteCrypto.B64u(RemoteCrypto.Random(32)));
                cases.Add(new KeyValuePair<string, RemoteQr.Level>(url, RemoteQr.Level.Medium));
            }
            for (int version = 1; version <= 40; version++)
                foreach (RemoteQr.Level level in new[] { RemoteQr.Level.Low, RemoteQr.Level.Medium, RemoteQr.Level.Quartile, RemoteQr.Level.High })
                {
                    int bytes = (RemoteQr.DataCodewords(version, level) * 8 - 4 - (version <= 9 ? 8 : 16)) / 8;
                    StringBuilder text = new StringBuilder();
                    for (int i = 0; i < bytes; i++) text.Append(alphabet[dice.Next(alphabet.Length)]);
                    cases.Add(new KeyValuePair<string, RemoteQr.Level>(text.ToString(), level));
                }
            foreach (KeyValuePair<string, RemoteQr.Level> item in cases)
            {
                RemoteQr qr = RemoteQr.Encode(item.Key, item.Value);
                string name = "qr-" + made.ToString("D3", CultureInfo.InvariantCulture) + ".png";
                // Every pairing URL is 123 bytes, so version 8 Medium; each sweep code is full for its version and level.
                int expected = made < urls ? 8 : 1 + (made - urls) / 4;
                if (qr.Version != expected || qr.Correction != item.Value) { wrong++; Console.WriteLine("FAIL qr " + name + " came out as version " + qr.Version + " " + qr.Correction + ", not " + expected + " " + item.Value); }
                int scale = qr.Version > 30 ? 4 : 6;
                Save(qr, Path.Combine(folder, name), scale);
                manifest.Add(name + "\t" + qr.Version + "\t" + qr.Correction + "\t" + qr.Mask + "\t" + scale + "\t" + item.Key);
                made++;
            }
            File.WriteAllLines(Path.Combine(folder, "manifest.tsv"), manifest.ToArray(), new UTF8Encoding(false));
            Console.WriteLine((wrong == 0 ? "PASS" : "FAIL") + " qr: made " + urls + " pairing URLs (version 8 Medium, " + Encoding.UTF8.GetByteCount(url) + " bytes) and " + (made - urls) + " sweep codes, each the version and level it should be");
            return wrong == 0 ? 0 : 1;
        }

        // Black modules on white with the standard four-module quiet zone.
        private static void Save(RemoteQr qr, string path, int scale)
        {
            int side = (qr.Size + 8) * scale;
            using (Bitmap image = new Bitmap(side, side, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                for (int y = 0; y < qr.Size; y++)
                    for (int x = 0; x < qr.Size; x++)
                        if (qr.Dark(x, y)) graphics.FillRectangle(Brushes.Black, (x + 4) * scale, (y + 4) * scale, scale, scale);
                image.Save(path, ImageFormat.Png);
            }
        }
    }
}
