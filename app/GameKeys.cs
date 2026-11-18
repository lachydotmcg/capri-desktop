using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace CapriDesktop
{
    // The keys a game is really set to, from the files it writes itself. She only ever reads them.
    internal static class GameKeys
    {
        private static readonly string[,] CyberpunkMoves =
        {
            { "forward", "move forward" }, { "back", "move back" }, { "left", "move left" }, { "right", "move right" },
            { "jump", "jump" }, { "crouchToggle", "crouch" }, { "sprintToggle", "sprint" }, { "dodgeDash", "dodge" },
            { "selectChoice", "interact" }, { "reload", "reload" }, { "attack", "shoot" }, { "aim", "aim" },
            { "visionHold", "scanner" }, { "map", "map" }, { "journal", "journal" }, { "inventory", "inventory" },
            { "callVehicle", "call vehicle" }, { "vehicleAutodrive", "with your weapon put away, hold for 0.7 seconds in a car to auto drive to your waypoint" }, { "vehicleHandbrake", "handbrake" }, { "exitVehicle", "get out of a car" }, { "useConsumable", "heal" }, { "openPhone", "phone" }, { "holsterWeapon", "holster" }, { "quickSave", "quick save" }
        };

        internal static string Summary(string game)
        {
            return Summary(game, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        internal static string Summary(string game, string roaming, string local)
        {
            string name = game ?? "";
            try
            {
                if (Regex.IsMatch(name, @"\bminecraft\b|\bjavaw\b", RegexOptions.IgnoreCase)) return Minecraft(Path.Combine(roaming, ".minecraft", "options.txt"));
                if (Regex.IsMatch(name, @"\bcyberpunk\s*2077\b", RegexOptions.IgnoreCase)) return Cyberpunk(Path.Combine(local, "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json"));
                if (Regex.IsMatch(name, @"\bfortnite\b", RegexOptions.IgnoreCase)) return Unreal(Path.Combine(local, "FortniteGame", "Saved"));
                string folder = Regex.Replace(name, @"[^A-Za-z0-9]", "");
                if (folder.Length >= 3) return Unreal(Path.Combine(local, folder, "Saved"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (XmlException) { }
            return null;
        }

        private static string Minecraft(string path)
        {
            List<string> keys = new List<string>();
            foreach (string line in Regex.Split(Read(path), "\r?\n"))
            {
                Match binding = Regex.Match(line, @"^key_key\.([^:]+):(.+)$");
                if (!binding.Success || Regex.IsMatch(binding.Groups[1].Value, "^(hotbar|debug|spectator|toggleSpectator|saveToolbar|loadToolbar|social|friends|quickActions|screenshot|fullscreen)")) continue;
                string key = Key(binding.Groups[2].Value);
                if (key != null) keys.Add(Words(binding.Groups[1].Value) + " " + key);
            }
            return keys.Count == 0 ? null : string.Join(", ", keys.ToArray());
        }

        private static string Cyberpunk(string path)
        {
            XmlDocument settings = new XmlDocument();
            settings.XmlResolver = null;
            using (XmlDictionaryReader json = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(Read(path)), XmlDictionaryReaderQuotas.Max)) settings.Load(json);
            Dictionary<string, string> bound = new Dictionary<string, string>();
            foreach (XmlNode group in settings.SelectNodes("//*[starts-with(group_name, '/key_bindings/')]"))
                foreach (XmlNode option in group.SelectNodes("options/item"))
                {
                    XmlNode name = option.SelectSingleNode("name");
                    XmlNode value = option.SelectSingleNode("value");
                    if (name != null && value != null && !bound.ContainsKey(name.InnerText)) bound[name.InnerText] = value.InnerText;
                }
            List<string> keys = new List<string>();
            for (int i = 0; i < CyberpunkMoves.GetLength(0); i++)
            {
                string value;
                if (!bound.TryGetValue(CyberpunkMoves[i, 0], out value)) continue;
                string key = Key(value);
                if (key != null) keys.Add(CyberpunkMoves[i, 1] + " " + key);
            }
            return keys.Count == 0 ? null : string.Join(", ", keys.ToArray());
        }

        private static readonly string[,] UnrealMoves =
        {
            { @"^(move)?forwards?$|^moveup$", "move forward" }, { @"^(move)?back(ward)?s?$|^movedown$", "move back" }, { @"^(move|strafe)left$", "move left" }, { @"^(move|strafe)right$", "move right" },
            { @"sprint|^run$|^togglerun$", "sprint" }, { @"crouch|^duck$|^slide$", "crouch" }, { @"^jump$|^jumpaction$", "jump" }, { @"walk$", "walk slowly" },
            { @"^(use|interact|pickup|search|open)", "interact" }, { @"^reload", "reload" }, { @"^(fire|shoot|attack)$|^primaryfire$", "shoot" },
            { @"^(target|aim|ads|zoom|secondaryfire)$", "aim" }, { @"harvest|pickaxe", "pickaxe" }, { @"^(map|togglemap|openmap)$", "map" },
            { @"inventory", "inventory" }, { @"^(heal|consumable)", "heal" }, { @"^mantle$|^climb$", "mantle" }
        };

        private static readonly Regex UnrealKey = new Regex(@"^(?:[A-Z]|One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Zero|SpaceBar|Left(?:Shift|Control|Alt)|Right(?:Shift|Control|Alt)|Tab|CapsLock|Escape|Enter|Tilde|BackSpace|Delete|Insert|Home|End|PageUp|PageDown|F\d{1,2}|(?:Left|Right|Middle)MouseButton|ThumbMouseButton2?|MouseScroll(?:Up|Down)|NumPad\w+|Up|Down|Left|Right)$");
        private static readonly Regex UnrealNoise = new Regex(@"Property$|^(None|Key|KeyName|CurrentKey|DefaultKey|PrimaryKey|SecondaryKey|Keys|Mappings|ActionMappings|AxisMappings|Name|DisplayName|MappingName|ActionName|AxisName|Slot|Scale|bShift|bCtrl|bAlt|bCmd|Guid|Struct|Value|Profile\w*|PlayerMappedKeys|ProfileIdString)$", RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, KeyValuePair<DateTime, string>> unrealRead = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);

        private static string Unreal(string saved)
        {
            if (!Directory.Exists(saved)) return null;
            lock (unrealRead)
            {
                KeyValuePair<DateTime, string> kept;
                if (unrealRead.TryGetValue(saved, out kept) && DateTime.UtcNow < kept.Key) return kept.Value;
            }
            string read = UnrealRead(saved);
            lock (unrealRead) unrealRead[saved] = new KeyValuePair<DateTime, string>(DateTime.UtcNow.AddSeconds(30), read);
            return read;
        }

        private static string UnrealRead(string saved)
        {
            Dictionary<string, string> bound = new Dictionary<string, string>();
            foreach (string kind in new[] { "WindowsClient", "Windows", "WindowsNoEditor" })
            {
                string config = Path.Combine(saved, Path.Combine("Config", kind));
                foreach (string file in new[] { "Input.ini", "GameUserSettings.ini" })
                    if (File.Exists(Path.Combine(config, file))) UnrealIni(Read(Path.Combine(config, file)), bound);
            }
            List<string> saves = new List<string>();
            foreach (string place in new[] { "Cloud", "SaveGames" })
                if (Directory.Exists(Path.Combine(saved, place))) saves.AddRange(Directory.GetFiles(Path.Combine(saved, place), "*.sav", SearchOption.AllDirectories));
            saves.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            foreach (string save in saves)
            {
                if (new FileInfo(save).Length > 16 * 1024 * 1024) continue;
                byte[] data;
                using (FileStream file = new FileStream(save, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    data = new byte[file.Length];
                    int at = 0, got;
                    while (at < data.Length && (got = file.Read(data, at, data.Length - at)) > 0) at += got;
                }
                UnrealSave(data, bound);
            }
            List<string> keys = new List<string>();
            for (int i = 0; i < UnrealMoves.GetLength(0); i++)
            {
                string key;
                if (bound.TryGetValue(UnrealMoves[i, 1], out key) && !keys.Contains(UnrealMoves[i, 1] + " " + key)) keys.Add(UnrealMoves[i, 1] + " " + key);
            }
            return keys.Count < 2 ? null : string.Join(", ", keys.ToArray());
        }

        private static void Bind(Dictionary<string, string> bound, string action, string key)
        {
            string plain = Regex.Replace(action ?? "", @"^(IA_|In_|Input_|Fort_?|Default_?|Action_?)", "", RegexOptions.IgnoreCase);
            plain = Regex.Replace(plain.Substring(plain.LastIndexOf('.') + 1), @"[^A-Za-z]", "").ToLowerInvariant();
            if (plain.Length == 0 || !UnrealKey.IsMatch(key ?? "")) return;
            for (int i = 0; i < UnrealMoves.GetLength(0); i++)
                if (Regex.IsMatch(plain, UnrealMoves[i, 0]))
                {
                    if (!bound.ContainsKey(UnrealMoves[i, 1])) bound[UnrealMoves[i, 1]] = Key(key);
                    return;
                }
        }

        private static void UnrealIni(string text, Dictionary<string, string> bound)
        {
            foreach (Match line in Regex.Matches(text, @"^[+]?(?:ActionMappings|AxisMappings)=\((.*)\)\s*$", RegexOptions.Multiline))
            {
                Match action = Regex.Match(line.Groups[1].Value, @"(?:ActionName|AxisName)=""?([^"",)]+)");
                Match key = Regex.Match(line.Groups[1].Value, @"\bKey=""?([^"",)]+)");
                Match scale = Regex.Match(line.Groups[1].Value, @"\bScale=(-?[\d.]+)");
                if (!action.Success || !key.Success) continue;
                string name = action.Groups[1].Value;
                if (scale.Success && scale.Groups[1].Value.StartsWith("-", StringComparison.Ordinal)) name = Regex.Replace(name, "Forward", "Back", RegexOptions.IgnoreCase).Replace("Right", "Left");
                Bind(bound, name, key.Groups[1].Value);
            }
        }

        internal static void UnrealSave(byte[] data, Dictionary<string, string> bound)
        {
            string action = null, field = null;
            for (int at = 0; at + 6 <= data.Length; )
            {
                int length = BitConverter.ToInt32(data, at);
                if (length < 2 || length > 96 || at + 4 + length > data.Length || data[at + 4 + length - 1] != 0) { at++; continue; }
                bool plain = true;
                for (int i = at + 4; i < at + 4 + length - 1 && plain; i++) plain = data[i] >= 0x20 && data[i] < 0x7f;
                if (!plain) { at++; continue; }
                string token = Encoding.ASCII.GetString(data, at + 4, length - 1);
                at += 4 + length;
                if (Regex.IsMatch(token, @"^(CurrentKey|PrimaryKey|CurrentBinding)$", RegexOptions.IgnoreCase)) { field = "now"; continue; }
                if (Regex.IsMatch(token, @"^(Key|KeyName)$", RegexOptions.IgnoreCase)) { if (field != "old") field = "now"; continue; }
                if (Regex.IsMatch(token, @"^(DefaultKey|DefaultBinding|SecondaryKey)$", RegexOptions.IgnoreCase)) { field = "old"; continue; }
                if (field == "now" && action != null && UnrealKey.IsMatch(token)) { Bind(bound, action, token); field = null; continue; }
                if (UnrealKey.IsMatch(token) || UnrealNoise.IsMatch(token) || !Regex.IsMatch(token, @"^[A-Za-z][A-Za-z0-9_.]{2,63}$")) continue;
                action = token;
                field = null;
            }
        }

        private static string Read(string path)
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(file)) return reader.ReadToEnd();
        }

        // A key's name in plain words, from Minecraft's key.keyboard.left.shift or Cyberpunk's IK_LShift. Null when nothing is bound.
        private static string Key(string value)
        {
            string key = value.Trim();
            if (key.StartsWith("key.mouse.", StringComparison.Ordinal)) return Regex.IsMatch(key, @"\.\d+$") ? "mouse " + key.Substring(10) : key.Substring(10) + " mouse";
            if (key.StartsWith("key.keyboard.", StringComparison.Ordinal)) key = key.Substring(13);
            if (key.StartsWith("IK_", StringComparison.Ordinal)) key = key.Substring(3);
            string plain = Words(key);
            switch (plain)
            {
                case "unknown": case "none": return null;
                case "lshift": return "left shift";
                case "rshift": return "right shift";
                case "lcontrol": return "left ctrl";
                case "rcontrol": return "right ctrl";
                case "lalt": return "left alt";
                case "left control": return "left ctrl";
                case "right control": return "right ctrl";
                case "space bar": return "space";
                case "left mouse button": return "left mouse";
                case "right mouse button": return "right mouse";
                case "middle mouse button": return "middle mouse";
                case "thumb mouse button": return "mouse 4";
                case "thumb mouse button2": return "mouse 5";
                case "ralt": return "right alt";
            }
            return plain;
        }

        private static string Words(string value)
        {
            return Regex.Replace(Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').Replace('.', ' '), @"\s+", " ").Trim().ToLowerInvariant();
        }
    }
}
