using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // Their connection right now: the links that are up, like Ethernet or Wi-Fi, how a few pings to a public address went, and
    // whether web addresses look up.
    internal sealed class NetUse
    {
        internal List<string> Links = new List<string>();
        internal string Pinged;
        internal int Sent, Lost;
        internal long Fastest, Average;
        internal bool? Names;
    }

    // Reads their connection the way they'd check it themselves, a few pings and a name lookup, without changing anything
    // (Lachy, 3 Nov: "ensure they have the tools they need to perform everything themself").
    internal static class Network
    {
        // Pings the address they gave, or two public ones, the second only when the first never answers, and looks a name up.
        internal static NetUse Read(string address)
        {
            NetUse use = new NetUse();
            use.Links.AddRange(Links());
            if (use.Links.Count == 0) return use;
            string asked = (address ?? "").Trim();
            Uri site;
            if (asked.Length > 0 && Uri.TryCreate(asked.Contains("://") ? asked : "http://" + asked, UriKind.Absolute, out site)) asked = site.Host;
            // A name is looked up once, for at most 2.5 seconds, before any ping: a ping to a name looks it up again with no time
            // limit of its own, so a DNS server that stopped answering could hold the check up.
            string named = asked.Length > 0 && Uri.CheckHostName(asked) == UriHostNameType.Dns ? asked : "www.google.com";
            IPAddress[] found = Lookup(named);
            use.Names = found != null && found.Length > 0;
            foreach (string host in asked.Length > 0 ? new[] { asked } : new[] { "1.1.1.1", "8.8.8.8" })
            {
                IPAddress target;
                if (!IPAddress.TryParse(host, out target))
                    target = host == named && use.Names == true ? Array.Find(found, one => one.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? found[0] : null;
                use.Pinged = host;
                use.Sent = 0;
                use.Lost = 0;
                long total = 0;
                use.Fastest = 0;
                using (Ping ping = new Ping())
                {
                    // Two lost in a row from the start means nothing's answering there, so it doesn't wait out all four.
                    for (int i = 0; i < 4 && !(i == 2 && use.Lost == 2); i++)
                    {
                        use.Sent++;
                        if (target == null) { use.Lost++; continue; }
                        try
                        {
                            PingReply reply = ping.Send(target, 1000);
                            if (reply == null || reply.Status != IPStatus.Success) { use.Lost++; continue; }
                            total += reply.RoundtripTime;
                            if (use.Fastest == 0 || reply.RoundtripTime < use.Fastest) use.Fastest = reply.RoundtripTime;
                        }
                        catch (Exception) { use.Lost++; }
                    }
                }
                use.Average = use.Sent > use.Lost ? total / (use.Sent - use.Lost) : 0;
                if (use.Lost < use.Sent) break;
            }
            return use;
        }

        // A name's addresses, or null when it doesn't look up within 2.5 seconds.
        private static IPAddress[] Lookup(string name)
        {
            Task<IPAddress[]> lookup = Task.Factory.StartNew(delegate { return Dns.GetHostAddresses(name); });
            try { return lookup.Wait(2500) ? lookup.Result : null; }
            catch (Exception) { return null; }
        }

        // The network links that are up, Ethernet or Wi-Fi, never virtual or VPN ones.
        internal static List<string> Links()
        {
            List<string> links = new List<string>();
            try
            {
                foreach (NetworkInterface link in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (link.OperationalStatus != OperationalStatus.Up) continue;
                    string described = (link.Description + " " + link.Name).ToLowerInvariant();
                    if (described.Contains("virtual") || described.Contains("vpn") || described.Contains("hyper-v") || described.Contains("vmware") || described.Contains("virtualbox") || described.Contains("loopback") || described.Contains("bluetooth")) continue;
                    string kind = link.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : link.NetworkInterfaceType == NetworkInterfaceType.Ethernet || link.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet || link.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ? "Ethernet" : null;
                    if (kind != null && !links.Contains(kind)) links.Add(kind);
                }
            }
            catch (Exception) { }
            return links;
        }
    }
}
