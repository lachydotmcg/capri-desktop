using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // A skill is know-how for a game, written by someone who plays it: what to build, when, and why. It's a text file with a
    // short header between two --- lines and the know-how under it, and it goes into her game brief whenever she plays one of its games.
    internal sealed partial class Mods
    {
        internal const string SkillExtension = ".capri-skill";
        private const int MostGames = 20;
        private static readonly Regex SkillLine = new Regex(@"^([a-z-]+)\s*:\s*(.*?)\s*$");
        private volatile Mod[] skills = new Mod[0];

        private Mod LoadSkill(string file, HashSet<string> off)
        {
            ModFile read = ReadSkill(File.ReadAllText(file));
            if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            return new Mod { File = read, Path = file, Skill = true, On = !off.Contains(read.Id) };
        }

        internal static ModFile ReadSkill(string text)
        {
            string[] lines = (text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
            int at = 0;
            while (at < lines.Length && lines[at].Trim().Length == 0) at++;
            if (at == lines.Length || lines[at].Trim() != "---") throw new ModException("A skill starts with a --- line, then capri-skill: 1.");
            ModFile read = new ModFile { WakeNames = new string[0], HeardAs = new string[0], Games = new string[0] };
            bool marked = false, closed = false;
            for (at++; at < lines.Length; at++)
            {
                string line = lines[at].Trim();
                if (line == "---") { closed = true; break; }
                if (line.Length == 0) continue;
                Match field = SkillLine.Match(line);
                if (!field.Success) throw new ModException("\"" + line + "\" isn't a header line. The header is lines like name: BTD6 strategy, and it ends with a second --- line before the know-how.");
                string value = field.Groups[2].Value;
                switch (field.Groups[1].Value)
                {
                    case "capri-skill": marked = int.TryParse(value, out read.Format); break;
                    case "id": read.Id = value; break;
                    case "name": read.Name = value; break;
                    case "version": read.Version = value; break;
                    case "author": read.Author = value; break;
                    case "description": read.Description = value; break;
                    case "games": read.Games = Array.ConvertAll(value.Split(','), game => game.Trim()); break;
                }
            }
            if (!marked) throw new ModException("A skill's header needs a capri-skill: 1 line.");
            if (!closed) throw new ModException("A skill's header ends with a second --- line, and its know-how goes under that.");
            CheckDetails(read);
            read.Games = Array.FindAll(read.Games, game => game.Length > 0);
            if (read.Games.Length == 0) throw new ModException("A skill needs a games: line naming the games it's for.");
            if (read.Games.Length > MostGames) throw new ModException("A skill can be for up to " + MostGames + " games.");
            foreach (string game in read.Games) if (game.Length > 60 || Plain(game).Length == 0) throw new ModException("\"" + game + "\" can't be a game's name.");
            read.Know = string.Join("\n", lines, at + 1, lines.Length - at - 1).Trim();
            if (read.Know.Length == 0) throw new ModException("That skill has nothing under its header.");
            return read;
        }

        private Mod ImportSkill(string file)
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > Largest) throw new ModException("That skill is too big. The limit is " + Largest / 1024 + " KB.");
                text = File.ReadAllText(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            ModFile read = ReadSkill(text);
            string home = Path.Combine(folder, read.Id + SkillExtension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllText(writing, text, new UTF8Encoding(false));
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                HashSet<string> off = Off();
                if (off.Remove(read.Id)) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that skill: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that skill: " + error.Message); }
            Load();
            return All.Find(mod => mod.File.Id == read.Id);
        }

        // "Minecraft 26.3 - Singleplayer" is Minecraft, and "Cyberpunk 2077 (C) 2020 by CD Projekt RED" is Cyberpunk 2077.
        internal static string GameName(string title)
        {
            string name = Regex.Replace(title ?? "", @"[®™©*]", " ");
            name = Regex.Replace(name, @"\s+[-–—|:(\[].*$", "");
            name = Regex.Replace(name, @"\s+v?\d+(\.\d+)+.*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(name, @"\s+", " ").Trim(' ', '.');
        }

        // What a game job is told from the skills that are on for this game. "BloonsTD6" is Bloons TD 6, since only letters and numbers count.
        internal string SkillsFor(string game)
        {
            string playing = Plain(GameName(game));
            if (playing.Length == 0) return "";
            StringBuilder told = new StringBuilder();
            foreach (Mod skill in skills)
                if (Array.Exists(skill.File.Games, name => Plain(name) == playing))
                    told.Append("\nSkill on for " + game + ": " + skill.File.Name + (string.IsNullOrEmpty(skill.File.Author) ? "" : " by " + skill.File.Author) + ". It's know-how from someone who plays it, so follow it over the general advice above:\n" + skill.File.Know);
            return told.ToString();
        }

        private static string Plain(string name) { return Regex.Replace((name ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", ""); }
    }
#if !CROSS

    internal sealed partial class Companion
    {
        private void CheckSkills(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<string, string, string> write = delegate(string id, string text)
            {
                string file = Path.Combine(output, id + Mods.SkillExtension);
                File.WriteAllText(file, text, new UTF8Encoding(false));
                return file;
            };
            string good = write("smoke-skill", "---\ncapri-skill: 1\nid: smoke-skill\nname: Smoke skill\nauthor: Smoke\ngames: Bloons TD 6, BTD6\n---\nPop the red bloons with darts first.\n");
            try
            {
                Mod skill = mods.Import(good, null);
                if (skill == null || !skill.Skill || !skill.On || skill.File.Games.Length != 2 || skill.File.Know != "Pop the red bloons with darts first.") throw new Exception("A skill didn't import as a skill.");
                if (mods.SkillsFor("BloonsTD6").IndexOf("Pop the red bloons", StringComparison.Ordinal) < 0 || mods.SkillsFor("Minecraft") != "" || mods.SkillsFor(null) != "") throw new Exception("A skill went to the wrong game: " + mods.SkillsFor("BloonsTD6"));
                string brief = GameNotes.Brief("BloonsTD6");
                if (brief.IndexOf("Skill on for BloonsTD6: Smoke skill by Smoke.", StringComparison.Ordinal) < 0 || brief.IndexOf("Pop the red bloons", StringComparison.Ordinal) < brief.IndexOf("Tower defense", StringComparison.Ordinal)) throw new Exception("A skill didn't reach her game brief after the general advice: " + brief);
                mods.Set(skill, false, null);
                if (mods.SkillsFor("BTD6") != "" || GameNotes.Brief("BloonsTD6").Contains("Pop the red bloons")) throw new Exception("A skill that was turned off stayed in her brief.");
                mods.Load();
                skill = mods.All.Find(mod => mod.File.Id == "smoke-skill");
                if (skill == null || skill.On) throw new Exception("A skill that was off came back on after a reload.");
                mods.Set(skill, true, null);
                if (mods.SkillsFor("Bloons TD 6") == "") throw new Exception("Turning a skill back on didn't bring it back.");
                checks.Add("PASS skills: a .capri-skill imports, goes into her brief only for its games, and turning it off or reloading is kept");
                string[,] cases =
                {
                    { "skill-bare", "Pop the bloons.", "starts with a --- line" },
                    { "skill-unmarked", "---\nid: skill-unmarked\nname: X\ngames: BTD6\n---\nPop.", "capri-skill: 1" },
                    { "skill-open", "---\ncapri-skill: 1\nid: skill-open\nname: X\ngames: BTD6\nPop.", "second --- line" },
                    { "skill-gameless", "---\ncapri-skill: 1\nid: skill-gameless\nname: X\n---\nPop.", "games: line" },
                    { "skill-empty", "---\ncapri-skill: 1\nid: skill-empty\nname: X\ngames: BTD6\n---\n\n", "nothing under" },
                    { "skill-later", "---\ncapri-skill: 2\nid: skill-later\nname: X\ngames: BTD6\n---\nPop.", "format 2" },
                    { "skill-badid", "---\ncapri-skill: 1\nid: Bad Id\nname: X\ngames: BTD6\n---\nPop.", "lowercase" },
                    { "skill-junk", "---\ncapri-skill: 1\nthis isn't a header\n---\nPop.", "header line" }
                };
                for (int i = 0; i < cases.GetLength(0); i++)
                {
                    try { mods.Import(write(cases[i, 0], cases[i, 1]), null); throw new Exception("A bad skill was let in: " + cases[i, 0]); }
                    catch (ModException refused) { if (refused.Message.IndexOf(cases[i, 2], StringComparison.Ordinal) < 0) throw new Exception("A bad skill gave the wrong reason: " + refused.Message); }
                }
                if (mods.All.FindAll(mod => mod.Skill).Count != 1 || Directory.GetFiles(mods.Folder, "*" + Mods.SkillExtension + "*").Length != 1) throw new Exception("A refused skill left something behind.");
                checks.Add("PASS skills: no header, no format line, an open header, no games, nothing to know, a later format, a bad id and junk lines are refused with the reason");
                ModListing listed = new ModListing { Folder = "smoke-skill", FileName = "smoke-skill" + Mods.SkillExtension };
                if (listed.Kind != "SKILL." || listed.KindIndex != 4 || ModDirectory.Kinds[4] != "Skills" || ModDirectory.Filter(new List<ModListing> { listed }, "", 4).Count != 1 || ModDirectory.Filter(new List<ModListing> { listed }, "", 3).Count != 0) throw new Exception("The mods directory didn't sort a skill as a skill.");
                mods.Remove(skill);
                if (mods.All.Exists(mod => mod.Skill) || mods.SkillsFor("BTD6") != "" || File.Exists(Path.Combine(mods.Folder, "smoke-skill" + Mods.SkillExtension))) throw new Exception("Removing a skill left it behind.");
                checks.Add("PASS skills: the directory lists them under Skills, and removing one deletes it and takes it out of her brief");
                string real = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "..", "..", "mods", "btd6-strategy" + Mods.SkillExtension));
                if (!File.Exists(real)) { checks.Add("LIMIT skills: the BTD6 skill isn't beside this build, so it wasn't imported"); return; }
                Mod btd6 = mods.Import(real, null);
                string played = GameNotes.Brief("BloonsTD6");
                if (btd6 == null || played.IndexOf("0-2-5 (Crossbow Master)", StringComparison.Ordinal) < 0 || played.IndexOf("Round 40 (The MOAB)", StringComparison.Ordinal) < 0 || played.IndexOf("period twice and slash five times", StringComparison.Ordinal) < 0) throw new Exception("The BTD6 skill didn't reach her Bloons brief.");
                mods.Remove(btd6);
                checks.Add("PASS skills: the BTD6 strategy skill imports and her Bloons TD 6 brief carries its plan (" + played.Length + " characters)");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) if (left.Skill) mods.Remove(left);
            }
        }
    }
#endif
}
