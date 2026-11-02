using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class SelfKnow
    {
        internal sealed class Topic
        {
            internal readonly string Name;
            internal readonly string PlainName;
            internal readonly string[] Words;
            internal readonly string Facts;
            internal readonly string File;
            internal readonly string Label;
            // Facts made from her tools here, for topics about what she can do.
            private readonly Func<Hands.Kit, string> made;

            internal Topic(string name, string words, string facts, string file, string label)
            {
                Name = name;
                PlainName = Plain(name);
                Words = words.Split('|');
                Facts = facts;
                File = file;
                Label = label;
            }

            internal Topic(string name, string words, Func<Hands.Kit, string> made, string file, string label) : this(name, words, (string)null, file, label) { this.made = made; }

            internal string FactsFor(Hands.Kit kit) { return made == null ? Facts : made(kit); }
        }

        internal static readonly Topic[] Topics =
        {
            new Topic("Mainframe", "your settings|her settings|open your settings|mainframe|settings window",
                "Right-click Capri or her tray icon and choose Open Capri Mainframe. Its main page has BRAIN, VOICE and CAPRI; Settings at the top opens the settings page, and changes are saved as you make them.", "Capri.cs", "Open Capri Mainframe"),
            new Topic("What she can do", "what can you do|what do you do|what can capri do|your features|your capabilities|what are your tools|what tools do you have|your tools|everything you can do|what else can you do",
                delegate(Hands.Kit kit) { return "Talk by hovering over Capri and pressing TYPE or TALK, or use the keys in Mainframe > Settings > KEYS. She chats and speaks, and with her tools she can " + kit.Abilities + ". Ask in plain words; Esc stops a running job." + (kit.Absent.Length == 0 ? "" : " Not here right now: " + Regex.Replace(kit.Absent, @"\btheir\b", "your") + ".") + (kit.Switched.Length == 0 ? "" : " Off right now: " + Regex.Replace(kit.Switched, @"\btheir\b", "your") + "."); }, "Hands.cs", "use_computer"),
            new Topic("Clones and chaos mode", "your clones|clone yourself|chaos mode|make clones|copies of yourself|copy yourself|multiply yourself|duplicate yourself",
                "Ask clone yourself, make 5 clones or activate chaos mode. Her clones wander around, talk to each other, steal the cursor, drag notes in and grab windows; with no number they keep multiplying until they're cleared. Hold Esc, or say get rid of your clones or chaos mode off, to clear them, and windows they moved go back. Mainframe > Settings > CAPRI > Chaos when upset can set them off when you insult her. It's Windows only for now.", "Hands.cs", "clone"),
            new Topic("Shortcuts she can press", "shortcuts can you press|shortcuts you can press|shortcuts do you press|shortcuts do you know|press shortcuts|press a shortcut|press keyboard shortcuts",
                delegate(Hands.Kit kit) { return "Ask in plain words and she presses it for you: she can " + Pressed(kit) + ". Mainframe > Settings > HABITS > Security > Shortcuts sets whether she asks first. Her own hotkeys, like Talk and Quiet, are under Mainframe > Settings > KEYS."; }, "Hands.cs", "shortcut"),
            new Topic("Brain", "your brain|her brain|brain setup|set up her brain|normal brain|higher brain|max intelligence|ollama|local brain|built in brain",
                "In Mainframe > BRAIN, NORMAL uses a local brain: press Set up her brain to download one, or open Ollama and press Refresh. HIGHER uses a free NVIDIA key; MAX INTELLIGENCE accepts Google AI (Gemini), OpenRouter, OpenAI, Claude or DeepSeek keys, and only the selected tier runs. Pick a model from the model menu; a brain key and a voice key do different jobs.", "Mainframe.cs", "NORMAL"),
            new Topic("Free key", "free key|nvidia key|nvidia api|connect a key|connect my key|connect an api key|brain key|api keys",
                "In Mainframe > BRAIN, press Free key, sign in to NVIDIA, then Generate API Key and copy it. Choose HIGHER, paste the key into the BRAIN box and press Connect, then choose a model there. Keys go in the Mainframe's key boxes, not in chat; they are saved encrypted on this PC.", "Mainframe.cs", "Free key"),
            new Topic("Cloud brain keys", "openai key|claude key|deepseek key|chatgpt key|paid key|paid brain|google ai|gemini|openrouter|open router",
                "In Mainframe > BRAIN, choose MAX INTELLIGENCE, paste a Google AI (Gemini), OpenRouter, OpenAI, Claude or DeepSeek API key and press Connect. Google AI starts on an available stable Flash model when there is one; Gemini can see. OpenRouter prefers a free model that takes images, and the model list tells her which models can see and use tools. If no free seeing model is listed she prefers another free model, then an available paid one. A new Claude key starts on Claude Haiku 5.5. Pick an available model in the menu; these are brain services, separate from CLOUD VOICE, and a ChatGPT subscription is not an API key.", "Mainframe.cs", "MAX INTELLIGENCE"),
            new Topic("Voice", "change your voice|change her voice|your voice|her voice|voice choices|voice settings|prebuilt local|custom local|cloud voice|elevenlabs|fish audio",
                "Mainframe > VOICE offers OFF for text only, PREBUILT LOCAL for free voices on this PC, CUSTOM LOCAL for your own model, and CLOUD VOICE for Fish Audio, ElevenLabs or OpenAI. Under PREBUILT LOCAL, press Get free voice if needed and use Free voice: Her own to pick a style; CUSTOM LOCAL's Set up opens its model panel. For CLOUD VOICE pick the service and connect there; ElevenLabs has a key box, Voice ID and model menu, and Test lets you hear the result.", "Mainframe.cs", "PREBUILT LOCAL"),
            new Topic("HD voice", "hd voice|your voice hd|voice clip|clone my voice",
                "Mainframe > VOICE > PREBUILT LOCAL's free voice menu offers Your voice (HD) and a clip picker. HD recommends 6 GB graphics memory and 16 GB RAM; 4 GB may fit with smaller chunks. Less memory warns but still lets you try, and Use CPU for HD trades speed for graphics memory. Capri ships no voice clip of her own. Pick another free voice in that menu to return to a preset style.", "Mainframe.cs", "Your voice (HD)"),
            new Topic("Microphone", "your microphone|your mic|voice chat|talk button|mute mic|mute your mic|cant hear me|cannot hear me|push to talk",
                "Hover over Capri and press TALK, or use the default Ctrl + Alt + V, to start or stop voice chat; choose the input under Mainframe > VOICE > Mic. The mic button or default Ctrl + Alt + N mutes it, and Settings > KEYS can bind Push to talk to a key or mouse button. ElevenLabs uses its voice link when selected and connected unless Listen for Capri is on; the other voice choices use free voice chat, and OFF types her answers.", "Mainframe.cs", "Mic: default"),
            new Topic("Listen for Capri", "listen for capri|listen for your name|only reply to your name|say your name",
                "Mainframe > Settings > VOICE > Listen for Capri is off by default. Turn it on and her mic stays open whenever she's running, with no key to press, and only whole sentences containing her name reach her, including mid-job. Ctrl + Alt + V closes her ears, and again opens them. Hidden or Quiet they wait, and come back with her. Stop words during a job or her speech and push to talk work without her name. Hey Capri is separate: it opens chat, which stays open until you close it. With ElevenLabs selected she uses her own ears and brain, then speaks replies in the chosen ElevenLabs voice; listening uses no ElevenLabs credits, and replies use text-to-speech credits instead of voice-link minutes.", "Mainframe.cs", "Listen for Capri"),
            new Topic("Listening to PC sound", "listen tool|pc sound|listen to videos|hear videos|hear game dialogue|translate speech",
                "Ask her to listen to this, say what someone is saying in a video, call, stream or game, or translate their words. The listen tool waits for her own voice to finish, then captures the default output device from now for 15 seconds unless you give another duration. She stays quiet while capturing, keeps the audio only in memory and releases it after transcription. Her existing Parakeet ears or Windows offline recognizer turn it into words, which can have mistakes. Stop ends listening early and uses what she has. It does not record in the background, replay earlier sound or identify songs by ear.", "Hands.cs", "listen"),
            new Topic("Follow-up messages", "follow up messages|follow-up messages|second thought|answer then yourself",
                "Mainframe > Settings > BRAIN > Follow-up messages is on by default. Sometimes she adds one second thought in its own bubble and voice, from the same brain reply. It waits until her first voice and reading time finish, then another two seconds. Typing, speech or a new message drops it. It does not run in jobs, voice chat links or game comments; free voice chat waits for you to finish.", "Mainframe.cs", "Follow-up messages"),
            new Topic("Watch-along memory", "watch along memory|watch-along memory|watch this with me|watch a video with you|watch together|react to this",
                "Say watch this with me, watch this video with me, let's watch this or react to this. She follows any window using PC sound and pictures, with free Parakeet or Windows offline speech recognition and a seeing brain. She explains missing eyes or ears and uses what works. Pictures are taken every five seconds; about every twenty seconds a small fast brain picks a short reaction or stays quiet, waiting for a gap in the sound. Mainframe > Settings > WATCH > Watch-along memory holds 8, 16, 24, 48 or 96 lines, default 24, only in memory. At the limit, the oldest half becomes one short note in her transcript if Remember what she sees is on, otherwise it is dropped. Say stop watching with me or stop; ninety seconds without sound or screen changes also ends it. Raw audio and the rolling transcript are not saved.", "Mainframe.cs", "Watch-along memory"),
            new Topic("Head pats", "head pats|head pat|headpats|pat you|pet you|petting you",
                "Mainframe > Settings > CAPRI > Head pats is on by default. Rub the cursor back and forth over her and she grins and says a line, at most once every 20 seconds, and a pat wakes her from a nap. Turned off, rubbing the cursor over her does nothing.", "Mainframe.cs", "Head pats"),
            new Topic("Chaos when upset", "chaos when upset|upset chaos|chaos when youre upset|why did your clones|clones when im rude",
                "Mainframe > Settings > CAPRI > Chaos when upset is on by default. Insulting her can set her clones off: 1 chance in 3 the first time, 2 in 3 the next, and the third insult always does it. They keep multiplying until you hold Esc. It never happens during a job, a game, a stream or voice chat. Turned off, an insult only gets a reply. It's Windows only for now.", "Mainframe.cs", "Chaos when upset"),
            new Topic("Advanced Settings", "advanced settings|simple settings|more settings|all your settings|all the settings|cant find that setting|cant find the setting|where did that setting go|where did my settings go|where did the settings go|settings are missing",
                "Mainframe > Settings opens on the main settings. The Advanced Settings button at the top, next to Back, shows every setting, and the same button then says Simple Settings to go back. She remembers which you left it on.", "Mainframe.cs", "Advanced Settings"),
            new Topic("Extras", "extras window|extras screen|your extras|first launch picker",
                "On a new install's first launch she opens EXTRAS over the Mainframe with four switches, all off: Watch my screen, Comment on my games, Clip my games and Listen for Capri. Each costs something or uses the mic, so they're your pick, and Done closes it. All four are in Settings as well: WATCH, GAMES, GAMES and VOICE. Comment on my games is GAMES > Comments set to Chatty.", "Mainframe.cs", "EXTRAS >"),
            new Topic("Language", "your language|her language|change language|speak another language",
                "Mainframe > Settings > YOU > Language chooses the language she replies in. Changing it starts a fresh recent chat.", "Mainframe.cs", "Language"),
            new Topic("What should she call you?", "your nickname|my nickname|change my name|what should she call you|what you call me",
                "In Mainframe > Settings > YOU, enter a nickname under What should she call you? and press Save. Clear it or enter choom to go back to choom; Get to know me asks about your name, pronouns and interests and keeps the answers in me.md.", "Mainframe.cs", "What should she call you?"),
            new Topic("Get to know me", "get to know me|your introduction|my pronouns",
                "Mainframe > Settings > YOU > Get to know me starts her questions about what to call you, your pronouns and what you're into. The answers go into me.md; say skip to pass a question or stop to end it.", "Mainframe.cs", "Get to know me"),
            new Topic("Volume", "your volume|her volume|voice volume|you are too loud|you are too quiet",
                "Mainframe > Settings > VOICE > Volume changes how loud her voice plays, from Mute to 150%. It changes Capri's voice, not the volume of other apps.", "Mainframe.cs", "Volume"),
            new Topic("Pitch", "voice pitch|your pitch|her pitch",
                "Mainframe > Settings > VOICE > Pitch makes her voice lower or higher, from -6 to +6 semitones, and starts at +2. It applies to every voice, including ElevenLabs, and she says a sample when you release the slider.", "Mainframe.cs", "Pitch"),
            new Topic("Speed", "voice speed|your speech speed|talk faster|talk slower|speak faster|speak slower",
                "Mainframe > Settings > VOICE > Speed changes how quickly she speaks, from 0.7x to 1.5x. It affects every voice and plays a sample when you release the slider.", "Mainframe.cs", "Speed"),
            new Topic("Robot", "robot voice|robotic voice|sound robotic",
                "Mainframe > Settings > VOICE > Robot adds a robotic effect to her voice. HD and custom or cloud model voices keep their own amounts; the other voices share an amount.", "Mainframe.cs", "Robot"),
            new Topic("Glitch", "voice glitch|glitchy voice|glitch effect",
                "Mainframe > Settings > VOICE > Glitch changes the glitch effect in her speech. HD and custom or cloud model voices keep their own amounts; this slider changes sound, not her startup animation.", "Mainframe.cs", "Glitch"),
            new Topic("Voice layers", "voice layers|layers setting|layers switch|layer pitch|layer delay",
                "Mainframe > Settings > VOICE > Layers opens VOICE LAYERS, where Voice layers turns two quieter copies of her voice on or off. Layers are off by default. Each copy has its own Pitch and Delay, and the effect applies to every voice, including ElevenLabs. Hear her plays a sample and Defaults restores the layer settings.", "Mainframe.cs", "Voice layers"),
            new Topic("Sounds", "your sound effects|her sound effects|sound effects volume",
                "Mainframe > Settings > VOICE > Sounds controls her sound effects, with Off and volume steps. Voice Volume has its own slider.", "Mainframe.cs", "Sounds"),
            new Topic("Typing sound", "typing sound|typing noise|typing clicks",
                "Mainframe > Settings > VOICE > Typing sound turns the soft typing noise during her text replies on or off. Sounds in the same section controls the sound effects level.", "Mainframe.cs", "Typing sound"),
            new Topic("Free voice in games", "free voice in games",
                "Mainframe > Settings > VOICE > Free voice in games appears while HD voice is selected and is off by default. When on, a full-screen game or video makes the free voice take over from HD so the graphics card is freed for it.", "Mainframe.cs", "Free voice in games"),
            new Topic("Mini player", "mini player|music player|your player",
                "Mainframe > Settings > MOVES > Mini player shows the song Windows reports, with playback controls and a place for her to sit. It hides during full-screen games and videos unless Mini player over full screen is on under HABITS; Spotify also gets a heart for liking the song.", "Mainframe.cs", "Mini player"),
            new Topic("Wander", "your wandering|her wandering|wander around|wander off|stop wandering|dont wander|why dont you move|why arent you moving",
                "Mainframe > Settings > MOVES > Wander sets Off, Rarely, Sometimes, Often or Restless, and a new install starts on Rarely; right-click > Move > Wander around also toggles it. She wanders while idle and you're active at the PC, and stays put in a spot chosen with Sit here until you choose Stand up.", "Mainframe.cs", "Wander"),
            new Topic("Sleep", "your sleep|her sleep|when do you sleep|when you fall asleep|go to sleep|wake you up",
                "Mainframe > Settings > MOVES > Sleep chooses how long the PC is quiet before she dozes off, or Never. She sleeps until you're back. She also lies down awake for 2 to 4 minutes now and then, even on Never. Jobs, calls, voice chat, Gaming Mode and typing to her keep her awake. Moving the mouse wakes her own nap; a click, a pet, talking to her or asking for a job or call wakes an asked-for nap too. Asked to sleep during a job, she finishes it first; say stop to stop the job, or wake up to cancel the nap.", "Mainframe.cs", "Sleep"),
            new Topic("Pop-ins", "pop ins|popins|how often you visit|how often you pop in",
                "Mainframe > Settings > MOVES > Pop-ins sets Check in on me's pace: Rarely is 20 to 40 minutes, Sometimes 8 to 20, and Often 3 to 8. Check in on me under CAPRI turns the check-ins on or off.", "Mainframe.cs", "Pop-ins"),
            new Topic("Dance to music", "dance to music|your dancing|why dont you dance|stop dancing",
                "Mainframe > Settings > MOVES > Dance to music turns dancing with detected music on or off; the same switch is under right-click > Move. Windows needs to report the playing media as music; Move > Dance asks for a dance directly.", "Mainframe.cs", "Dance to music"),
            new Topic("Glitch in at startup", "glitch in at startup|startup animation|your entrance",
                "Mainframe > Settings > MOVES > Glitch in at startup switches her arrival effect on or off. It changes how she appears when Capri starts, not whether she starts with Windows.", "Mainframe.cs", "Glitch in at startup"),
            new Topic("Hotkeys", "hotkeys|hot keys|your shortcuts|keyboard shortcuts|change your keys|keybinds|ctrl alt",
                "Mainframe > Settings > KEYS lets you click a binding, press new keys and click Confirm; Cancel keeps the old binding. The defaults are Ctrl + Alt + V for Talk, N for Mute mic, T for Type, H for Hide/show, P for Quiet and G for Gaming Mode. Push to talk starts unbound and can use a key or mouse button held while talking.", "Mainframe.cs", "KEYS >"),
            new Topic("Quiet", "quiet mode|quiet key|make you quiet|keep you quiet|stop interrupting|stop talking on your own",
                "The default Ctrl + Alt + P toggles Quiet, and Mainframe > Settings > KEYS lets you change that key. It stops unsolicited glances, check-ins, cheers and jokes and pauses Hey Capri, while she still answers when you talk to her. Press it again to resume without changing Watch my screen or Check in on me.", "Mainframe.cs", "Quiet: Ctrl + Alt + P"),
            new Topic("Size", "your size|her size|make you bigger|make you smaller|make her bigger|make her smaller|resize you|resize capri",
                "Mainframe > Settings > CAPRI > Size changes Capri herself from Tiny to Giant. Right-click > Size also has size choices; Mainframe size is a separate setting for her window.", "Mainframe.cs", "Size"),
            new Topic("Mainframe size", "mainframe size|settings too small|mainframe too small|mainframe bigger|virtual super resolution|dsr",
                "Mainframe > Settings > CAPRI > Mainframe size scales the window, with Auto or 100% to 200%. Auto compensates when Virtual Super Resolution or DSR makes the desktop larger than the display showing it; Size changes the character separately.", "Mainframe.cs", "Mainframe size"),
            new Topic("Check in on me", "check in on me|pop in on me|your check ins|you keep popping up|keep asking for attention|why are you asking for attention",
                "Mainframe > Settings > CAPRI > Check in on me is the current name for Pop in on me. It enables her visits to say hi, attention check-ins, game and music cheers and the busy-PC joke; MOVES > Pop-ins changes the visit pace. With Wander off or a chosen seat she checks in from her spot instead of moving over.", "Mainframe.cs", "Check in on me"),
            new Topic("Always on top", "always on top|keep you on top|behind my windows|behind other windows",
                "Mainframe > Settings > CAPRI > Always on top keeps her above ordinary windows. An exclusive full-screen app can still cover her; borderless or windowed mode lets desktop overlays show.", "Mainframe.cs", "Always on top"),
            new Topic("Watch my screen", "watch my screen|watching my screen|your screen watching|see my screen|read my screen|screen watching|cant you see|cannot you see",
                "Mainframe > Settings > WATCH > Watch my screen enables occasional comments, with Every setting the time between glances. Seeing requires a vision-capable brain: text-only models cannot take images; under BRAIN a seeing NVIDIA choice is nvidia/nemotron-3.5-super-vl-120b-a12b. She skips unchanged or blank captures, Quiet pauses glances, and during voice chat she comments only while the mic is muted; asking her to look is a separate action.", "Mainframe.cs", "Watch my screen"),
            new Topic("Remember what she sees", "remember what she sees|remember what you see|keep her caught up|keep you caught up|caught up setting|silent glances",
                "Mainframe > Settings > WATCH > Remember what she sees works with Watch my screen. When she glances at your screen and says nothing, she still remembers what she saw for three hours, so you can ask about it. It has nothing to do with updating the app, and it uses a little more of your brain API.", "Mainframe.cs", "Remember what she sees"),
            new Topic("Every", "glance interval|time between glances|how often you look|how often you watch",
                "Mainframe > Settings > WATCH > Every sets the gap between screen glances, from 10 seconds to 10 minutes. Watch my screen must be on; she can still skip a glance when the screen has not changed or she is busy.", "Mainframe.cs", "Every"),
            new Topic("Stay on my model", "stay on my model|switching models|changing brains by yourself|model fallback",
                "Mainframe > Settings > BRAIN > Stay on my model keeps the selected NVIDIA model when it gets stuck. With it on she reports the problem instead of trying another model.", "Mainframe.cs", "Stay on my model"),
            new Topic("Replies", "your replies|her replies|reply length|longer replies|shorter replies|long answers|short answers",
                "Mainframe > Settings > BRAIN > Replies selects Short, Medium or Long answers. This changes the amount she says, while Bubble changes how long her text stays visible.", "Mainframe.cs", "Replies"),
            new Topic("Max reply", "max reply|maximum reply|reply tokens|token limit|reply limit",
                "Mainframe > Settings > BRAIN > Max reply offers Auto, 256, 2K or 8K tokens for her longest reply. Auto keeps the usual reply budget and follows Replies above; the numbered choices allow that many tokens, with 2K meaning 2048 and 8K meaning 8192. Jobs keep extra room when they need it; choosing more room does not make every answer long.", "Mainframe.cs", "Max reply"),
            new Topic("Memory", "your memory setting|her memory setting|recent chat|chat context|forget our conversation",
                "Mainframe > Settings > BRAIN > Memory chooses Short, Medium or Long for 8, 16 or 32 recent messages. That recent chat can survive restarts for up to a week; Remember our chats and me.md cover longer-term memories separately.", "Mainframe.cs", "Memory"),
            new Topic("Memory size", "memory size|context size|context window|context tokens",
                "Mainframe > Settings > BRAIN > Memory size sets the context window in tokens for Ollama and her built-in brain: Auto, 4K, 8K, 16K, 32K or 64K. Auto uses 4096 tokens with Short Memory and 8192 with Medium or Long Memory. Memory still chooses how many recent messages she keeps; this does not change a cloud brain or another local server's context size.", "Mainframe.cs", "Memory size"),
            new Topic("Bubble", "bubble setting|your bubble|your bubbles|her bubble|bubble duration|how long replies stay|text disappears",
                "Mainframe > Settings > BRAIN > Bubble selects Quick, Normal or Long for how long her reply bubble stays up. The time also follows the reply's length; this does not change how much she says.", "Mainframe.cs", "Bubble"),
            new Topic("Custom model name", "custom model name|custom brain|another ollama model",
                "Mainframe > Settings > BRAIN > Custom model name accepts an Ollama model you've already pulled, a model ID on your selected local server or one available to your brain key. Enter it and press Use; it does not install an arbitrary model for you.", "Mainframe.cs", "Custom model name"),
            new Topic("Local servers", "local server|local servers|local server address|lm studio|use jan|jan server|jan brain|connect jan|koboldcpp|llama cpp",
                "LM Studio, Jan, KoboldCpp and llama.cpp can supply her NORMAL brain from this PC. Start the server with a model loaded, then choose NORMAL in Mainframe > BRAIN, press Refresh and pick its model. For a different address, use Mainframe > Settings > BRAIN > Local server address, enter an HTTP loopback address such as http://127.0.0.1:1234 and press Save. Refresh checks ports 1234, 1337, 5001 and 8080 plus your saved address.", "Mainframe.cs", "Local server address"),
            new Topic("Start with Windows", "start with windows|run at startup|launch at startup|start automatically",
                "Mainframe > Settings > HABITS > Start with Windows controls automatic launch for your Windows user. The installed copy enables it once on first run, and turning it off stays off after updates; Task Manager's Startup apps setting is reflected here too.", "Mainframe.cs", "Start with Windows"),
            new Topic("Show on Discord", "show on discord|playing capri|discord status|discord activity",
                "Mainframe > Settings > HABITS > Show on Discord controls the Playing Capri activity while Discord is running on this PC. It shows a short activity and elapsed time, not song titles or the contents of jobs.", "Mainframe.cs", "Show on Discord"),
            new Topic("Use her cursor", "use her cursor|use your cursor|your pink pointer|your cursor setting",
                "Mainframe > Settings > HABITS > Use her cursor lets YouTube searches and Spotify's volume slider use her visible pointer when a brain and the mouse permission are available. Spotify music uses play_music directly with either setting. Off uses the quick tools instead; it does not turn off explicit mouse and keyboard jobs.", "Mainframe.cs", "Use her cursor"),
            new Topic("Obsidian vault", "memory vault|obsidian|my vault|in obsidian|to obsidian|obsidian vault",
                "Mainframe > Settings > HABITS > Obsidian vault lets you pick one of your Obsidian vaults, or Browse to any .md note. Her notes about you, me.md, then live in a Capri Memories note there, so you can read and edit them in Obsidian. Her transcripts and memories.md stay in her own folder. Pick Her own notes to put her back on me.md, and what she learned in the vault comes back with her.", "Mainframe.cs", "Obsidian vault"),
            new Topic("Remember our chats", "remember our chats|memories md|chat transcripts|open her memories|your memories",
                "Mainframe > Settings > HABITS > Remember our chats keeps daily transcripts and folds older chats into memories.md on this PC, with Open her memories opening their folder. Turning it off stops writing, folding, searching and reading these memories without deleting them. With a cloud brain, relevant recalled text and chats being folded go to that selected brain as context.", "Mainframe.cs", "Remember our chats"),
            new Topic("Perch", "perch fps|perch frame rate|perch setting|your animation speed",
                "Mainframe > Settings > HABITS > Perch selects the frames per second while she sits on a window. The sway takes the same time at each setting; above 30 fps she draws intermediate frames for smoother movement.", "Mainframe.cs", "Perch"),
            new Topic("Keep PC awake for jobs", "keep pc awake|keep my pc awake|keep the pc awake|keep screen on|keep my screen on|keep the screen on|keep screen awake|keep awake for jobs|sleep during jobs|laptop lid",
                "Mainframe > Settings > HABITS > Keep PC awake for jobs is on by default. It keeps both the PC and screen awake while a job runs, waits for your answer or has an active watch_for. Her eyes need the screen on to see. She releases the hold when no jobs or watches remain, when you turn the switch off, or when she quits. It does not change Windows power settings or simulate input. A closed laptop lid still sleeps. Explore while I'm away is separate: it never wakes a sleeping screen and still stops after half an hour.", "Mainframe.cs", "Keep PC awake for jobs"),
            new Topic("Water", "water check in|water check ins|water setting|water reminder setting",
                "Mainframe > Settings > HABITS > Water turns her water check-ins on or off. The time menu beside it chooses the gap, with 60 minutes as the default. She waits until she is free to say the line; Edit check-in lines changes its wording.", "Care.cs", "Water"),
            new Topic("Stretch", "stretch check in|stretch check ins|stretch setting|stretch reminder setting",
                "Mainframe > Settings > HABITS > Stretch turns her stretch check-ins on or off, with a time menu beside it and a default gap of 90 minutes. Edit check-in lines changes what she says.", "Care.cs", "Stretch"),
            new Topic("Look away", "look away setting|eye break setting|eye check ins|rest your eyes setting",
                "Mainframe > Settings > HABITS > Look away enables check-ins to look away from the screen for 20 seconds. The time menu beside it sets the gap, which starts at 25 minutes; Edit check-in lines changes the words.", "Care.cs", "Look away"),
            new Topic("Sleep nudge", "sleep nudge|bedtime nudge|bedtime setting",
                "Mainframe > Settings > HABITS > Sleep nudge gives one late-night check-in per night. The time menu beside it chooses the hour, with 1 am as the default; this is a nudge for you, separate from MOVES > Sleep for Capri's own nap.", "Care.cs", "Sleep nudge"),
            new Topic("Meals", "meals setting|meal check in|meal check ins|meal reminder setting",
                "Mainframe > Settings > HABITS > Meals enables food check-ins if you've been at the PC since before noon. Mealtimes start at noon and repeat at the interval beside the switch, six hours by default; Edit check-in lines changes what she says.", "Care.cs", "Meals"),
            new Topic("Game break", "game break setting|game break check in|game break check ins",
                "Mainframe > Settings > HABITS > Game break enables a check-in after time in a detected game, two hours by default. The time menu beside it changes that gap; changing games restarts the timer. She waits until she is free and out of full-screen play before saying it.", "Care.cs", "Game break"),
            new Topic("Bored", "bored setting|bored check in|bored check ins|boredom check in|stop saying youre bored",
                "Mainframe > Settings > HABITS > Bored controls her bored attention check-ins. The time menu beside it starts at 60 minutes without attention; noticing her resets the wait, and ignoring her makes later waits longer. Edit check-in lines includes Bored, Bored noticed and Bored ignored for those replies.", "Care.cs", "Bored"),
            new Topic("Edit check-in lines", "edit check in lines|check ins md|change check in lines|check in wording",
                "Mainframe > Settings > HABITS > Edit check-in lines opens check-ins.md in Notepad. Edit the bullet lines under each heading and save; removing all bullets under a heading silences that check-in. The HABITS check-ins have their own switches and times, separate from CAPRI > Check in on me. Quiet or being busy postpones them, and more than ten minutes away resets their interval timers.", "Mainframe.cs", "Edit check-in lines"),
            new Topic("Comments", "your game comments|game commentary setting|comments setting",
                "Mainframe > Settings > GAMES > Comments selects Off, only when something happens, or Chatty. Game commentary uses screen observations, sound levels and supported logs written by the game; it is separate from the timer under WATCH.", "Mainframe.cs", "Comments"),
            new Topic("Hide in full screen", "hide in full screen|hide in fullscreen|disappear in fullscreen|hide over videos",
                "Mainframe > Settings > HABITS > Hide in full screen is off by default. When on, she hides while the foreground window fills its monitor and returns afterwards. A manual hide stays hidden. Speech and text follow her usual hidden behaviour. Gaming Mode takes priority while it is on.", "Mainframe.cs", "Hide in full screen"),
            new Topic("Mini player over full screen", "mini player over full screen|mini player over fullscreen|player over games|music over fullscreen",
                "Mainframe > Settings > HABITS > Mini player over full screen is off by default. When on, her enabled mini player can stay over borderless full-screen windows. Windows cannot show it over exclusive full screen. A manual hide and Gaming Mode still put the player away; Hide in full screen can hide her body while the player stays up.", "Mainframe.cs", "Mini player over full screen"),
            new Topic("Explore while I'm away", "explore while im away|explore while i'm away|afk exploring|scroll while away|explore my screen",
                "Mainframe > Settings > HABITS > Explore while I'm away is off by default. After Explore after, five minutes by default, she moves only her own pointer and scrolls a browser or reader page under your mouse. She never clicks, types, opens or closes anything while exploring. Games, full-screen windows, password fields and blocked windows are excluded. Your mouse or keyboard input stops her; her own scrolling does not reset your away time. She stops after half an hour so your screen can turn off and your PC can sleep, and she never wakes a screen that has already gone off. With a brain that can see she sometimes makes one short comment using WATCH's pace.", "Mainframe.cs", "Explore while I'm away"),
            new Topic("Explore after", "explore after|away time|afk time|exploring delay",
                "Mainframe > Settings > HABITS > Explore after sets the idle time before Explore while I'm away starts: one, two, five, ten, fifteen or thirty minutes. Five minutes is the default. Her own scrolling does not count as you coming back.", "Mainframe.cs", "Explore after"),
            new Topic("Gaming Mode", "gaming mode|game mode|gamer mode|gaming mod|game mod|gaming mood|your gaming hud|your game overlay|turn on the hud|activate the hud|enable the hud|start the hud|move the hud|move your hud|reset the hud|hud position|gaming mode key",
                "Say \"Hey Capri, activate gaming mode\" and she enters Gaming Mode. Mainframe > Settings > GAMES also has the Gaming Mode switch; the default key is Ctrl + Alt + G. It's on by default: a game in front brings it up with nothing said, and turning it off keeps it off until you turn it back on. She stays on your desktop as usual, you can still drag her, and she reacts when something happens in the game until it closes. Her HUD is switched off for now, so there's nothing to move. It works best on MAX INTELLIGENCE and is weaker on other brains. Settings > KEYS changes that key; press it again to turn the mode off, or ask turn off gaming mode. Gaming Mode does not by itself ask her to play. When she plays a game herself as a job, she goes into Gaming Mode by herself once the game is in front; it stays up until the game closes, and turning it off keeps it off for that job.", "Mainframe.cs", "Gaming Mode"),
            new Topic("Clip my games", "clip my games|clip my game|clip a game|game clips|saved clips|clips saved|your clips|clip that|record my games",
                "Mainframe > Settings > GAMES > Clip my games is off by default. When on, it keeps up to the last two minutes of a detected game's video and PC playback sound in memory while the game is in front. Say clip that to save the last 30 seconds, or clip the last two minutes for a longer clip of what is buffered. Clips are MP4 files in Videos\\Capri Clips on this PC; she tells you the saved path.", "Mainframe.cs", "Clip my games"),
            new Topic("Save big moments", "save big moments|automatic clips|auto clips|automatic clipping|clip moments|big moments",
                "Mainframe > Settings > GAMES > Save big moments is directly under Clip my games and off by default. Turning it on also turns Clip my games on; turning Clip my games off turns both off. With both on, her game commentary decides whether a moment is worth keeping and saves the last 30 seconds of buffered video and PC sound. Comments must be running: Gaming Mode runs them, but with Comments off outside Gaming Mode she judges and saves nothing automatically. She reacts as usual, then saves it with no spoken notice; failures stay in her log. Moments under 30 seconds apart do not extend a clip or save another. Clips go to Videos\\Capri Clips like any other clip and take up space the same way; there is no limit on how many she saves and she does not delete old clips.", "Mainframe.cs", "Save big moments"),
            new Topic("Mute her", "mute her|mute capri|mute button|mute you|stop talking on your own",
                "Mainframe > Settings > GAMES > Mute her stops her talking on her own, and she still answers when you talk to her. It's the same as her Quiet key, Ctrl + Alt + P.", "Mainframe.cs", "Mute her"),
            new Topic("Fast hands in games", "fast hands|haiku in games|games on haiku|play on haiku|why haiku|hands brain|cheaper games|faster in games",
                "Mainframe > Settings > GAMES > Fast hands in games is on by default. On a Claude key, a job that plays a game runs on Claude Haiku 5.5, because a game doesn't wait: turns take about 3 seconds instead of 10 or more and cost a fraction. When she's stuck she asks the brain you picked what to do, or Claude Sonnet 5.5 if you picked Haiku, which costs a few cents a time. Chat stays on the brain you picked. Turn it off to play on your picked brain. Other brains aren't changed by it.", "Mainframe.cs", "Fast hands in games"),
            new Topic("Mini games", "mini games|mini game|minigames|tic tac toe|tictactoe|connect four|connect 4|rock paper scissors|desktop boards",
                "Ask play tic-tac-toe, play connect four, play rock paper scissors or play chess to open her own desktop board beside her, even without a brain connected. Click a move or tell her a square, column or hand. On the board, tic-tac-toe uses 1-9, connect four 1-7, and rock paper scissors R, P or S; arrows and Enter also work. N starts another round and Esc closes those boards. Chess uses mouse moves and its own buttons. Rock paper scissors is first to three points, and scores last for the session.", "Hands.cs", "mini_game"),
            new Topic("Chess", "chess|chess board|chess difficulty",
                "Ask play chess, or play chess on easy, normal or hard. You have white unless you say I'll be black. Click a piece then a legal square, or drag it; promotion offers queen, rook, bishop and knight. Undo or take that back, new game, hint and resign work by text, voice or the board buttons. Levels change on the board. Her native engine and stock reactions need no brain or key; speech uses her installed local voice. Scores last for the session. Closing discards the position without asking. Requests for chess.com stay in the browser.", "ChessBoard.cs", "CHESS"),
            new Topic("Drawing on your screen", "drawing on your screen|draw on my screen|draw on your screen|drawing on my screen|drawing on screen|your drawing|her drawing|your ink",
                "Ask her to draw and she does it in pink right on your screen: \"draw me a cat\", \"write hi on my screen\", \"circle the start button\", \"point at the save button\". For things already on screen she looks at the monitor you're using first. Clicks go straight through the ink. It stays until you say \"clear your drawing\", \"remove it\" or \"get rid of that\", hold Esc, pick Clear her drawing from her menu, or ask for a new drawing. Say \"add\" to keep what's there.", null, null),
            new Topic("Copy a reply", "copy a reply|copy your reply|copy her reply|copy what you said|copy your message|copy your text",
                "Right click her reply bubble to copy the whole reply, even the pages of a long one that aren't showing. The bubble then shows > sys: copied. A left click dismisses the bubble.", null, null),
            new Topic("Handing her a file", "handing her a file|hand you a file|give you a file|drop a file|drag a file|attach a file|read a file|read files|files can you read|files do you read|file types|file formats|read a pdf|read a spreadsheet",
                "Drag a file onto Capri, or ask read followed by its full path; quote a path with spaces. She reads text and code files, Word .docx, Excel .xlsx, PDFs through Windows OCR, and PNG, JPG, JPEG, GIF, BMP and WebP pictures. Pictures use the selected brain when it can see, otherwise Windows OCR. Her selected brain answers from the contents, which stay available for a few follow-up turns; she reports unreadable files and any omitted contents.", "Hands.cs", "read_file"),
            new Topic("Finding files", "find files|find a folder|find game folders|finding files|file search",
                "Ask for a file or folder by its name or a plain description, like show me the path to the cyberpunk game folder. She searches names on a worker, in your personal folders and game locations first, and through your fixed drives if nothing turns up there. Results show full paths, closest match first. Say show it to me or open that folder to select the first in File Explorer; say stop to cancel a running search. She does not read file contents to search.", "Hands.cs", "find_file"),
            new Topic("Website nicknames", "website nicknames|website aliases|remember a website|name a website",
                "Say remember this website as apples, call this site apples, or remember apples is example.com/page. She reads the front browser's address without moving input, or asks for the address if unavailable. Open apples, check out the apples website, go to apples and take me to apples open that whole nickname; in chrome chooses Chrome. What websites do you know lists them, and forget the apples website removes one. Nicknames are in websites.json next to memories.md in her data folder.", "Hands.cs", "website_nickname"),
            new Topic("Moves", "your moves|move you|move capri|ride cursor|hop off|sit here|hang here|sit on windows|bring here|bring to this screen",
                "Drag Capri to place her, double-click her to ride the cursor, or right-click > Move for Sit here, Hang here, Lie down, Ride cursor and monitor choices. Sit on windows lets her land on window edges; Stand up releases a chosen seat and Hop off ends a cursor ride, also available with Ctrl + Alt + C. Mainframe > CAPRI has Bring here and Hide Capri, and double-clicking her tray icon brings her back.", "Capri.cs", "Sit on windows"),
            new Topic("Notes", "me md|your notes|her notes|open her notes|edit your notes|remember things about me",
                "Mainframe > Settings > YOU > Open her notes opens me.md in Notepad. Tell her remember or forget a fact, or edit the file and save it; she reads these notes when you chat. Get to know me writes your nickname, pronouns and interests there too.", "Mainframe.cs", "Open her notes"),
            new Topic("personality.md", "personality md|edit your personality|change your personality|personality file",
                "Mainframe > Settings > YOU > Her personality > Edit personality.md opens personality.md in Notepad, starting from her default. Rewrite how she acts and talks, save, and her next reply uses it. Her safety lines stay in her code and always come after it, so it can't change them. Reset to her default puts her back and keeps yours as personality.backup.md. On Mac and Linux it's under Settings in her right-click menu.", "Mainframe.cs", "Edit personality.md"),
            new Topic("Reminders", "your reminders|set a reminder|set reminders|reminder setting|do you have reminders|can you set reminders|can you remind me later|can you remind me tomorrow|how do reminders work",
                "Ask remind me in 20 minutes to take the pizza out, or remind me tomorrow at 9 to call Mum. She uses your PC's local clock and saves reminders in reminders.json. Ask list my reminders or cancel the pizza reminder to manage them. Capri needs to be running to deliver them; reminders overdue when she starts are announced as late. Quiet and full-screen mode show reminders without speaking.", "Hands.cs", "remind"),
            new Topic("Screen watches", "watch for something|watch something for me|your screen watches|how do screen watches work|can you keep an eye on",
                "Ask tell me when my health is low, let me know when the download finishes, warn me if the boss spawns, or tell me when someone messages me. With a brain that can see, watch_for checks the visible screen about every three seconds, more slowly if the brain takes longer. Unchanged screens get another look at least every 30 seconds. She stays quiet until a match, waits while either of you speaks, and can warn you aloud in full-screen games. Hushed and Quiet keep alerts as text. Watches fire once by default; every_time waits for a clear look before another alert. Ask what are you watching for or stop watching, optionally naming one. Watches are not saved and end when she closes. Clock-based requests use remind.", "Lookout.cs", "watch_for"),
            new Topic("Routines", "your routines|saved routines|routine setting|save a routine|set up a routine|how do routines work|routine phrases",
                "Tell her when I say focus time, open Notepad to save a routine. Saving keeps the steps for later; saying focus time runs them like an ordinary request. Ask list my routines to see them or forget the focus time routine to remove one. They are saved on this PC in routines.json.", "Hands.cs", "routine"),
            new Topic("Stop a job", "stop a job|stop your job|stop you working|cancel a job|cancel your job|escape key|esc key|stop your task",
                "Press Esc while a mouse and keyboard job is running to stop it. Moving your own mouse does not cancel the job; Quiet controls unsolicited chatter separately.", "Capri.cs", "Esc"),
            new Topic("Updates", "your updates|update you|update capri|check for updates|restart to update|your version",
                "Mainframe > CAPRI > Check for updates, or right-click > Help > Check for updates, checks for a newer version. She also checks automatically after startup and every three hours, downloads an available update and offers Restart to update. Settings and notes stay in her data folder across updates.", "Mainframe.cs", "Check for updates"),
            new Topic("Data folder", "your data|her data|data folder|where are your files|where do you store|localappdata|settings json|chat json",
                "On Windows her data lives in %LOCALAPPDATA%\\Capri; paste that path in Win + R or File Explorer. It includes settings.json, chat.json, me.md, memories.md, transcripts, reminders.json, routines.json and check-ins.md, with local brain and voice downloads in subfolders. API keys in settings are encrypted for the Windows user.", "Capri.cs", "settings.json"),
            new Topic("Security", "security setting|security presets|custom preset|your permissions|her permissions|blackwall|lockdown|guarded|trusted|bypass all permissions|why do you ask permission",
                "Mainframe > Settings > HABITS > Security opens SECURITY: Lockdown turns every toggle off, Guarded turns the first three on, and Trusted, the default, turns the first seven on. Blackwall turns all eleven on, including passwords and Listen for Hey Capri; a different mix shows Custom, and choosing a preset resets its toggles. With an action toggle off she asks first, except passwords and card numbers are yours to type and Hey Capri simply stops listening.", "Mainframe.cs", "SECURITY >"),
            new Topic("Browser choice", "choose a browser|which browser|browser choice|named browser|open websites",
                "Name Chrome, Edge, Firefox, Opera, Opera GX, Brave or Vivaldi to open a site or search there, for example open YouTube in Chrome. Otherwise she uses your default browser. If the named browser isn't installed she uses the default and says so. She opens a new tab in the named browser and brings it forward, and tells you if its window stays behind or hasn't appeared yet.", "Hands.cs", "open_website"),
            new Topic("Open apps, games and sites", "opening apps permission|open apps games and sites",
                "Mainframe > Settings > HABITS > Security > Open apps, games and sites controls opening things you request. On she opens them directly; off she asks first and does it after a yes.", "Hands.cs", "Open apps, games and sites"),
            new Topic("Music, media and volume", "music media and volume|media permission",
                "Mainframe > Settings > HABITS > Security > Music, media and volume covers playing media and adjusting PC volume. On she does it when asked; off she asks first.", "Hands.cs", "Music, media and volume"),
            new Topic("Shortcuts (screenshots, Game Bar, captions, Discord mute)", "shortcuts permission|screenshots game bar captions discord mute",
                "Mainframe > Settings > HABITS > Security > Shortcuts (screenshots, Game Bar, captions, Discord mute) controls pressing those shortcuts for you. On she presses them directly; off she asks first.", "Hands.cs", "Shortcuts (screenshots, Game Bar, captions, Discord mute)"),
            new Topic("Close and switch apps and tabs", "close and switch apps and tabs|closing apps permission",
                "Mainframe > Settings > HABITS > Security > Close and switch apps and tabs controls window and tab changes you request. On she switches or closes them directly; off she asks first.", "Hands.cs", "Close and switch apps and tabs"),
            new Topic("Shut down, restart, sleep, lock and sign out", "shut down restart sleep lock and sign out|power permission",
                "Mainframe > Settings > HABITS > Security > Shut down, restart, sleep, lock and sign out covers those actions on the PC. On she acts as soon as you ask; off she asks first, and Capri's own nap is separate.", "Hands.cs", "Shut down, restart, sleep, lock and sign out"),
            new Topic("Use your mouse and keyboard for jobs", "use your mouse and keyboard for jobs|mouse permission|keyboard permission",
                "Mainframe > Settings > HABITS > Security > Use your mouse and keyboard for jobs controls starting mouse and keyboard jobs. On she starts when asked; off she asks first, and quick music and media tools stay off her pointer.", "Hands.cs", "Use your mouse and keyboard for jobs"),
            new Topic("Send, post and delete what you asked for", "send post and delete what you asked for|send permission|delete permission",
                "Mainframe > Settings > HABITS > Security > Send, post and delete what you asked for covers those steps in your request. On she carries them out without checking again; off she asks even when you requested that step.", "Hands.cs", "Send, post and delete what you asked for"),
            new Topic("Pay, buy and book", "pay buy and book|payment permission|purchase permission",
                "Mainframe > Settings > HABITS > Security > Pay, buy and book covers purchases, payments and bookings during jobs. On she does them without checking again; off she asks before that step.", "Hands.cs", "Pay, buy and book"),
            new Topic("Things your request didn't mention", "things your request didnt mention|unasked steps|extra steps permission",
                "Mainframe > Settings > HABITS > Security > Things your request didn't mention controls extra steps needed by a job. Off she asks before unmentioned sends, posts, deletions, installs or purchases and keys that can run things, such as Win + R; on she carries on.", "Hands.cs", "Things your request didn't mention"),
            new Topic("Type passwords and card numbers", "type passwords and card numbers|password permission|card numbers permission",
                "Mainframe > Settings > HABITS > Security > Type passwords and card numbers lets her type credentials supplied by your job. Off you type them yourself; on she types them, and password fields are masked in her displayed and saved job text.", "Hands.cs", "Type passwords and card numbers"),
            new Topic("Listen for \"Hey Capri\"", "hey capri|wake word",
                "Mainframe > Settings > HABITS > Security > Listen for Hey Capri is off by default, and Blackwall turns it on. It listens locally for her name and opens voice chat when heard; the first use downloads her free voice and wake-word pack. Quiet, mic mute and voice chat pause the wake listener, and it avoids a Bluetooth headset mic to preserve playback quality.", "Hands.cs", "Listen for \"Hey Capri\"")
        };

        internal static string Tutorial
        {
            get
            {
                return "1. Right-click me > Open Capri Mainframe; under BRAIN press Set up her brain, or choose HIGHER and connect a Free key.\n" +
                    "2. Hover over me and press TYPE to chat, or TALK to use your mic.\n" +
                    "3. Under VOICE pick PREBUILT LOCAL and Get free voice if needed; use the free voice menu and Test to hear it.\n" +
                    "4. Open Settings to choose Language, your nickname, my Size and Check in on me.\n" +
                    "5. Ask me to open an app, play music or do a job; Esc stops a job.\n" +
                    "6. Try WATCH > Watch my screen with a seeing brain, and YOU > Open her notes for me.md; Ctrl + Alt + P is the default Quiet key.";
            }
        }

        private static readonly Regex Question = new Regex(@"^(?:(?:hey|hi|please|so|capri) )*(what|whats|how|why|where|which|can|could|does|do|is|are|explain|tell me|show me|give me|teach me|help|tutorial|guide|walkthrough|i want|i need)\b");
        private static readonly Regex App = new Regex(@"\b(capri|mainframe|settings?|switch|toggle|preset|option)\b");
        private static readonly Regex Game = new Regex(@"\b(video ?games?|games?|gaming|gameplay|boss|quest|mission|npc|lore|weapon|inventory|chess|minecraft|roblox|valorant|fortnite|cyberpunk|2077|night city|edgerunners|elden ring|silksong|hollow knight|skyrim|terraria|stardew|overwatch|counter strike|cs2|gta|baldurs gate|league of legends)\b");
        private static readonly Regex Walkthrough = new Regex(@"^(?:(?:please|hey|capri|can you|could you) )*(?:(?:(?:give|show) me (?:a |the )?|i (?:want|need) (?:a )?)?(?:tutorial|walkthrough|getting started guide)(?: (?:for|on|about) (?:capri|you|your app|this app))?|how (?:do|can) i use (?:you|capri)|(?:teach me |show me )?how to use (?:you|capri)|teach me to use (?:you|capri))(?: please)?$");

        private static string Plain(string text)
        {
            return Regex.Replace((text ?? "").ToLowerInvariant().Replace("'", "").Replace("\u2019", ""), @"[^a-z0-9]+", " ").Trim();
        }

        private static bool Has(string text, string words)
        {
            return (" " + text + " ").IndexOf(" " + words + " ", StringComparison.Ordinal) >= 0;
        }

        // The shortcuts she can press here, the way she'd ask to press them: "take a screenshot, open Task Manager and ...".
        private static string Pressed(Hands.Kit kit)
        {
            List<string> doing = new List<string>();
            foreach (string name in kit.Keys) doing.Add(Security.Asking("shortcut", new ToolArguments { Name = name }));
            return doing.Count < 2 ? string.Join("", doing.ToArray()) : string.Join(", ", doing.GetRange(0, doing.Count - 1).ToArray()) + " and " + doing[doing.Count - 1];
        }

        // Facts about the topics they asked about. What she can do comes from her tools here, and a brain that can't see leaves out looking.
        internal static string Matching(string asked, Hands.Kit kit = null)
        {
            string text = Plain(asked);
            if (text.Length == 0 || !Question.IsMatch(text)) return null;
            if (Regex.IsMatch(text, @"\b(favou?rite|pretty|beautiful|cute|lovely|opinion|do you like|do you love|think of|think about|feel about)\b")) return null;
            string game = text;
            foreach (string setting in new[] { "fast hands in games", "player over games", "gaming mode", "game mode", "gamer mode", "gaming mod", "game mod", "gaming mood", "free voice in games", "your game comments", "game commentary setting", "your gaming hud", "your game overlay", "clip my games", "clip my game", "clip a game", "game clips", "record my games", "mini games", "mini game", "game break", "chess setting", "chess with you", "your chess", "chess difficulty", "open apps games and sites", "shortcuts screenshots game bar captions discord mute" })
                game = (" " + game + " ").Replace(" " + setting + " ", " ").Trim();
            if (Game.IsMatch(game)) return null;
            if (Walkthrough.IsMatch(text)) return "Tutorial:\n" + Tutorial;
            bool app = App.IsMatch(text) || Regex.IsMatch(text, @"^(what (is|does)|whats|explain) (the )?(bubble|quiet|perch)( do| mean| work)?$");
            int[] scores = new int[Topics.Length];
            for (int topicAt = 0; topicAt < Topics.Length; topicAt++)
            {
                Topic topic = Topics[topicAt];
                string name = topic.PlainName;
                string separate = " " + text + " ";
                for (int longerAt = 0; longerAt < Topics.Length; longerAt++)
                {
                    string longer = Topics[longerAt].PlainName;
                    if (longer.Length > name.Length && Has(longer, name)) separate = separate.Replace(" " + longer + " ", " ");
                }
                separate = separate.Trim();
                if (app && Has(separate, name)) scores[topicAt] = 30 + name.Split(' ').Length;
                foreach (string words in topic.Words)
                    if (Has(separate, words)) scores[topicAt] = Math.Max(scores[topicAt], 20 + words.Split(' ').Length);
            }
            StringBuilder found = new StringBuilder();
            for (int pickAt = 0; pickAt < 3; pickAt++)
            {
                int best = -1;
                for (int scoreAt = 0; scoreAt < scores.Length; scoreAt++)
                    if (scores[scoreAt] > 0 && (best < 0 || scores[scoreAt] > scores[best])) best = scoreAt;
                if (best < 0) break;
                if (found.Length > 0) found.Append('\n');
                found.Append(Topics[best].Name).Append(": ").Append(Topics[best].FactsFor(kit ?? Hands.KitFor()));
                scores[best] = 0;
            }
            // On the same line: a setting named here can be one Simple Settings hides, and the help must still say where it is.
            if (found.ToString().Contains("Mainframe > Settings >")) found.Append(" A setting that isn't showing is under Advanced Settings, next to Back.");
            return found.Length == 0 ? null : found.ToString();
        }
    }
}
