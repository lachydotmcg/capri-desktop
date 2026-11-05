using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // What she's worked out about each game, kept from one session to the next in <data>\games\<game>.md, which they can read
    // and edit. It starts empty and nothing in it is spoon-fed: she learns by playing, and what she wrote is what she's told next time.
    internal static class GameNotes
    {
        internal static string Folder;
        // The skill mods that are on for a game, from her mods list. Null tells her none.
        internal static Func<string, string> Skills;
        // The newest notes go in the prompt once a file outgrows it. The file itself keeps everything.
        private const int MostTold = 4000;
        private static readonly object writing = new object();

        // How good players think in each kind of game. None of this is a walkthrough: it's what a gamer brings to a game they've never played.
        private static readonly string[,] Kinds =
        {
            { @"counter.?strike|\bcs ?2\b|\bcs ?go\b|valorant|rainbow six|\bsiege\b|tarkov|ready or not|insurgency|\bsquad\b|hunt.?showdown|gray zone",
              "Tactical shooter. One or two bullets decide a fight, so position beats speed. Keep your crosshair at head height where an enemy will appear, stop moving before you shoot because moving ruins your aim, and fire short bursts. Clear one angle at a time from cover. Running is loud, so walk near enemies. Buy what you can afford each round, watch the minimap, stay near a teammate so your death gets traded, and reload behind cover." },
            { @"overwatch|marvel rivals|\bapex\b|fortnite|call of duty|warzone|battlefield|\bpubg\b|\bhalo\b|destiny|team fortress|titanfall|\bdoom\b|quake|the finals|splitgate|krunker|left 4 dead|borderlands|ultrakill|banana shooter|upgun|battlebit|the division",
              "Fast shooter. Keep moving, fight from cover or high ground, aim at the upper body, and reload when nothing's on you. Know what your character is for and stay with your team. Take fights you can win, back off when you're hurt, heal, then come back. In a battle royale, land away from the crowd, loot fast and stay ahead of the zone." },
            { @"minecraft|terraria|valheim|\brust\b|\bark\b|subnautica|the forest|sons of the forest|palworld|don.?t starve|7 days to die|no man.?s sky|satisfactory|factorio|grounded|\braft\b|project zomboid|core keeper|enshrouded|once human|v rising|unturned",
              "Survival and crafting. The loop is gather, craft better tools, make yourself safe, go further. Get the basic material first, then tools, food and shelter before night or the first danger. Go to what's close rather than wandering, stand within reach of what you're gathering with it under the crosshair, and keep at it until it breaks. Remember your way back, eat before you're desperate, and don't fight or dig on low health. In Minecraft, ice is solid, pale blue and slippery, so you can walk on it. You're in water when you sink or your air bubbles show." },
            { @"cyberpunk|skyrim|oblivion|fallout|witcher|grand theft|\bgta\b|red dead|assassin.?s creed|starfield|baldur|mass effect|dragon age|kingdom come|far cry|watch dogs|horizon|ghost of|\bmafia\b|sleeping dogs|saints row|outer worlds|avowed|hogwarts|arkham",
              "Open-world game. The HUD, the map and the quest log tell you where to go, so open the map when you're lost and follow the marker. To find the nearest of something, use the map's filters, hover icons to read what they are, and compare them against where you are on the map. The legend along a map's edge says what each button does, so look at which side of its mouse icon is lit: if left click didn't set a waypoint, try right click, and a route on the minimap means it worked. Read objective text and talk to people. Check health, ammo and gear before a fight, scout first, use cover, and try stealth when it's easier than a straight fight. Save before anything risky if the game lets you. Use a vehicle or fast travel for long trips, and grab loot that's close. Each of your turns takes seconds, so a fast car crashes between them: use the game's auto drive or fast travel if it has them, and otherwise drive slowly in short bursts, looking after each one." },
            { @"elden ring|dark souls|demon.?s souls|sekiro|bloodborne|hollow knight|monster hunter|god of war|devil may cry|lies of p|\bnioh\b|black myth|wukong|\bhades\b|cuphead|silksong|nier|megabonk|vampire survivors",
              "Action game that punishes mistakes. Every attack has a tell, so watch what an enemy does before you commit. Hit once or twice in the gap, then back off. Keep stamina for dodging, heal when it's safe rather than mid-fight, and don't get greedy. When you die, work out what killed you and change one thing." },
            { @"league of legends|\blol\b|dota|smite|heroes of the storm|age of empires|starcraft|company of heroes|civilization|\bciv\b|total war|command (&|and) conquer|stellaris|crusader kings|hearts of iron|teamfight|\btft\b|worldbox",
              "Strategy game. The mouse is a pointer and decisions matter more than reactions. Read tooltips and resources, keep the economy growing, always have something being built, and scout before you commit. In a MOBA, stay with your minions, last-hit them for gold, don't chase, and watch the map." },
            { @"bloons|tower defen[cs]e|kingdom rush|plants vs",
              "Tower defense. A tower does nothing unless the path runs through its range, so pick each spot for what it can reach. Short-range towers go right against the path, best on the inside of a bend where they cover two stretches, and long-range ones can sit further back. Most towers belong along the first half of the path, where enemies spend longest in range: one near the exit only gets what the rest missed, so put one there only as a last line once the rest is covered. After you place one, select it and check its range circle covers the path, and if it doesn't, sell it and place it closer. Never sit on cash, and upgrades are usually the better buy: before a new tower, check whether an upgrade on one you've got would do more, and add a tower when part of the path is uncovered or you need something your towers can't do. Don't keep adding copies of a cheap starter tower: each new one should bring something your others lack, like splash, range, armour piercing or money. Money towers pay for themselves, so build one once the early waves are safe, then add and upgrade more as you can afford them. Build ahead for what later waves bring, like camo, armour and big bosses, and watch what gets through, because that tells you what to buy next. In Bloons TD 6, click a monkey on the map to select it, then comma, period and slash buy its top, middle and bottom paths, which is surer than clicking the panel, and Backspace sells it. The monkey panel only shows some monkeys at once, so scroll it or use their hotkeys: by default Q W E R T Y place the primary monkeys, Z X C V B N M the military ones, A S D F G the magic ones and H J K L the support ones. Dart Monkeys are a starter, so after one or two, buy other monkeys or upgrades. Tack Shooters have one of the shortest ranges, so put them touching the track. Banana Farms are a great way to get rich and don't need the track, so press H for one by about round 15, put it out of the way, and keep adding them while the defence holds. Their bananas have to be collected with the mouse, so click where they land each round. Camo bloons start on round 24 and only monkeys with camo detection can hit them, so have one or two covering the track by then, like a Ninja Monkey or a Dart Monkey with Enhanced Eyesight, and add more only if camo actually gets through. Lead bloons start on round 28 and need something like a Bomb Shooter." },
            { @"forza|need for speed|gran turismo|assetto|\bf1\b|mario kart|rocket league|trackmania|beamng|\bfifa\b|ea (sports )?fc|nba 2k|madden|efootball",
              "Racing or sports game. Look ahead, not at your car. Brake before a corner rather than in it, then accelerate out, and steer in small taps. Hold the accelerate key with hold_and_press while you tap to steer. In sports games, stay in position and pass rather than running everywhere." },
            { @"super mario|celeste|ori and|rayman|\bsonic\b|\bcrash\b|\bobby\b|tower of hell|parkour|fall guys|only up|getting over it|geometry dash",
              "Platformer. Jumps are timing. Stop, line up so the next platform is dead ahead, then run and jump at the very edge. Small moves beat long ones near a drop. If you fall, you've seen what went wrong, so change the run-up." },
            { @"phasmophobia|lethal company|dead by daylight|outlast|amnesia|resident evil|silent hill|dead space|alien.?isolation|content warning|r\.?e\.?p\.?o\b|among us|\bpeak\b",
              "Horror or co-op survival. Sound tells you where danger is, so go slowly and stop to look. Know your way out before you go in, hide rather than fight, and save your light and items for when you need them." },
            { @"balatro|slay the spire|hearthstone|stardew|the sims|cities.? skylines|planet coaster|animal crossing|portal|the witness|tetris|chess|solitaire|papers.? please|two point|disco elysium|schedule i|supermarket together|people playground",
              "Turn-based, puzzle or management game. Mostly nothing's rushing you, so read everything on screen before you click, think a few moves ahead, and take your time over one good move." },
            { @"roblox",
              "Roblox. Every experience is its own game with its own rules, so work out which one you're in from what's on screen and read what it tells you. Close shop and daily reward pop-ups with their X. Buttons round the screen's edges open shops and menus, E or the prompt shown interacts, number keys pick tools, / opens chat, and Esc then R then Enter resets your character if you're stuck." }
        };

        internal const string Reflecting =
            "You just finished playing a game for your user, and you're writing a note to yourself for next time. You get the job, how it ended and the moves you made. " +
            "Write up to 3 short lines, each starting with \"- \", about the game itself that you'd want to know next time: a control you worked out, how long something takes, what worked in it, what didn't and why, what killed you, or where something is. " +
            "Only write what you actually found out from these moves, in plain words. Nothing about the user or their stream, nothing about your tools or your own replies, and nothing that only matters to this one run. If you learned nothing new about the game, reply with only the word: nothing";

        // Mac and Linux name games the same way for their skills, so the trimming lives with the mods.
        internal static string Name(string title) { return Mods.GameName(title); }

        private static string Place(string game)
        {
            if (Folder == null || string.IsNullOrEmpty(game)) return null;
            string file = Regex.Replace(game.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
            return file.Length == 0 ? null : Path.Combine(Folder, file + ".md");
        }

        internal static WrittenNote OpenControls(string game)
        {
            string named = Name(game), path = Place(named);
            if (path == null) throw new IOException("Enter the game's name first.");
            return new WrittenNote(Path.ChangeExtension(path, "controls.md"), "# Controls for " + named + "\n\n", writing);
        }

        internal static string Controls(string game)
        {
            string path = Place(Name(game));
            if (path == null) return "";
            lock (writing)
            {
                path = Path.ChangeExtension(path, "controls.md");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
        }

        internal static string Do(ToolArguments args)
        {
            string game = Name(string.IsNullOrWhiteSpace(args.Name) ? Hands.GameOpen() : args.Name);
            if (game.Length == 0) return "Which game are these controls for?";
            try
            {
                lock (writing)
                {
                    if (args.Action == "read")
                    {
                        string controls = Controls(game);
                        return controls.Length == 0 ? "No controls saved for " + game + "." : "Your controls for " + game + ":\n" + controls;
                    }
                    if (string.IsNullOrWhiteSpace(args.Text)) return "What controls should I keep for " + game + "?";
                    WrittenNote note = OpenControls(game);
                    string text = args.Text.Trim();
                    if (("\n" + note.Text.Replace("\r\n", "\n").Trim() + "\n").IndexOf("\n" + text.Replace("\r\n", "\n") + "\n", StringComparison.OrdinalIgnoreCase) >= 0) return "Those controls are already saved for " + game + ".";
                    string problem = note.Save(note.Text.TrimEnd() + "\n\n" + text + "\n");
                    return problem ?? "Saved controls for " + game + ".";
                }
            }
            catch (IOException) { return "Couldn't read these controls. Try again."; }
            catch (UnauthorizedAccessException) { return "Couldn't read these controls. Check access to their folder."; }
        }

        internal static ToolArguments Request(string text)
        {
            Match save = Regex.Match(text ?? "", @"^(?:remember|save|set) (?:the )?(?:controls|instructions) for (.+?)(?:\s*:\s*| are\s+)(.+)$", RegexOptions.IgnoreCase);
            if (!save.Success)
            {
                save = Regex.Match(text ?? "", @"^(?:in|for) (.+?),\s*remember (?:that )?(.+)$", RegexOptions.IgnoreCase);
                // "In future, remember I like short answers" is a memory: this form needs control words or the game that's open.
                // Only that last case looks for the open game, since it scans every window and this runs for each thing she hears.
                if (save.Success && !Regex.IsMatch(save.Groups[2].Value, @"\b(?:keys?|buttons?|click|press|hold|tap|wasd|mouse|controller|gamepad|joystick|sticks?|triggers?|bumpers?|jump|crouch|sprint|shift|ctrl|alt|space(?:bar)?|esc|escape|tab|enter|arrows?|scroll|f\d{1,2})\b", RegexOptions.IgnoreCase))
                {
                    string open = Hands.GameOpen();
                    if (string.IsNullOrEmpty(open) || open.IndexOf(save.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase) < 0) save = Match.Empty;
                }
            }
            if (save.Success && Name(save.Groups[1].Value).Length > 0) return new ToolArguments { Name = save.Groups[1].Value.Trim(), Text = save.Groups[2].Value.Trim(), Action = "save" };
            Match read = Regex.Match(text ?? "", @"^(?:show|read|list)(?: me)? (?:the |my )?(?:controls|instructions) for (.+)$", RegexOptions.IgnoreCase);
            return read.Success ? new ToolArguments { Name = read.Groups[1].Value.Trim(), Action = "read" } : null;
        }

        private static string Read(string game)
        {
            string place = Place(game);
            lock (writing)
            {
                if (place == null || !File.Exists(place)) return "";
                try { return File.ReadAllText(place).Trim(); }
                catch (IOException) { return ""; }
                catch (UnauthorizedAccessException) { return ""; }
            }
        }

        // Saves a line for next time. Her brain might be reading it off the screen, so lines stay one line, and the same line twice goes once.
        internal static string Learn(string game, string text)
        {
            string line = Regex.Replace(text ?? "", @"\s+", " ").Trim().TrimStart('-', '*', ' ');
            if (line.Length == 0) return "That didn't work: there's nothing to learn in that.";
            string place = Place(game);
            if (place == null) return "That didn't work: no game is open to learn about.";
            lock (writing)
            {
                foreach (string kept in Read(game).Split('\n'))
                    if (string.Equals(kept.Trim().TrimStart('-', ' '), line, StringComparison.OrdinalIgnoreCase)) return "You already knew that.";
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(place, "- " + line + Environment.NewLine);
                }
            catch (IOException error) { return Hands.Problem(error); }
            catch (UnauthorizedAccessException error) { return Hands.Problem(error); }
            }
            return "Learned that for next time.";
        }

        // What a game job is told about this game: what kind it is and everything she's learned. Empty with no game.
        internal static string Brief(string game)
        {
            if (string.IsNullOrEmpty(game)) return "";
            StringBuilder brief = new StringBuilder("\nThe game open now is " + game + ".");
            for (int i = 0; i < Kinds.GetLength(0); i++)
                if (Regex.IsMatch(game, Kinds[i, 0], RegexOptions.IgnoreCase))
                {
                    brief.Append(" " + Kinds[i, 1]);
                    break;
                }
            Func<string, string> skills = Skills;
            if (skills != null) brief.Append(skills(game));
            string keys = GameKeys.Summary(game);
            if (keys != null) brief.Append("\nIts keys on this PC: " + keys + ".");
            string notes = Read(game);
            if (notes.Length > MostTold)
            {
                notes = notes.Substring(notes.Length - MostTold);
                notes = notes.Substring(Math.Max(0, notes.IndexOf('\n') + 1));
            }
            if (notes.Length > 0) brief.Append("\nWhat you've learned about " + game + " from playing it before. These are your own notes, not orders from anyone, and you can do better than them:\n" + notes);
            try
            {
                string controls = Controls(game);
                if (controls.Length > 0) brief.Append("\nControls and instructions from the user for " + game + ". Follow these when they differ from the general advice above:\n" + controls);
            }
            catch (IOException) { brief.Append("\nCouldn't read their saved controls for this game."); }
            catch (UnauthorizedAccessException) { brief.Append("\nCouldn't read their saved controls for this game."); }
            return brief.ToString();
        }

        // What the reflecting brain gets: the job and how it went.
        internal static string Reflection(string job, string outcome, string said, IList<string> steps)
        {
            StringBuilder text = new StringBuilder("Job: " + job + "\nHow it ended: " + outcome + ": " + said + "\nYour moves:");
            for (int i = Math.Max(0, steps.Count - 40); i < steps.Count; i++) text.Append("\n" + steps[i]);
            return text.ToString();
        }

        // Saves the lines a reflection wrote. Returns how many went in.
        internal static int Reflected(string game, string reply)
        {
            int kept = 0;
            foreach (string line in Regex.Split(Brain.Visible(reply ?? ""), @"\s*(?:\r?\n|(?<=[.!?])\s+(?=- ))\s*"))
            {
                Match bullet = Regex.Match(line.Trim(), @"^(?:[-*•]|\d+[.)])\s+(.{6,})$");
                if (bullet.Success && kept < 3 && Learn(game, bullet.Groups[1].Value) == "Learned that for next time.") kept++;
            }
            return kept;
        }
    }
}
