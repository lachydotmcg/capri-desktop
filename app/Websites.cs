using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CapriDesktop
{
    internal sealed class Websites
    {
        [DataContract]
        private sealed class Entry
        {
            [DataMember] internal string Name;
            [DataMember] internal string Address;
        }

        private readonly string path;
        private readonly object gate = new object();
        private readonly List<Entry> entries = new List<Entry>();
        internal Func<string> Current = FrontAddress;

        internal Websites(string folder)
        {
            path = Path.Combine(folder, "websites.json");
            try
            {
                if (!File.Exists(path)) return;
                using (FileStream saved = File.OpenRead(path))
                    foreach (Entry item in (List<Entry>)new DataContractJsonSerializer(typeof(List<Entry>)).ReadObject(saved))
                        if (!string.IsNullOrWhiteSpace(item.Name) && Address(item.Address) != null)
                            entries.Add(new Entry { Name = Key(item.Name), Address = Address(item.Address) });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
        }

        private static string Key(string name) { return Regex.Replace((name ?? "").Trim().Trim('"', '\'', '\u201c', '\u201d').Trim(), @"\s+", " ").ToLowerInvariant(); }

        internal string Find(string name)
        {
            string key = Key(name);
            lock (gate) foreach (Entry item in entries) if (item.Name == key) return item.Address;
            return null;
        }

        private static string Bare(string name) { return Key(Regex.Replace(Regex.Replace(Key(name), @"\s+(?:web\s?site|site)$", ""), @"^(?:my|the)\s+", "")); }

        // Her saved name wins first; "my school website" can then mean "school" (john_sandevistan, B88).
        internal string Nickname(string name)
        {
            string key = Key(name);
            string withoutSite = Regex.Replace(key, @"\s+(?:web\s?site|site)$", "", RegexOptions.IgnoreCase);
            foreach (string candidate in new[] { key, withoutSite, Regex.Replace(key, @"^(?:my|the)\s+", ""), Regex.Replace(withoutSite, @"^(?:my|the)\s+", "") })
                if (Find(candidate) != null) return Key(candidate);
            string bare = Bare(key), named = null;
            lock (gate)
                foreach (Entry item in entries)
                    if (bare.Length > 0 && Bare(item.Name) == bare)
                    {
                        if (named != null && named != item.Name) return null;
                        named = item.Name;
                    }
            return named;
        }

        internal static string Address(string text)
        {
            string value = (text ?? "").Trim();
            if (Regex.IsMatch(value, @"\s")) return null;
            if (!Regex.IsMatch(value, @"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)) value = "https://" + value;
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && (uri.Host.Contains(".") || uri.Host == "localhost") ? uri.AbsoluteUri : null;
        }

        internal string Do(ToolArguments args)
        {
            string name = Key(args.Name);
            if (args.Action == "forget") name = Nickname(name) ?? name;
            string address = args.Action == "list" || args.Action == "forget" ? null : Address(string.IsNullOrWhiteSpace(args.Site) ? Current() : args.Site);
            lock (gate)
            {
                if (args.Action == "list")
                {
                    List<string> lines = new List<string>();
                    foreach (Entry item in entries) lines.Add(item.Name + ": " + item.Address);
                    return lines.Count == 0 ? "You haven't given me any website nicknames yet." : "Your websites:\n" + string.Join("\n", lines.ToArray());
                }
                if (name.Length == 0) return "What should I call that website?";
                if (args.Action != "forget" && address == null) return "I couldn't read that browser's address. Tell me: remember " + name + " is example.com/page, with its address.";
                List<Entry> next = new List<Entry>(entries);
                int removed = next.RemoveAll(item => item.Name == name);
                if (args.Action == "forget" && removed == 0) return "I don't have a website called " + name + ".";
                if (args.Action != "forget") next.Add(new Entry { Name = name, Address = address });
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temporary = path + ".tmp";
                    using (FileStream saved = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<Entry>)).WriteObject(saved, next);
                    if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                    entries.Clear(); entries.AddRange(next);
                }
                catch (IOException) { return "Couldn't save your website nicknames just now."; }
                catch (UnauthorizedAccessException) { return "Couldn't save your website nicknames just now."; }
                return args.Action == "forget" ? "Forgot the " + name + " website." : "I'll remember " + name + " as " + address;
            }
        }

        internal static ToolArguments Request(string text)
        {
            if (Regex.IsMatch(text, @"^(?:what websites do you know|list (?:my |the )?websites|show (?:me )?(?:my |the )?websites)$", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "list" };
            Match current = Regex.Match(text, @"^(?:remember (?:this|the) (?:web)?site(?: that i(?:'m| am) (?:currently )?on)? as|call (?:this|the) (?:web)?site)\s+(.+)$", RegexOptions.IgnoreCase);
            if (current.Success) return new ToolArguments { Action = "save", Name = Key(current.Groups[1].Value) };
            Match explicitSite = Regex.Match(text, @"^remember\s+(.+?)\s+(?:is|as)\s+(\S+)$", RegexOptions.IgnoreCase);
            // "Remember my score is 12.5" is a note about them, so an address has to look like one.
            if (explicitSite.Success && Regex.IsMatch(explicitSite.Groups[2].Value, @"[a-z]{2}(?:[/:?#]\S*)?$|^(?:https?://)?\d+\.\d+\.\d+\.\d+", RegexOptions.IgnoreCase) && Address(explicitSite.Groups[2].Value) != null) return new ToolArguments { Action = "save", Name = Key(explicitSite.Groups[1].Value), Site = Address(explicitSite.Groups[2].Value) };
            Match forgotten = Regex.Match(text, @"^forget (?:the )?(.+?) (?:web)?site$", RegexOptions.IgnoreCase);
            return forgotten.Success ? new ToolArguments { Action = "forget", Name = Key(forgotten.Groups[1].Value) } : null;
        }

        internal static bool Browser(string program) { return Regex.IsMatch(program ?? "", @"^(?:chrome|msedge|firefox|opera|opera gx|brave|vivaldi)$", RegexOptions.IgnoreCase); }

        internal static IntPtr BrowserWindow(IntPtr front, string program, bool hers, IList<OpenWindow> windows)
        {
            if (Browser(program)) return front;
            if (hers) foreach (OpenWindow window in windows) if (Browser(window.Program)) return window.Handle;
            return IntPtr.Zero;
        }

        internal static int AddressScore(string id, string name, bool document)
        {
            if (document) return 0;
            if (Regex.IsMatch(id ?? "", @"^(?:urlbar(?:-input)?|address(?:Edit|Field|_field)?|omnibox)$", RegexOptions.IgnoreCase)) return 3;
            return Regex.IsMatch(name ?? "", @"address|search or enter|search with.*enter|adresse|direcci[oó]n|indirizzo|endere[cç]o", RegexOptions.IgnoreCase) ? 2 : 0;
        }

        internal static string FrontAddress()
        {
            try
            {
                IntPtr front = Hands.Foreground();
                if (front == IntPtr.Zero) return null;
                AutomationElement window = AutomationElement.FromHandle(front);
                using (Process process = Process.GetProcessById(window.Current.ProcessId))
                using (Process own = Process.GetCurrentProcess())
                    front = BrowserWindow(front, process.ProcessName, process.Id == own.Id, process.Id == own.Id ? Hands.Windows() : new List<OpenWindow>());
                if (front == IntPtr.Zero) return null;
                window = AutomationElement.FromHandle(front);
                string address = null;
                int best = 0;
                foreach (AutomationElement edit in window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
                {
                    if (edit.Current.IsPassword || edit.Current.IsOffscreen) continue;
                    bool document = false;
                    AutomationElement parent = TreeWalker.ControlViewWalker.GetParent(edit);
                    while (parent != null && !Automation.Compare(parent, window))
                    {
                        if (parent.Current.ControlType == ControlType.Document) { document = true; break; }
                        parent = TreeWalker.ControlViewWalker.GetParent(parent);
                    }
                    int score = AddressScore(edit.Current.AutomationId, edit.Current.Name, document);
                    object pattern;
                    if (score <= best || !edit.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) continue;
                    string candidate = Address(((ValuePattern)pattern).Current.Value);
                    if (candidate == null) continue;
                    address = candidate; best = score;
                }
                return address;
            }
            catch (Exception) { return null; }
        }
    }
}
