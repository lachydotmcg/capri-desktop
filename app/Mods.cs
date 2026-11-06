using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class ModFile
    {
        [DataMember(Name = "format")] public int Format;
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "author")] public string Author;
        [DataMember(Name = "description")] public string Description;
        [DataMember(Name = "wake_names")] public string[] WakeNames;
        [DataMember(Name = "heard_as")] public string[] HeardAs;
        [DataMember(Name = "sounds_like")] public bool SoundsLike;
        [DataMember(Name = "hearing_log")] public bool HearingLog;
        [DataMember(Name = "colors")] public ModColors Colors;
        internal bool Intro;
        internal string[] Games;
        internal string Know;
    }

    [DataContract]
    internal sealed class ModColors
    {
        [DataMember(Name = "hair")] public string Hair;
        [DataMember(Name = "accent")] public string Accent;
        [DataMember(Name = "eyes")] public string Eyes;
        [DataMember(Name = "edges")] public string Edges;
        [DataMember(Name = "body")] public string Body;
        internal string[] AsSlots() { return new[] { Hair, Accent, Eyes, Edges, Body }; }
    }

    internal sealed class Mod
    {
        internal ModFile File;
        internal string Path;
        internal bool On;
        internal bool Code, Changed, Skin, Skill;
        internal string Hash;
    }

    internal sealed class ModException : Exception
    {
        internal ModException(string message) : base(message) { }
    }

    internal sealed partial class Mods
    {
#if CROSS
        internal const string CodeExtension = ".capri-code";
        internal const string WindowsOnly = "Windows only for now";

        private Mod LoadCode(string file, HashSet<string> off) { throw new ModException(WindowsOnly); }
        private Mod ImportCode(string file, Func<ModFile, string, bool> ask) { throw new ModException(WindowsOnly); }
        private bool SetCode(Mod mod, bool on, Func<ModFile, string, bool> ask) { throw new ModException(WindowsOnly); }
        private void RemoveCode(Mod mod) { }
#endif
        internal const string Extension = ".capri-mod";
        internal const int Format = 1;
        private const int Largest = 64 * 1024;
        private const int MostNames = 20;
        private const int MostSpellings = 60;
        private const int LogLines = 300;
        private const string LogFile = "hearing-log.txt";
        private const string OffFile = "off.txt";
        private static readonly Regex GoodId = new Regex(@"^[a-z0-9][a-z0-9-]{0,47}$");
        private static readonly Regex GoodName = new Regex(@"^[\p{L}\p{M}\p{N}]+(?:[ '-][\p{L}\p{M}\p{N}]+)*$");
        private readonly string folder;
        private volatile Regex called;
        private volatile string note = "";
        private volatile SoundsLike sounds;
        private volatile bool logging;

        internal Mods(string data)
        {
            folder = Path.Combine(data, "mods");
            All = new List<Mod>();
            Problems = new List<string>();
            Hands.AlsoHeard = delegate(string words)
            {
                Regex names = called;
                SoundsLike like = sounds;
                return names != null && names.IsMatch(words ?? "") || like != null && like.Hears(words);
            };
            Hands.AlsoCalled = delegate { return note; };
            Load();
        }

        internal string Folder { get { return folder; } }
        internal List<Mod> All { get; private set; }
        internal List<string> Problems { get; private set; }

        internal string[] WakeNames
        {
            get
            {
                List<string> names = new List<string>();
                foreach (Mod mod in All)
                    if (mod.On)
                        foreach (string name in mod.File.WakeNames ?? new string[0])
                            if (!names.Exists(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
                return names.ToArray();
            }
        }

        internal void Load()
        {
            All.Clear();
            Problems.Clear();
            HashSet<string> off;
            string[] files;
            try
            {
                off = Off();
                files = new string[0];
                if (Directory.Exists(folder))
                {
                    List<string> found = new List<string>(Directory.GetFiles(folder, "*" + Extension));
                    found.AddRange(Directory.GetFiles(folder, "*" + CodeExtension));
                    found.AddRange(Directory.GetFiles(folder, "*" + SkinExtension));
                    found.AddRange(Directory.GetFiles(folder, "*" + SkillExtension));
                    found.Sort(StringComparer.OrdinalIgnoreCase);
                    files = found.ToArray();
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                Problems.Add("the mods folder: " + error.Message);
                Brain.Log("mods", -1, Problems[0]);
                off = new HashSet<string>();
                files = new string[0];
            }
            {
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        if (string.Equals(Path.GetExtension(file), CodeExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadCode(file, off)); continue; }
                        if (string.Equals(Path.GetExtension(file), SkinExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadSkin(file, off)); continue; }
                        if (string.Equals(Path.GetExtension(file), SkillExtension, StringComparison.OrdinalIgnoreCase)) { All.Add(LoadSkill(file, off)); continue; }
                        ModFile read = Read(File.ReadAllText(file));
                        if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
                        All.Add(new Mod { File = read, Path = file, On = !off.Contains(read.Id) });
                    }
                    catch (Exception error)
                    {
                        if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                        string problem = Path.GetFileName(file) + ": " + error.Message;
                        Problems.Add(problem);
                        Brain.Log("mods", -1, problem);
                    }
                }
            }
            Apply();
        }

        internal Mod Import(string file, Func<ModFile, string, bool> ask)
        {
            if (string.Equals(Path.GetExtension(file), CodeExtension, StringComparison.OrdinalIgnoreCase)) return ImportCode(file, ask);
            if (string.Equals(Path.GetExtension(file), SkinExtension, StringComparison.OrdinalIgnoreCase)) return ImportSkin(file);
            if (string.Equals(Path.GetExtension(file), SkillExtension, StringComparison.OrdinalIgnoreCase)) return ImportSkill(file);
            string text;
            try
            {
                if (!string.Equals(Path.GetExtension(file), Extension, StringComparison.OrdinalIgnoreCase)) throw new ModException("That isn't a " + Extension + ", " + CodeExtension + ", " + SkinExtension + " or " + SkillExtension + " file.");
                if (new FileInfo(file).Length > Largest) throw new ModException("That mod is too big. A mod is a small settings file.");
                text = File.ReadAllText(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            ModFile read = Read(text);
            string home = Path.Combine(folder, read.Id + Extension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllText(writing, text, new UTF8Encoding(false));
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                HashSet<string> off = Off();
                bool changed = off.Remove(read.Id);
                if (read.Colors != null) { OnlyColours(read.Id, off); changed = true; }
                if (changed) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            Load();
            return All.Find(mod => mod.File.Id == read.Id);
        }

        internal bool Set(Mod mod, bool on, Func<ModFile, string, bool> ask)
        {
            if (mod.Code) return SetCode(mod, on, ask);
            if (mod.Skin) { SetSkin(mod, on); return true; }
            HashSet<string> off = Off();
            if (on)
            {
                off.Remove(mod.File.Id);
                if (mod.File.Colors != null)
                {
                    OnlyColours(mod.File.Id, off);
                    foreach (Mod other in All) if (other != mod && other.File.Colors != null) other.On = false;
                }
            }
            else off.Add(mod.File.Id);
            WriteOff(off);
            mod.On = on;
            Apply();
            return true;
        }

        internal void Remove(Mod mod)
        {
            if (mod.Code) RemoveCode(mod);
            if (mod.Skin) RemoveSkin(mod);
            File.Delete(mod.Path);
            HashSet<string> off = Off();
            if (off.Remove(mod.File.Id)) WriteOff(off);
            Load();
        }

        internal static ModFile Read(string text)
        {
            ModFile read = ReadJson(text);
            CheckDetails(read);
            read.WakeNames = read.WakeNames ?? new string[0];
            read.HeardAs = read.HeardAs ?? new string[0];
            if (read.WakeNames.Length == 0 && read.HeardAs.Length == 0 && !read.SoundsLike && !read.HearingLog && read.Colors == null) throw new ModException("That mod doesn't change anything. It needs wake_names, heard_as, sounds_like, hearing_log or colors.");
            if (read.Colors != null) CheckColours(read.Colors);
            if (read.WakeNames.Length > MostNames) throw new ModException("A mod can add up to " + MostNames + " names.");
            if (read.HeardAs.Length > MostSpellings) throw new ModException("A mod can add up to " + MostSpellings + " spellings.");
            CheckNames(read.WakeNames);
            CheckNames(read.HeardAs);
            return read;
        }

        private static ModFile ReadJson(string text)
        {
            ModFile read;
            try
            {
                using (MemoryStream body = new MemoryStream(Encoding.UTF8.GetBytes((text ?? "").TrimStart('﻿'))))
                    read = (ModFile)new DataContractJsonSerializer(typeof(ModFile)).ReadObject(body);
            }
            catch (SerializationException) { throw new ModException("That file isn't valid mod JSON."); }
            if (read == null) throw new ModException("That file isn't valid mod JSON.");
            return read;
        }

        private static void CheckNames(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string name = (names[i] ?? "").Trim();
                if (name.Length < 2 || name.Length > 32 || !GoodName.IsMatch(name)) throw new ModException("\"" + names[i] + "\" can't be a name. Use 2 to 32 letters or numbers, with single spaces, dashes or apostrophes between them.");
                names[i] = name;
            }
        }

        private static void CheckDetails(ModFile read)
        {
            if (read.Format != Format) throw new ModException("That mod needs format " + read.Format + ", and this Capri reads format " + Format + ".");
            if (read.Id == null || !GoodId.IsMatch(read.Id)) throw new ModException("A mod's id must be lowercase letters, numbers and dashes.");
            if (string.IsNullOrWhiteSpace(read.Name) || read.Name.Length > 40) throw new ModException("A mod needs a name of up to 40 characters.");
            if (read.Version != null && read.Version.Length > 20) throw new ModException("A mod's version can be up to 20 characters.");
            if (read.Author != null && read.Author.Length > 40) throw new ModException("A mod's author can be up to 40 characters.");
            if (read.Description != null && read.Description.Length > 200) throw new ModException("A mod's description can be up to 200 characters.");
        }

        internal static string Pattern(string[] names)
        {
            string[] longestFirst = (string[])names.Clone();
            Array.Sort(longestFirst, (one, other) => other.Length.CompareTo(one.Length));
            return @"(?<![\p{L}\p{M}\p{N}_])(?:" + string.Join("|", Array.ConvertAll(longestFirst, delegate(string name) { return Regex.Escape(name).Replace(@"\ ", @"\s+"); })) + @")(?![\p{L}\p{M}\p{N}_])";
        }

        private void Apply()
        {
            string[] names = WakeNames;
            List<string> exact = new List<string>(names);
            List<string> soundsLike = new List<string>(names);
            soundsLike.Add("Capri");
            bool wantSounds = false, wantLog = false;
            foreach (Mod mod in All)
            {
                if (!mod.On) continue;
                exact.AddRange(mod.File.HeardAs ?? new string[0]);
                wantSounds |= mod.File.SoundsLike;
                wantLog |= mod.File.HearingLog;
            }
            called = exact.Count == 0 ? null : new Regex(Pattern(exact.ToArray()), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            sounds = wantSounds ? new SoundsLike(soundsLike) : null;
            logging = wantLog;
            if (!wantLog) DeleteLog();
            ApplySkin();
            skills = All.FindAll(mod => mod.Skill && mod.On).ToArray();
            note = names.Length == 0 ? "" : "\nThey can also call you " + Listed(names) + ". Those are nicknames for you, so answer to them like your own name.";
        }

        internal string LogPath { get { return Path.Combine(folder, LogFile); } }

        internal void LogHearing(string heard, bool forHer)
        {
            if (!logging) return;
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + (forHer ? " | FOR HER | " : " | ignored | ") + heard + Environment.NewLine);
                if (new FileInfo(LogPath).Length < LogLines * 120) return;
                string[] lines = File.ReadAllLines(LogPath);
                if (lines.Length <= LogLines) return;
                string[] kept = new string[LogLines];
                Array.Copy(lines, lines.Length - LogLines, kept, 0, LogLines);
                File.WriteAllLines(LogPath, kept);
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        private void DeleteLog()
        {
            try { if (File.Exists(LogPath)) File.Delete(LogPath); }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        internal static string Listed(string[] names)
        {
            if (names.Length < 2) return string.Join("", names);
            string[] front = new string[names.Length - 1];
            Array.Copy(names, front, front.Length);
            return string.Join(", ", front) + " or " + names[names.Length - 1];
        }

        private HashSet<string> Off()
        {
            string file = Path.Combine(folder, OffFile);
            return new HashSet<string>(File.Exists(file) ? File.ReadAllLines(file) : new string[0]);
        }

        private void WriteOff(HashSet<string> off)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, OffFile), off);
        }
    }
}
