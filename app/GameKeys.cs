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
