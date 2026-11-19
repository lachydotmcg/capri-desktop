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
            new[] { MailStarter, "Email", "Checks, searches and reads your email, and writes mail for you. Works with Gmail, iCloud, Yahoo, Fastmail and others through an app password.", "\"mail\":{\"type\":\"capri\",\"builtin\":\"mail\"}", "\"accounts\",\"send\",\"network\"", "\"send_mail\"" },
            new[] { "notion", "Notion", "Searches, reads and edits your Notion pages and databases. You sign in to Notion in your browser.", "\"notion\":{\"type\":\"http\",\"url\":\"https://mcp.notion.com/mcp\"}", "\"accounts\",\"network\"", "" },
            new[] { "linear", "Linear", "Finds, makes and updates your Linear issues and projects. You sign in to Linear in your browser.", "\"linear\":{\"type\":\"http\",\"url\":\"https://mcp.linear.app/mcp\"}", "\"accounts\",\"network\"", "" }
        };

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

        internal static string MailSetupHelp(string address)
        {
            string domain = MailAccount.Domain(address ?? "");
            if (domain == "gmail.com" || domain == "googlemail.com") return "Gmail: turn on 2-Step Verification, then make an app password at myaccount.google.com/apppasswords.";
            if (domain == "icloud.com" || domain == "me.com" || domain == "mac.com") return "iCloud: make an app-specific password at account.apple.com, under Sign-In and Security.";
            if (domain.StartsWith("yahoo.", StringComparison.Ordinal) || domain == "aol.com") return "Yahoo and AOL: make an app password under Account Security in your account settings.";
            if (domain == "fastmail.com" || domain == "fastmail.fm") return "Fastmail: make an app password under Settings, Privacy & Security.";
            return "Make an app password in your email account's security settings. Capri never uses your normal password.";
        }
    }
}
