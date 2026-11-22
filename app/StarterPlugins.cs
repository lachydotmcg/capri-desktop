using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CapriDesktop
{
    internal sealed partial class Mods
    {
        internal static bool WithStarters;
        internal const string MailStarter = "email";

        private static readonly string[][] Starters =
        {
            new[] { MailStarter, "Email", "Checks, searches and reads your email, and writes mail for you. Works with Gmail, iCloud, Yahoo, Fastmail and others through an app password.", "\"mail\":{\"type\":\"capri\",\"builtin\":\"mail\"}", "\"accounts\",\"send\",\"network\"", "\"send_mail\"", "Checks, reads and writes your email." },
            new[] { "notion", "Notion", "Searches, reads and edits your Notion pages and databases. You sign in to Notion in your browser.", "\"notion\":{\"type\":\"http\",\"url\":\"https://mcp.notion.com/mcp\"}", "\"accounts\",\"network\"", "", "Searches, reads and edits your Notion pages." },
            new[] { "linear", "Linear", "Finds, makes and updates your Linear issues and projects. You sign in to Linear in your browser.", "\"linear\":{\"type\":\"http\",\"url\":\"https://mcp.linear.app/mcp\"}", "\"accounts\",\"network\"", "", "Finds, makes and updates your Linear issues." }
        };

        internal const int Envelope = 0, Page = 1, Checklist = 2, Plug = 3;

        internal sealed class MailProvider
        {
            internal string Name, Tint, Page, PageKey, Note;
            internal string[] Domains = new string[0], Steps = new string[0];
            internal bool Closed, Other;
        }

        internal static readonly MailProvider[] MailProviders =
        {
            new MailProvider { Name = "Gmail", Tint = "#FF5D66", Domains = new[] { "gmail.com", "googlemail.com" }, Page = "https://myaccount.google.com/apppasswords", PageKey = "Open Gmail's app password page",
                Steps = new[] { "Turn on 2-Step Verification in your Google Account.", "Open the page below and make an app password called Capri.", "Copy the 16 letters it shows you." } },
            new MailProvider { Name = "iCloud", Tint = "#5EF0F6", Domains = new[] { "icloud.com", "me.com", "mac.com" }, Page = "https://account.apple.com", PageKey = "Open Apple's account page",
                Steps = new[] { "Two-factor authentication needs to be on.", "Open the page below, then Sign-In and Security.", "Pick App-Specific Passwords, make one called Capri and copy it." } },
            new MailProvider { Name = "Yahoo", Tint = "#7854F0", Domains = new[] { "yahoo.com", "yahoo.", "ymail.com", "rocketmail.com" }, Page = "https://login.yahoo.com/account/security", PageKey = "Open Yahoo's security page",
                Steps = new[] { "Open the page below and sign in.", "Under External connections, press Create app password.", "Type Capri, press Generate password and copy it." } },
            new MailProvider { Name = "AOL", Tint = "#38B0FF", Domains = new[] { "aol.com" }, Page = "https://login.aol.com/myaccount/security", PageKey = "Open AOL's security page",
                Steps = new[] { "Open the page below and sign in.", "Press Generate app password, then Get Started.", "Type Capri, press Generate password and copy it." } },
            new MailProvider { Name = "Fastmail", Tint = "#F4D646", Domains = new[] { "fastmail.com", "fastmail.fm" }, Page = "https://app.fastmail.com/settings/security", PageKey = "Open Fastmail's security page",
                Steps = new[] { "App passwords need a paid Fastmail plan.", "On the page below, press Manage app passwords, then New app password.", "Keep Mail, Contacts & Calendars, press Generate password and copy it." } },
            new MailProvider { Name = "Outlook / Hotmail", Tint = "#92828C", Domains = new[] { "outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.", "hotmail." }, Closed = true, Note = MailAccount.OutlookNote },
            new MailProvider { Name = "Other", Tint = "#00E86E", Other = true,
                Steps = new[] { "Open your email's security settings.", "Make an app password called Capri.", "Copy it. Your email's help pages name its IMAP and SMTP servers." } }
        };

        internal const string AppPasswordLine = "An app password is a separate password just for Capri. Your normal password stays yours.";

        internal static MailProvider OtherMail { get { return MailProviders[MailProviders.Length - 1]; } }

        internal static MailProvider ProviderFor(string address)
        {
            string domain = MailAccount.Domain(address ?? "");
            if (domain.Length == 0) return null;
            foreach (MailProvider provider in MailProviders)
                foreach (string one in provider.Domains)
                    if (domain == one || (one.EndsWith(".", StringComparison.Ordinal) && domain.StartsWith(one, StringComparison.Ordinal) && domain.Length > one.Length)) return provider;
            int dot = domain.LastIndexOf('.');
            return dot > 0 && dot < domain.Length - 2 ? OtherMail : null;
        }

        internal static bool HostsShown(MailProvider picked, string address)
        {
            if (picked == null || picked.Closed) return false;
            if (NeedsHosts(address) && ProviderFor(address) != null) return true;
            return picked.Other && ProviderFor(address) == null;
        }

        internal static string PluginLine(Mod mod)
        {
            if (mod.Starter) foreach (string[] starter in Starters) if (starter[0] == mod.File.Id) return starter[6];
            return mod.File.Description ?? "";
        }

        internal static int PluginPicture(Mod mod)
        {
            if (Builtin(mod, "mail")) return Envelope;
            if (mod.Starter && mod.File.Id == "notion") return Page;
            if (mod.Starter && mod.File.Id == "linear") return Checklist;
            return Plug;
        }

        internal static string PluginState(Mod mod)
        {
            if (!mod.On) return "Off";
            if (Builtin(mod, "mail")) return MailTools.Ready ? "Connected" : "Set up";
            if (!Online(mod)) return "On";
            if (Plugins.Trouble(mod.File.Id) == McpSignInNeeded.Said) return "Sign in";
            return Plugins.SignedIn(mod.File.Id) ? "Connected" : "On";
        }

        internal static List<Mod> PluginsOf(List<Mod> all)
        {
            List<Mod> found = all.FindAll(delegate(Mod mod) { return mod.Plugin; });
            List<Mod> starters = found.FindAll(delegate(Mod mod) { return mod.Starter; });
            starters.Sort(delegate(Mod one, Mod other) { return StarterAt(one).CompareTo(StarterAt(other)); });
            starters.AddRange(found.FindAll(delegate(Mod mod) { return !mod.Starter; }));
            return starters;
        }

        private static int StarterAt(Mod mod)
        {
            for (int i = 0; i < Starters.Length; i++) if (Starters[i][0] == mod.File.Id) return i;
            return Starters.Length;
        }

        private string StartersFile { get { return Path.Combine(pluginFolder, "starters.txt"); } }

        private Dictionary<string, string> StartersMade()
        {
            Dictionary<string, string> made = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(StartersFile)) return made;
                foreach (string line in File.ReadAllLines(StartersFile))
                {
                    string[] parts = line.Trim().Split(' ');
                    if (parts.Length == 2) made[parts[0]] = parts[1];
                }
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            return made;
        }

        private bool IsStarter(string id, string hash)
        {
            string made;
            return StartersMade().TryGetValue(id, out made) && made == hash;
        }

        internal static bool Builtin(Mod mod, string name)
        {
            if (mod == null || !mod.Plugin || mod.Info == null) return false;
            foreach (PluginServer server in mod.Info.Servers) if (server.BuiltIn == name) return true;
            return false;
        }

        internal static bool Online(Mod mod)
        {
            if (mod == null || !mod.Plugin || mod.Info == null) return false;
            foreach (PluginServer server in mod.Info.Servers) if (server.Url != null) return true;
            return false;
        }

        internal void AddStarters()
        {
            Dictionary<string, string> made = StartersMade();
            bool wrote = false;
            foreach (string[] starter in Starters)
            {
                string id = starter[0], file = Path.Combine(folder, id + PluginExtension);
                if (made.ContainsKey(id) || File.Exists(file)) continue;
                try
                {
                    Directory.CreateDirectory(folder);
                    string manifest = "{\"name\":\"" + id + "\",\"displayName\":\"" + starter[1] + "\",\"version\":\"1.0.0\",\"author\":{\"name\":\"Capri\"},\"description\":" + PluginJson.Quote(starter[2])
                        + ",\"mcpServers\":{" + starter[3] + "},\"capri\":{\"permissions\":[" + starter[4] + "]" + (starter[5].Length == 0 ? "" : ",\"ask\":[" + starter[5] + "]") + "}}";
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        using (ZipArchive zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                        {
                            ZipArchiveEntry entry = zip.CreateEntry(".claude-plugin/plugin.json");
                            entry.LastWriteTime = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
                            using (Stream into = entry.Open())
                            {
                                byte[] text = new UTF8Encoding(false).GetBytes(manifest);
                                into.Write(text, 0, text.Length);
                            }
                        }
                        byte[] all = bytes.ToArray();
                        File.WriteAllBytes(file, all);
                        made[id] = HashOf(all);
                        wrote = true;
                    }
                }
                catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            }
            if (!wrote) return;
            try
            {
                Directory.CreateDirectory(pluginFolder);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> pair in made) lines.Add(pair.Key + " " + pair.Value);
                File.WriteAllLines(StartersFile, lines.ToArray());
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        internal static string StarterAbout(Mod mod)
        {
            if (!mod.Starter || mod.On) return null;
            if (Builtin(mod, "mail")) return "STARTER. Turn it on and give her an app password. ";
            if (Online(mod)) return "STARTER. Turn it on and sign in, in your browser. ";
            return "STARTER. ";
        }

        internal static MailAccount MailFromForm(string address, string password, string incoming, string outgoing, out string problem)
        {
            address = (address ?? "").Trim();
            problem = MailAccount.Unsupported(address);
            if (problem != null) return null;
            MailAccount account = MailAccount.ForAddress(address);
            if (!account.Known)
            {
                int port;
                string host = Host(incoming, 993, out port);
                account.ImapHost = host;
                account.ImapPort = port;
                host = Host(outgoing, 587, out port);
                account.SmtpHost = host;
                account.SmtpPort = port;
                account.SmtpImplicitTls = port == 465;
                account.Password = password ?? "";
            }
            else account.Password = (password ?? "").Replace(" ", "");
            return account;
        }

        private static string Host(string typed, int usual, out int port)
        {
            string text = (typed ?? "").Trim();
            port = usual;
            int colon = text.LastIndexOf(':');
            int given;
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out given) && given > 0 && given < 65536) { port = given; text = text.Substring(0, colon); }
            return text;
        }

        internal static bool NeedsHosts(string address)
        {
            address = (address ?? "").Trim();
            return address.IndexOf('@') > 0 && MailAccount.Unsupported(address) == null && !MailAccount.ForAddress(address).Known;
        }
    }
}
