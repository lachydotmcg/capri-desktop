using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private void SmokeResources(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            string data = Path.Combine(output, "rest-data");
            Engine resting = new Engine(data);
            ProcessStartInfo launch = resting.StartInfo(Engine.Picks[2], false, false, 1);
            string model = Regex.Match(launch.Arguments, "-m \"([^\"]+)\"").Groups[1].Value;
            Directory.CreateDirectory(Path.GetDirectoryName(launch.FileName));
            Directory.CreateDirectory(Path.GetDirectoryName(model));
            File.WriteAllText(launch.FileName, "");
            File.WriteAllText(model, "");
            Func<ProcessStartInfo, Process> spawnWas = Engine.Spawn;
            List<System.Net.Sockets.TcpListener> fakes = new List<System.Net.Sockets.TcpListener>();
            int spawned = 0;
            Engine.Spawn = delegate(ProcessStartInfo info)
            {
                spawned++;
                System.Net.Sockets.TcpListener fake;
                FakeServer(new[] { "{\"status\":\"ok\"}" }, new List<string>(), out fake, port: int.Parse(Regex.Match(info.Arguments, @"--port (\d+)").Groups[1].Value));
                lock (fakes) fakes.Add(fake);
                return Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true });
            };
            try
            {
                resting.Ensure(Engine.Picks[2]);
                int id = resting.ServerId;
                if (!resting.Running || id == 0) throw new Exception("The fake brain didn't start.");
                if (resting.Rest(60)) throw new Exception("A brain used a moment ago stopped.");
                resting.Begin();
                try
                {
                    Thread.Sleep(30);
                    if (resting.Rest(0) || !resting.Running) throw new Exception("A brain stopped in the middle of an ask.");
                }
                finally { resting.End(); }

                SystemUse use = new SystemUse();
                use.Sample(id, false);
                Thread.Sleep(400);
                UseView view = use.Sample(id, false);
                UseRow own = view.Capri.Find(delegate(UseRow row) { return row.Name == "Capri"; });
                UseRow brainRow = view.Capri.Find(delegate(UseRow row) { return row.Name == "Her brain"; });
                long ram, total;
                SystemUse.Memory(out ram, out total);
                using (Process current = Process.GetCurrentProcess())
                {
                    if (use.Problem != null) throw new Exception("The process list failed: " + use.Problem);
                    if (own == null || own != view.Capri[0] || own.Ram < (8L << 20) || own.Ram > current.WorkingSet64 + (64L << 20)) throw new Exception("Capri's own row is wrong: " + (own == null ? "missing" : own.Ram + " bytes beside a working set of " + current.WorkingSet64));
                }
                if (brainRow == null || brainRow.Ram <= 0) throw new Exception("Her brain (the fake llama-server) wasn't counted as Capri's: " + string.Join(", ", view.Capri.ConvertAll(delegate(UseRow row) { return row.Name; }).ToArray()));
                int mineCounted = 0;
                foreach (UseRow row in view.Capri) mineCounted += row.Count;
                if (own.Count != 1 || brainRow.Count != 1 || mineCounted != view.CapriTotal.Count || view.Others.Exists(delegate(UseRow row) { return row.Name == "Her brain"; })) throw new Exception("Capri's parts were counted wrong: " + string.Join(", ", view.Capri.ConvertAll(delegate(UseRow row) { return row.Name + " x" + row.Count; }).ToArray()) + " of " + view.CapriTotal.Count);
                UseRow host = view.Others.Find(delegate(UseRow row) { return row.Name == "svchost"; });
                if (host == null || host.Count < 3 || host.Ram <= 0) throw new Exception("Other programs weren't read or grouped by name: svchost " + (host == null ? "missing" : "x" + host.Count));
                if (view.RamTotal != total || view.RamUsed <= 0 || view.RamUsed > view.RamTotal || view.CapriTotal.Ram < own.Ram + brainRow.Ram || view.OthersTotal.Count < 20) throw new Exception("The totals don't add up: " + view.RamUsed + " of " + view.RamTotal + ", Capri " + view.CapriTotal.Ram + ", " + view.OthersTotal.Count + " other processes");
                if (view.Cpu < 0 || view.Cpu > 100 || view.CapriCpu > view.Cpu) throw new Exception("CPU use is out of range: " + view.Cpu + " / Capri " + view.CapriCpu);
                checks.Add("PASS resources: the process list reads every program with its private memory and CPU, groups them by name, counts her brain and other children as Capri's and keeps them out of other programs (" + view.CapriTotal.Count + " Capri, " + view.OthersTotal.Count + " other processes)");

                UseView graphics = use.Sample(id, true);
                if (use.Problem == null && graphics.Gpu > 100) throw new Exception("GPU use is over 100%: " + graphics.Gpu);
                checks.Add(graphics.GpuKnown ? "PASS resources GPU: Windows' GPU Engine counters read, GPU " + UseView.Percent(graphics.Gpu) : "LIMIT resources GPU: this PC has no GPU Engine counters (" + (use.Problem ?? "none") + "), so the panel shows GPU as -");

                Thread.Sleep(1100);
                if (!resting.Rest(1) || resting.Running) throw new Exception("An unused brain didn't stop.");
                bool gone;
                try { using (Process left = Process.GetProcessById(id)) gone = left.HasExited; }
                catch (ArgumentException) { gone = true; }
                if (!gone) throw new Exception("The stopped brain's process kept running.");
                resting.Ensure(Engine.Picks[2]);
                if (!resting.Running || spawned != 2) throw new Exception("A rested brain didn't start again when asked.");
                Engine was = brain.Local;
                try
                {
                    brain.Local = null;
                    if (BrainRestAfter != SpareBrainRest) throw new Exception("A brain that isn't picked waits " + BrainRestAfter + " s.");
                    brain.Local = engine;
                    if (!preferences.NoAi && BrainRestAfter != BrainRest) throw new Exception("Her picked brain waits " + BrainRestAfter + " s.");
                }
                finally { brain.Local = was; }
                checks.Add("PASS brain rest: llama-server stops after " + BrainRest / 60 + " minutes unused, or " + SpareBrainRest + " seconds once another brain is picked, never during an ask, and starts again on the next one");
            }
            finally
            {
                Engine.Spawn = spawnWas;
                resting.Stop();
                lock (fakes) foreach (System.Net.Sockets.TcpListener fake in fakes) fake.Stop();
                try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            if (Mainframe.ResourcesWords(14.2, 9800L << 20, 16L << 30) != "CPU 14% · RAM 9.6/16 GB" || Mainframe.ResourcesWords(-1, 300L << 20, 800L << 20) != "CPU - · RAM 300/800 MB")
                throw new Exception("The Resources line reads " + Mainframe.ResourcesWords(14.2, 9800L << 20, 16L << 30) + " / " + Mainframe.ResourcesWords(-1, 300L << 20, 800L << 20));
            UseView made = new UseView { Cpu = 37, RamUsed = 12300L << 20, RamTotal = 32L << 30, Gpu = 64, VramUsed = 5300L << 20, VramTotal = 8L << 30, GpuName = "NVIDIA GeForce RTX 4060", GpuKnown = true };
            made.Capri.Add(new UseRow { Name = "Capri", Cpu = 1.2, Ram = 410L << 20, Gpu = 3, Vram = 120L << 20 });
            made.Capri.Add(new UseRow { Name = "Her brain", Cpu = 0.4, Ram = 220L << 20, Gpu = 0, Vram = 3400L << 20 });
            made.Capri.Add(new UseRow { Name = "HD voice", Cpu = 0, Ram = 96L << 20, Gpu = 0, Vram = 1100L << 20 });
            made.Others.Add(new UseRow { Name = "FortniteClient-Win64-Shipping", Cpu = 22.5, Ram = 5100L << 20, Gpu = 58, Vram = 2900L << 20 });
            made.Others.Add(new UseRow { Name = "chrome", Count = 31, Cpu = 8, Ram = 2300L << 20, Gpu = 2, Vram = 300L << 20 });
            made.Others.Add(new UseRow { Name = "Microsoft Edge WebView2", Count = 12, Cpu = 1, Ram = 900L << 20, Gpu = 0, Vram = 0 });
            made.Others.Add(new UseRow { Name = "Discord", Count = 6, Cpu = 2, Ram = 700L << 20, Gpu = 1, Vram = 90L << 20 });
            made.Others.Add(new UseRow { Name = "Spotify", Count = 5, Cpu = 0.5, Ram = 400L << 20, Gpu = 0, Vram = 40L << 20 });
            made.Others.Add(new UseRow { Name = "explorer", Cpu = 0.2, Ram = 150L << 20, Gpu = 0, Vram = 20L << 20 });
            made.Others.Add(new UseRow { Name = "never shown", Cpu = 0, Ram = 1L << 20 });
            made.CapriTotal.Cpu = 1.6; made.CapriTotal.Ram = 726L << 20; made.CapriTotal.Gpu = 3; made.CapriTotal.Vram = 4620L << 20; made.CapriTotal.Count = 3;
            made.OthersTotal.Cpu = 35.4; made.OthersTotal.Ram = 11574L << 20; made.OthersTotal.Gpu = 61; made.OthersTotal.Vram = 3350L << 20; made.OthersTotal.Count = 220;
            foreach (float zoom in new[] { 1f, 1.5f, 2f })
            {
                string crowded = mainframe.ResourcesSheetAt(zoom, made, Path.Combine(output, "resources-" + (int)(zoom * 100) + ".png"));
                if (crowded != null) throw new Exception("The Resources panel at " + (int)(zoom * 100) + "%: " + crowded);
            }
            checks.Add("PASS resources panel: CPU, RAM and GPU bars with Capri's share, her parts and the busiest other programs fit at 100%, 150% and 200% with nothing cut or overlapping");

            string docked = mainframe.ResourcesDocked(Path.Combine(output, "mainframe-resources.png"));
            if (docked != null) throw new Exception(docked);
            checks.Add("PASS Resources key: opens its panel docked under the Mainframe, lights up while it is open, follows the Mainframe when it moves, and closes it again");
        }

    }
}
