using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class MacRoutes
    {
        internal static void Attach(bool mac)
        {
            Hands.NativeDirect = mac ? null : LinuxRoutes.Direct;
            Hands.NativeRequest = mac ? Request : LinuxRoutes.Request;
            Hands.NativeWindow = mac ? null : LinuxRoutes.Window;
            Hands.NativeSystem = mac ? null : LinuxRoutes.System;
            Hands.NativeWebsite = mac ? Website : LinuxRoutes.Website;
        }

        internal static KeyValuePair<string, ToolArguments> Request(string text)
        {
            KeyValuePair<string, ToolArguments> request = LinuxRoutes.Request(text);
            return request.Key == "terminal_job" ? request : default;
        }

        internal static string Website(ToolArguments args)
        {
            if (!Uri.TryCreate(args.Site, UriKind.Absolute, out Uri url) || !url.IsLoopback) return null;
            Match explicitUrl = Regex.Match(args.Asked ?? "", @"^(?:please )?(?:open|visit|show)(?: me)?\s+(https?://\S+?)(?: please)?$", RegexOptions.IgnoreCase);
            if ((url.Scheme != "http" && url.Scheme != "https") || url.UserInfo.Length != 0 || !string.IsNullOrEmpty(args.Search)
                || !explicitUrl.Success || !Uri.TryCreate(explicitUrl.Groups[1].Value, UriKind.Absolute, out Uri requested) || requested.AbsoluteUri != url.AbsoluteUri)
                return "Name the exact http or https loopback URL in an explicit open request.";
            if (args.Current != null && !args.Current()) return "The website request was cancelled.";
            return Music.Run("/usr/bin/open", new[] { url.AbsoluteUri }) != null
                ? "Opened the requested local URL in your default browser."
                : "Couldn't open that local URL in your default browser.";
        }

        internal static void Checks(List<string> checks)
        {
            Func<string, ToolArguments> directWas = Hands.NativeDirect;
            Func<string, KeyValuePair<string, ToolArguments>> requestWas = Hands.NativeRequest;
            Func<ToolArguments, string> windowWas = Hands.NativeWindow, systemWas = Hands.NativeSystem, websiteWas = Hands.NativeWebsite;
            Func<string, string[], string> runWas = Music.Run;
            Func<ToolArguments, string> jobWas = Hands.NativeTerminalJob;
            Func<bool> terminalWas = Hands.TerminalAvailable;
            Func<bool> quietWas = Watching.Quiet;
            Action<string> openWas = Hands.Run;
            bool macWas = Hands.OnMac;
            var commands = new List<string>();
            try
            {
                Watching.Quiet = () => true;
                foreach (bool mac in new[] { false, true })
                {
                    Hands.OnMac = mac;
                    Hands.Kit kit = Hands.KitFor();
                    string[] names = { "watch_for", "fake_crash", "speech", "job_control", "quit", "updates", "mini_player" };
                    string[] actions = { "list", "show", "stop", "status", "quit", "check", "show" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        string marker = "\"name\":\"" + names[i] + "\"";
                        if (!kit.Has(names[i]) || !kit.Tools.Contains(marker) || !kit.ClaudeTools.Contains(marker)
                            || !Hands.Valid(names[i], new ToolArguments { Action = actions[i] })
                            || Hands.Valid(names[i], null) || Hands.Valid(names[i], new ToolArguments { Action = "invalid" }))
                            throw new Exception("A cross tool lost its catalog row or argument checks: " + names[i]);
                    }
                    foreach (string name in new[] { "desktop_icons", "watch_along", "gaming_mode", "clip", "mini_game", "voice_chat", "listen" })
                        if (kit.Has(name) || kit.Tools.Contains("\"name\":\"" + name + "\"")) throw new Exception("A Windows tool was exposed: " + name);
                    Hands.TerminalAvailable = () => false;
                    foreach (string name in new[] { "run_terminal", "terminal_job" })
                        if (Hands.KitFor().Has(name) || Hands.Unable(name, new ToolArguments()) == null) throw new Exception("An unavailable terminal tool had no refusal: " + name);
                    Hands.TerminalAvailable = null;
                    foreach (string name in new[] { "run_terminal", "terminal_job" })
                        if (Hands.Unable(name, new ToolArguments()) == null) throw new Exception("An unwired terminal tool had no refusal: " + name);
                }
                checks.Add("PASS seven cross tools reach both catalogs and validate arguments; Windows tools and unavailable terminals stay refused");
                Music.Run = (file, args) =>
                {
                    if (file != "/usr/bin/open" || args.Length != 1) throw new Exception("The Mac local URL command changed.");
                    commands.Add(args[0]);
                    return "";
                };
                Hands.TerminalAvailable = () => true;
                Hands.NativeTerminalJob = args => args.Action + " " + args.JobId;
                Attach(true); Hands.OnMac = true;
                if (Hands.NativeRequest != Request || Hands.NativeWebsite != Website || Hands.NativeDirect != null || Hands.NativeWindow != null || Hands.NativeSystem != null)
                    throw new Exception("The Mac routes were not attached.");
                string id = "0123456789abcdef0123456789abcdef";
                foreach (string verb in new[] { "read", "inspect", "check", "show status of", "stop", "cancel", "terminate" })
                {
                    string asked = "Please could you " + verb + " the terminal job " + id + " please?";
                    KeyValuePair<string, ToolArguments> parsed = Request(asked);
                    string action = verb == "stop" || verb == "cancel" || verb == "terminate" ? "stop" : "read";
                    if (parsed.Key != "terminal_job" || parsed.Value.Action != action || parsed.Value.JobId != id || parsed.Value.Asked != asked
                        || !Hands.Direct(asked, out string tool, out ToolArguments routed) || tool != "terminal_job"
                        || Hands.Do(tool, routed) != action + " " + id)
                        throw new Exception("A Mac terminal job request lost its route.");
                }
                foreach (string asked in new[] { "read job 1234", "don't stop job " + id, "how do I stop job " + id, "read job " + id + " then stop it", "check cpu and memory", "show status of example.service" })
                    if (Request(asked).Key != null) throw new Exception("A Mac request became an unrelated native action.");
                if (Hands.Unable("linux_system", new ToolArguments { Action = "status" }) == null)
                    throw new Exception("Mac advertised Linux system tools.");
                checks.Add("PASS Mac terminal job requests use the existing executor and leave Linux system tools unavailable");

                foreach (string site in new[] { "http://localhost:3000/", "http://127.0.0.1:8080/page?q=a%20b", "https://[::1]:8443/" })
                {
                    string done = Hands.Do("open_website", new ToolArguments { Site = site, Asked = "please open " + site + " please" });
                    if (done != "Opened the requested local URL in your default browser." || commands[commands.Count - 1] != new Uri(site).AbsoluteUri)
                        throw new Exception("A Mac local URL lost its exact open argument.");
                }
                int count = commands.Count;
                foreach (ToolArguments args in new[] {
                    new ToolArguments { Site = "http://localhost:3000/", Asked = "tell me about localhost" },
                    new ToolArguments { Site = "http://localhost:3000/", Asked = "open http://localhost:4000/" },
                    new ToolArguments { Site = "http://user:password@localhost/", Asked = "open http://user:password@localhost/" },
                    new ToolArguments { Site = "ftp://localhost/", Asked = "open ftp://localhost/" },
                    new ToolArguments { Site = "http://localhost/", Asked = "open http://localhost/", Search = "cats" },
                    new ToolArguments { Site = "http://localhost/", Asked = "open http://localhost/", Current = () => false }
                })
                    if (Website(args) == null || commands.Count != count) throw new Exception("An unqualified Mac local URL was opened.");
                if (Website(new ToolArguments { Site = "https://example.com/", Asked = "open https://example.com/" }) != null
                    || Website(new ToolArguments { Site = "not a URL" }) != null || commands.Count != count)
                    throw new Exception("Mac intercepted a normal website request.");
                Music.Run = (file, args) => null;
                if (Website(new ToolArguments { Site = "http://localhost/", Asked = "open http://localhost/" }) != "Couldn't open that local URL in your default browser.")
                    throw new Exception("A failed Mac browser launch was reported as opened.");
                checks.Add("PASS Mac local URLs require the exact request, honor cancellation and check the open command reply");

                Attach(false); Hands.OnMac = false;
                if (Hands.NativeDirect != LinuxRoutes.Direct || Hands.NativeRequest != LinuxRoutes.Request || Hands.NativeWindow != LinuxRoutes.Window
                    || Hands.NativeSystem != LinuxRoutes.System || Hands.NativeWebsite != LinuxRoutes.Website
                    || Hands.NativeRequest("check cpu and memory").Key != "linux_system"
                    || Hands.Unable("linux_system", new ToolArguments { Action = "status" }) != null)
                    throw new Exception("Linux lost its native routes.");
                string opened = null;
                Hands.Run = site => opened = site;
                Hands.NativeWebsite(new ToolArguments { Site = "http://localhost:3000/", Asked = "open http://localhost:3000/" });
                if (opened != "http://localhost:3000/" || commands.Count != count) throw new Exception("Linux took the Mac browser route.");
                checks.Add("PASS Linux keeps all five native routes and its existing local browser launch");
            }
            finally
            {
                Hands.NativeDirect = directWas; Hands.NativeRequest = requestWas;
                Hands.NativeWindow = windowWas; Hands.NativeSystem = systemWas; Hands.NativeWebsite = websiteWas;
                Music.Run = runWas; Hands.NativeTerminalJob = jobWas; Hands.TerminalAvailable = terminalWas;
                Watching.Quiet = quietWas;
                Hands.Run = openWas; Hands.OnMac = macWas;
            }
        }
    }
}
