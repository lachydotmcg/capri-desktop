using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CapriDesktop
{
    internal static class RemoteWake
    {
        internal const string Feature = "wake";
        internal const string Guide = "https://github.com/lachydotmcg/capri-desktop/blob/main/docs/WAKE_FROM_PHONE.md";
        private const int Most = 4, KeepMs = 60000;

        internal sealed class Card
        {
            internal string Mac, Broadcast, Address, Name, Description, Ready = "";
            internal bool Wired, Routed;
        }

        private static readonly object gate = new object();
        private static List<Card> kept;
        private static int keptAt;

        private static readonly string[] Virtuals = { "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "vbox", "tap-", "tap ", "wintun", "wireguard", "tailscale", "zerotier", "hamachi", "loopback", "bluetooth", "npcap", "docker", "wsl", "vpn", "teredo", "isatap" };

        internal static List<Card> Cards()
        {
            lock (gate)
                if (kept != null && unchecked(Environment.TickCount - keptAt) < KeepMs) return kept;
            List<Card> found = Find();
            Arm(found);
            lock (gate)
            {
                kept = found;
                keptAt = Environment.TickCount;
            }
            return found;
        }

        internal static string Info()
        {
            List<RemoteJson.Writer> list = new List<RemoteJson.Writer>();
            foreach (Card card in Cards())
                list.Add(new RemoteJson.Writer().Text("mac", card.Mac).Text("broadcast", card.Broadcast).Text("name", RemoteMessages.Clip(card.Name, 64)).Flag("wired", card.Wired).Text("ready", card.Ready));
            return new RemoteJson.Writer().Text("type", "wake.info").Text("os", Os()).Objects("cards", list).ToString();
        }

        internal static string Words(List<Card> cards)
        {
            if (cards.Count == 0) return "No network card to wake from. A network cable works best.";
            Card first = cards[0];
            string name = RemoteMessages.Clip(first.Description.Length > 0 ? first.Description : first.Name, 30);
            if (!first.Wired) return "On Wi-Fi, which rarely wakes a PC. A network cable works best.";
            if (first.Ready == "on") return "Ready on " + name + ". Phone on the same network only.";
            if (first.Ready == "off") return "Not turned on for " + name + ". See the guide.";
            return "Turn it on once with the guide. Phone on the same network only.";
        }

        private static string Os()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) return "windows";
            if (System.IO.Directory.Exists("/System/Library")) return "mac";
            return "linux";
        }

        private static List<Card> Find()
        {
            List<Card> found = new List<Card>();
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception) { return found; }
            foreach (NetworkInterface nic in all)
            {
                try
                {
                    Card card = Read(nic);
                    if (card != null && !found.Exists(delegate(Card seen) { return seen.Mac == card.Mac; })) found.Add(card);
                }
                catch (Exception) { }
            }
            found.Sort(delegate(Card a, Card b)
            {
                if (a.Routed != b.Routed) return a.Routed ? -1 : 1;
                if (a.Wired != b.Wired) return a.Wired ? -1 : 1;
                return string.CompareOrdinal(a.Name, b.Name);
            });
            if (found.Count > Most) found.RemoveRange(Most, found.Count - Most);
            return found;
        }

        private static Card Read(NetworkInterface nic)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) return null;
            NetworkInterfaceType kind = nic.NetworkInterfaceType;
            bool wireless = kind == NetworkInterfaceType.Wireless80211;
            bool wired = kind == NetworkInterfaceType.Ethernet || kind == NetworkInterfaceType.GigabitEthernet || kind == NetworkInterfaceType.FastEthernetT || kind == NetworkInterfaceType.FastEthernetFx || kind == NetworkInterfaceType.Ethernet3Megabit;
            if (!wired && !wireless) return null;
            string name = nic.Name ?? "", description = nic.Description ?? "";
            if (Virtual(name) || Virtual(description)) return null;
            if (wired && (name.StartsWith("wl", StringComparison.Ordinal) || description.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0 || description.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 || description.IndexOf("802.11", StringComparison.Ordinal) >= 0))
            {
                wired = false;
                wireless = true;
            }
            byte[] mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6 || (mac[0] & 1) != 0 || (mac[0] & 2) != 0) return null;
            bool blank = true;
            foreach (byte b in mac) if (b != 0) blank = false;
            if (blank) return null;
            IPInterfaceProperties props = nic.GetIPProperties();
            bool routed = false;
            foreach (GatewayIPAddressInformation way in props.GatewayAddresses)
                if (way.Address.AddressFamily == AddressFamily.InterNetwork && !way.Address.Equals(IPAddress.Any)) routed = true;
            foreach (UnicastIPAddressInformation address in props.UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] ip = address.Address.GetAddressBytes();
                if (ip[0] == 127 || ip[0] == 169 && ip[1] == 254) continue;
                byte[] mask = Mask(address);
                byte[] cast = new byte[4];
                for (int i = 0; i < 4; i++) cast[i] = (byte)(ip[i] | ~mask[i]);
                return new Card
                {
                    Mac = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]),
                    Broadcast = new IPAddress(cast).ToString(),
                    Address = address.Address.ToString(),
                    Name = name,
                    Description = description,
                    Wired = wired,
                    Routed = routed
                };
            }
            return null;
        }

        private static byte[] Mask(UnicastIPAddressInformation address)
        {
            try
            {
                IPAddress mask = address.IPv4Mask;
                if (mask != null && !mask.Equals(IPAddress.Any)) return mask.GetAddressBytes();
            }
            catch (Exception) { }
            return new byte[] { 255, 255, 255, 0 };
        }

        private static bool Virtual(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (string word in Virtuals) if (lower.Contains(word)) return true;
            return false;
        }

        private static void Arm(List<Card> cards)
        {
            if (cards.Count == 0) return;
            string os = Os();
            if (os == "windows")
            {
                string armed = Run("powercfg", "/devicequery wake_armed");
                if (armed == null) return;
                List<string> names = new List<string>();
                foreach (string line in armed.Split('\n')) if (line.Trim().Length > 0) names.Add(line.Trim());
                foreach (Card card in cards) card.Ready = names.Exists(delegate(string device) { return string.Equals(device, card.Description.Trim(), StringComparison.OrdinalIgnoreCase); }) ? "on" : "off";
            }
            else if (os == "mac")
            {
                string power = Run("pmset", "-g");
                if (power == null) return;
                string ready = "";
                foreach (string line in power.Split('\n'))
                {
                    string[] parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[0] == "womp") ready = parts[1] == "1" ? "on" : "off";
                }
                foreach (Card card in cards) card.Ready = ready;
            }
            else
                foreach (Card card in cards)
                {
                    if (card.Name.IndexOfAny(new[] { '/', ' ', '.' }) >= 0) continue;
                    string said = Run("ethtool", card.Name);
                    if (said == null) continue;
                    foreach (string line in said.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("Wake-on:", StringComparison.Ordinal)) card.Ready = trimmed.Substring(8).Trim().Contains("g") ? "on" : "off";
                    }
                }
        }

        private static string Run(string file, string arguments)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process process = Process.Start(start))
                {
                    if (process == null) return null;
                    System.Threading.Tasks.Task<string> read = process.StandardOutput.ReadToEndAsync();
                    if (!process.WaitForExit(4000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return null;
                    }
                    return process.ExitCode == 0 && read.Wait(1000) ? read.Result : null;
                }
            }
            catch (Exception) { return null; }
        }

        internal static string ArmCommand(List<Card> cards)
        {
            List<string> steps = new List<string>();
            foreach (Card card in cards)
            {
                if (!card.Wired || card.Description.Length == 0) continue;
                string device = card.Description.Replace("'", "''"), alias = card.Name.Replace("'", "''");
                steps.Add("powercfg /deviceenablewake '" + device + "'");
                steps.Add("try { Set-NetAdapterPowerManagement -Name '" + alias + "' -WakeOnMagicPacket Enabled -ErrorAction Stop } catch { }");
                steps.Add("try { Set-NetAdapterAdvancedProperty -Name '" + alias + "' -RegistryKeyword '*WakeOnMagicPacket' -RegistryValue 1 -ErrorAction Stop } catch { }");
            }
            return steps.Count == 0 ? null : string.Join("; ", steps.ToArray());
        }

        internal static void Forget() { lock (gate) kept = null; }
    }
}
