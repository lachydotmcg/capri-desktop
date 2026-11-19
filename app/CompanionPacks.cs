using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
#if CROSS
using Companion = CapriDesktop.Pet;
#endif

namespace CapriDesktop
{
    [DataContract]
    internal sealed class CompanionFile
    {
        [DataMember(Name = "format")] public int Format;
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "author")] public string Author;
        [DataMember(Name = "description")] public string Description;
        [DataMember(Name = "wake_names")] public string[] WakeNames;
        [DataMember(Name = "voice")] public CompanionVoice Voice;
        [DataMember(Name = "greetings")] public string[] Greetings;
    }

    [DataContract]
    internal sealed class CompanionVoice
    {
        [DataMember(Name = "free_speaker")] public int? FreeSpeaker;
        [DataMember(Name = "free_speed")] public double? FreeSpeed;
        [DataMember(Name = "british")] public bool British;
        [DataMember(Name = "elevenlabs")] public string ElevenLabs;
    }

    internal sealed class CompanionInfo
    {
        internal string Id, Name, Folder, Personality, ElevenLabs;
        internal string[] Nicknames = new string[0];
        internal string[] Greetings = CompanionPack.PlainGreetings;
        internal int Speaker = -1;
        internal float Speed;
        internal bool British, Art;
        internal int Skipped;
    }

    internal static class CompanionPack
    {
        internal const string Default = "Capri";
        internal static volatile CompanionInfo Active;
        internal static readonly string[] PlainGreetings = { "Good morning.", "Isn't it late?", "Miss me?" };
        internal static Action Changed;

        internal static string Name { get { CompanionInfo pack = Active; return pack == null ? Default : pack.Name; } }

        internal static bool On { get { return Active != null; } }

        internal static bool Art { get { CompanionInfo pack = Active; return pack != null && pack.Art; } }

        internal static string ElevenLabs { get { CompanionInfo pack = Active; return pack == null ? null : pack.ElevenLabs; } }

#if !CROSS
        internal static FreeStyle Style { get { CompanionInfo pack = Active; return pack == null || pack.Speaker < 0 ? null : new FreeStyle(pack.Name, pack.Speaker, pack.Speed, pack.British); } }
#endif

        internal static string Honest(CompanionInfo pack)
        {
            return "Your name is " + pack.Name + ". You are an AI companion on their computer, and if they sincerely ask whether you're a real person, tell them you're an AI.";
        }

        internal static byte[] Hello(string clip)
        {
            CompanionInfo pack = Active;
            if (pack == null || clip == null) return null;
            try
            {
                string file = Path.Combine(pack.Folder, "hello-" + clip + ".wav");
                return File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); return null; }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); return null; }
        }

        internal static void Use(CompanionInfo pack)
        {
            CompanionInfo was = Active;
            Active = pack;
            if (Same(was, pack)) return;
            Action changed = Changed;
            if (changed != null) changed();
        }

        private static bool Same(CompanionInfo one, CompanionInfo other)
        {
            if (one == null || other == null) return one == other;
            return one.Id == other.Id && one.Name == other.Name && one.Folder == other.Folder && one.Personality == other.Personality && one.ElevenLabs == other.ElevenLabs && one.Art == other.Art
                && one.Speaker == other.Speaker && one.Speed == other.Speed && one.British == other.British;
        }
    }

    internal sealed partial class Mods
    {
        internal const string CompanionExtension = ".capri-companion";
        private const string CompanionDetails = "companion.json";
        private const string CompanionPersonality = "personality.md";
        private const long LargestCompanion = 96L * 1024 * 1024;
        private const long LargestClip = 4L * 1024 * 1024;
        private const int LargestPersonalityFile = 32 * 1024;
        private const int MostSpeakers = 200;
        internal static readonly string[] HelloClips = { "hello-morning.wav", "hello-late.wav", "hello-miss.wav" };
        private static readonly Regex GoodVoiceId = new Regex(@"^[A-Za-z0-9]{10,40}$");

        private string CompanionHome(string id) { return Path.Combine(folder, id + "-companion"); }

        internal Mod CompanionOn { get { return All.Find(mod => mod.Pack != null && mod.On); } }

        private static bool IsCompanionFile(string file) { return string.Equals(Path.GetExtension(file), CompanionExtension, StringComparison.OrdinalIgnoreCase); }

        private Mod LoadCompanion(string file, HashSet<string> off)
        {
            string id = Path.GetFileNameWithoutExtension(file), home = CompanionHome(id);
            string details = Path.Combine(home, CompanionDetails), personality = Path.Combine(home, CompanionPersonality);
            if (!File.Exists(details) || !File.Exists(personality)) throw new ModException("its files are missing. Import it again.");
            CompanionFile read = ReadCompanion(File.ReadAllText(details, Encoding.UTF8));
            if (read.Id != id) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            CompanionInfo info = Info(read, home, File.ReadAllText(personality, Encoding.UTF8));
            return new Mod { File = AsModFile(read), Path = file, On = !off.Contains(read.Id), Pack = info };
        }

        private static ModFile AsModFile(CompanionFile read)
        {
            List<string> names = new List<string> { read.Name };
            foreach (string name in read.WakeNames)
                if (!names.Exists(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
            return new ModFile { Format = read.Format, Id = read.Id, Name = read.Name, Version = read.Version, Author = read.Author, Description = read.Description, WakeNames = names.ToArray(), HeardAs = new string[0] };
        }

        private static CompanionInfo Info(CompanionFile read, string home, string personality)
        {
            int skipped;
            string kept = Brain.Kept(Brain.Readable(personality), out skipped);
            if (kept.Length == 0) throw new ModException("its personality.md is empty.");
            CompanionInfo info = new CompanionInfo { Id = read.Id, Name = read.Name, Folder = home, Personality = kept, Skipped = skipped };
            List<string> nicknames = new List<string>();
            foreach (string name in read.WakeNames) if (!string.Equals(name, read.Name, StringComparison.OrdinalIgnoreCase)) nicknames.Add(name);
            info.Nicknames = nicknames.ToArray();
            if (read.Greetings != null) info.Greetings = read.Greetings;
            CompanionVoice voice = read.Voice;
            if (voice != null)
            {
                info.ElevenLabs = voice.ElevenLabs;
                if (voice.FreeSpeaker.HasValue)
                {
                    info.Speaker = voice.FreeSpeaker.Value;
                    info.Speed = (float)(voice.FreeSpeed ?? 1.1);
                    info.British = voice.British;
                }
            }
            try
            {
                foreach (string png in Directory.GetFiles(home, "*.png"))
                    if (Companion.HasArt("Capri.Sprite." + Path.GetFileName(png))) { info.Art = true; break; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return info;
        }

        internal static string CompanionSays(Mod mod)
        {
            if (!mod.On) return CompanionPack.Default + " is back.";
            CompanionInfo pack = mod.Pack;
            return pack.Name + " is here instead of " + CompanionPack.Default + (pack.Skipped > 0 ? ", with " + pack.Skipped + (pack.Skipped == 1 ? " line" : " lines") + " of its personality skipped for going against the safety lines" : "") + ". Turn it off to bring her back.";
        }

        internal static string CompanionLabel(Mod mod)
        {
            return mod.Pack == null ? "" : mod.On ? "COMPANION, ON INSTEAD OF " + CompanionPack.Default.ToUpperInvariant() + ". " : "COMPANION. ";
        }

        internal static CompanionFile ReadCompanion(string text)
        {
            CompanionFile read;
            try
            {
                using (MemoryStream body = new MemoryStream(Encoding.UTF8.GetBytes((text ?? "").TrimStart('﻿'))))
                    read = (CompanionFile)new DataContractJsonSerializer(typeof(CompanionFile)).ReadObject(body);
            }
            catch (SerializationException) { throw new ModException("That companion.json isn't valid JSON."); }
            if (read == null) throw new ModException("That companion.json isn't valid JSON.");
            CheckDetails(new ModFile { Format = read.Format, Id = read.Id, Name = read.Name, Version = read.Version, Author = read.Author, Description = read.Description });
            string[] name = { read.Name };
            CheckNames(name);
            read.Name = name[0];
            read.WakeNames = read.WakeNames ?? new string[0];
            if (read.WakeNames.Length > MostNames) throw new ModException("A companion can add up to " + MostNames + " names.");
            CheckNames(read.WakeNames);
            if (read.Greetings != null)
            {
                if (read.Greetings.Length != 3) throw new ModException("greetings has to be three lines: morning, late at night, and any other time.");
                for (int i = 0; i < 3; i++)
                {
                    string line = Regex.Replace(read.Greetings[i] ?? "", @"[\p{Cc}\p{Cf}\s]+", " ").Trim();
                    if (line.Length == 0 || line.Length > 120) throw new ModException("Each greeting has to be 1 to 120 characters.");
                    read.Greetings[i] = line;
                }
            }
            CompanionVoice voice = read.Voice;
            if (voice != null)
            {
                if (voice.FreeSpeaker.HasValue && (voice.FreeSpeaker.Value < 0 || voice.FreeSpeaker.Value >= MostSpeakers)) throw new ModException("free_speaker has to be a number from 0 to " + (MostSpeakers - 1) + ".");
                if (voice.FreeSpeed.HasValue && (double.IsNaN(voice.FreeSpeed.Value) || voice.FreeSpeed.Value < 0.5 || voice.FreeSpeed.Value > 2)) throw new ModException("free_speed has to be from 0.5 to 2.");
                if (voice.FreeSpeed.HasValue && !voice.FreeSpeaker.HasValue) throw new ModException("free_speed needs a free_speaker.");
                if (voice.ElevenLabs != null && !GoodVoiceId.IsMatch(voice.ElevenLabs)) throw new ModException("\"" + voice.ElevenLabs + "\" isn't an ElevenLabs voice ID.");
            }
            return read;
        }

        private static bool IsWave(string file)
        {
            byte[] head = new byte[12];
            using (FileStream reading = new FileStream(file, FileMode.Open, FileAccess.Read))
                if (reading.Read(head, 0, 12) != 12) return false;
            return Encoding.ASCII.GetString(head, 0, 4) == "RIFF" && Encoding.ASCII.GetString(head, 8, 4) == "WAVE";
        }

        private static CompanionFile UnpackCompanion(string file, string into)
        {
            CompanionFile read = null;
            bool personality = false;
            long total = 0;
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(file))
                {
                    if (zip.Entries.Count > MostEntries + HelloClips.Length + 2) throw new ModException("That companion pack has too many files. It should only hold companion.json, personality.md, her sprite strips and hello clips.");
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName;
                        if (name.Length == 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.Contains("..")) throw new ModException("\"" + name + "\" can't be in a companion pack. Keep every file at the top of the zip.");
                        bool details = name == CompanionDetails, words = name == CompanionPersonality, clip = Array.IndexOf(HelloClips, name) >= 0;
                        bool picture = !details && !words && !clip && name.EndsWith(".png", StringComparison.Ordinal) && Companion.HasArt("Capri.Sprite." + name);
                        if (!details && !words && !clip && !picture) throw new ModException("\"" + name + "\" can't be in a companion pack. It holds companion.json, personality.md, sprite strips named like hers and hello clips.");
                        string target = Path.Combine(into, name);
                        long size = Copy(entry, target, details ? Largest : words ? LargestPersonalityFile : clip ? LargestClip : LargestPicture);
                        total += size;
                        if (total > LargestUnpacked) throw new ModException("That companion pack unpacks too big.");
                        if (details) read = ReadCompanion(File.ReadAllText(target, Encoding.UTF8));
                        else if (words) personality = true;
                        else if (clip) { if (!IsWave(target)) throw new ModException("\"" + name + "\" isn't a WAV file."); }
                        else CheckPicture(target, name);
                    }
                }
            }
            catch (InvalidDataException) { throw new ModException("That file isn't a valid companion pack zip."); }
            if (read == null) throw new ModException("A companion pack needs a " + CompanionDetails + " file.");
            if (!personality) throw new ModException("A companion pack needs a personality.md: who they are and how they talk.");
            Info(read, into, File.ReadAllText(Path.Combine(into, CompanionPersonality), Encoding.UTF8));
            return read;
        }

        private Mod ImportCompanion(string file, bool turnOn)
        {
            string unpacking = Path.Combine(folder, "companion-import-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (new FileInfo(file).Length > LargestCompanion) throw new ModException("That companion pack is too big. The limit is " + LargestCompanion / (1024 * 1024) + " MB.");
                Directory.CreateDirectory(unpacking);
                CompanionFile read = UnpackCompanion(file, unpacking);
                string home = CompanionHome(read.Id), kept = Path.Combine(folder, read.Id + CompanionExtension);
                if (Directory.Exists(home)) Directory.Delete(home, true);
                Directory.Move(unpacking, home);
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase)) File.Copy(file, kept, true);
                if (turnOn)
                {
                    HashSet<string> off = Off();
                    OnlyCompanion(read.Id, off);
                    WriteOff(off);
                }
                Load();
                return All.Find(mod => mod.File.Id == read.Id && mod.Pack != null);
            }
            catch (IOException error) { throw new ModException("Couldn't save that companion pack: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that companion pack: " + error.Message); }
            finally
            {
                try { if (Directory.Exists(unpacking)) Directory.Delete(unpacking, true); }
                catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            }
        }

        private void OnlyCompanion(string keep, HashSet<string> off)
        {
            off.Remove(keep);
            foreach (Mod other in All) if (other.Pack != null && other.File.Id != keep) off.Add(other.File.Id);
        }

        private void SetCompanion(Mod mod, bool on)
        {
            HashSet<string> off = Off();
            if (on)
            {
                OnlyCompanion(mod.File.Id, off);
                foreach (Mod other in All) if (other.Pack != null && other != mod) other.On = false;
            }
            else off.Add(mod.File.Id);
            WriteOff(off);
            mod.On = on;
            Apply();
        }

        private void RemoveCompanion(Mod mod)
        {
            if (mod.On) CompanionPack.Use(null);
            string home = CompanionHome(mod.File.Id);
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }

        private void OneCompanion()
        {
            bool found = false;
            foreach (Mod mod in All)
                if (mod.Pack != null && mod.On)
                {
                    if (found) mod.On = false;
                    found = true;
                }
        }

        private string[] CompanionNames(string[] names)
        {
            Mod on = CompanionOn;
            if (on == null) return names;
            return Array.FindAll(names, name => !string.Equals(name, on.Pack.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyCompanion()
        {
            Mod on = CompanionOn;
            CompanionPack.Use(on == null ? null : on.Pack);
        }

        internal static string CompanionVersionIn(string file)
        {
            using (ZipArchive zip = ZipFile.OpenRead(file))
            {
                ZipArchiveEntry details = zip.GetEntry(CompanionDetails);
                if (details == null || details.Length > Largest) return null;
                using (StreamReader reading = new StreamReader(details.Open(), Encoding.UTF8)) return ReadCompanion(reading.ReadToEnd()).Version;
            }
        }
    }
}
