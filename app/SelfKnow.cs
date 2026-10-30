using System;
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

            internal Topic(string name, string words, string facts, string file, string label)
            {
                Name = name;
                PlainName = Plain(name);
                Words = words.Split('|');
                Facts = facts;
                File = file;
                Label = label;
            }
        }

        internal static readonly Topic[] Topics =
        {
            new Topic("Mainframe", "your settings|her settings|open your settings|mainframe|settings window",
                "Right-click Capri or her tray icon and choose Open Capri Mainframe. Its main page has BRAIN, VOICE and CAPRI; Settings at the top opens the settings page, and changes are saved as you make them.", "Capri.cs", "Open Capri Mainframe"),
            new Topic("What she can do", "what can you do|what do you do|what can capri do|your features|your capabilities",
                "Talk by hovering over Capri and pressing TYPE or TALK, or use the keys in Mainframe > Settings > KEYS. She chats, speaks, keeps notes, opens apps and sites, plays music, looks at the screen and does mouse and keyboard jobs you ask for. Ask in plain words; Esc stops a running job.", "Hands.cs", "use_computer"),
            new Topic("Brain", "your brain|her brain|brain setup|set up her brain|normal brain|higher brain|max intelligence|ollama|local brain|built in brain",
                "In Mainframe > BRAIN, NORMAL uses a local brain: press Set up her brain to download one, or open Ollama and press Refresh. HIGHER uses a free NVIDIA key; MAX INTELLIGENCE accepts OpenAI, Claude or DeepSeek keys, and only the selected tier runs. Pick a model from the model menu; a brain key and a voice key do different jobs.", "Mainframe.cs", "NORMAL"),
            new Topic("Free key", "free key|nvidia key|nvidia api|connect a key|connect my key|connect an api key|brain key|api keys",
                "In Mainframe > BRAIN, press Free key, sign in to NVIDIA, then Generate API Key and copy it. Choose HIGHER, paste the key into the BRAIN box and press Connect, then choose a model there. Keys go in the Mainframe's key boxes, not in chat; they are saved encrypted on this PC.", "Mainframe.cs", "Free key"),
            new Topic("Paid brain keys", "openai key|claude key|deepseek key|chatgpt key|paid key|paid brain",
                "In Mainframe > BRAIN, choose MAX INTELLIGENCE, paste an OpenAI, Claude or DeepSeek API key and press Connect. Pick an available model in the menu; these are brain services, separate from CLOUD VOICE, and a ChatGPT subscription is not an API key.", "Mainframe.cs", "MAX INTELLIGENCE"),
            new Topic("Voice", "change your voice|change her voice|your voice|her voice|voice choices|voice settings|prebuilt local|custom local|cloud voice|elevenlabs|fish audio",
                "Mainframe > VOICE offers OFF for text only, PREBUILT LOCAL for free voices on this PC, CUSTOM LOCAL for your own model, and CLOUD VOICE for Fish Audio, ElevenLabs or OpenAI. Under PREBUILT LOCAL, press Get free voice if needed and use Free voice: Her own to pick a style; CUSTOM LOCAL's Set up opens its model panel. For CLOUD VOICE pick the service and connect there; ElevenLabs has a key box, Voice ID and model menu, and Test lets you hear the result.", "Mainframe.cs", "PREBUILT LOCAL"),
            new Topic("HD voice", "hd voice|your voice hd|voice clip|clone my voice",
                "Mainframe > VOICE > PREBUILT LOCAL's free voice menu offers Your voice (HD) and a clip picker. HD uses a clip you supply and a graphics card with at least 6 GB; Capri ships no voice clip of her own. Pick another free voice in that menu to return to a preset style.", "Mainframe.cs", "Your voice (HD)"),
            new Topic("Microphone", "your microphone|your mic|voice chat|talk button|mute mic|mute your mic|cant hear me|cannot hear me|push to talk",
                "Hover over Capri and press TALK, or use the default Ctrl + Alt + V, to start or stop voice chat; choose the input under Mainframe > VOICE > Mic. The mic button or default Ctrl + Alt + N mutes it, and Settings > KEYS can bind Push to talk to a key or mouse button. ElevenLabs uses its voice link when selected and connected; the other voice choices use free voice chat, and OFF types her answers.", "Mainframe.cs", "Mic: default"),
            new Topic("Language", "your language|her language|change language|speak another language",
                "Mainframe > Settings > YOU > Language chooses the language she replies in. Changing it starts a fresh recent chat.", "Mainframe.cs", "Language"),
            new Topic("What should she call you?", "your nickname|my nickname|change my name|what should she call you|what you call me",
                "In Mainframe > Settings > YOU, enter a nickname under What should she call you? and press Save. Clear it or enter choom to go back to choom; Get to know me asks about your name, pronouns and interests and keeps the answers in me.md.", "Mainframe.cs", "What should she call you?"),
            new Topic("Get to know me", "get to know me|your introduction|my pronouns",
                "Mainframe > Settings > YOU > Get to know me starts her questions about what to call you, your pronouns and what you're into. The answers go into me.md; say skip to pass a question or stop to end it.", "Mainframe.cs", "Get to know me"),
            new Topic("Volume", "your volume|her volume|voice volume|you are too loud|you are too quiet",
                "Mainframe > Settings > VOICE > Volume changes how loud her voice plays, from Mute to 150%. It changes Capri's voice, not the volume of other apps.", "Mainframe.cs", "Volume"),
            new Topic("Pitch", "voice pitch|your pitch|her pitch",
                "Mainframe > Settings > VOICE > Pitch makes her voice lower or higher, from -6 to +6 semitones. It applies to every voice, including ElevenLabs, and she says a sample when you release the slider.", "Mainframe.cs", "Pitch"),
            new Topic("Speed", "voice speed|your speech speed|talk faster|talk slower|speak faster|speak slower",
                "Mainframe > Settings > VOICE > Speed changes how quickly she speaks, from 0.7x to 1.5x. It affects every voice and plays a sample when you release the slider.", "Mainframe.cs", "Speed"),
            new Topic("Robot", "robot voice|robotic voice|sound robotic",
                "Mainframe > Settings > VOICE > Robot adds a robotic effect to her voice. HD and custom or cloud model voices keep their own amounts; the other voices share an amount.", "Mainframe.cs", "Robot"),
            new Topic("Glitch", "voice glitch|glitchy voice|glitch effect",
                "Mainframe > Settings > VOICE > Glitch changes the glitch effect in her speech. HD and custom or cloud model voices keep their own amounts; this slider changes sound, not her startup animation.", "Mainframe.cs", "Glitch"),
            new Topic("Voice layers", "voice layers|layers setting|layers switch|layer pitch|layer delay",
                "Mainframe > Settings > VOICE > Layers opens VOICE LAYERS, where Voice layers turns two quieter copies of her voice on or off. Each copy has its own Pitch and Delay, and the effect applies to every voice, including ElevenLabs. Hear her plays a sample and Defaults restores the layer settings.", "Mainframe.cs", "Voice layers"),
            new Topic("Sounds", "your sound effects|her sound effects|sound effects volume",
                "Mainframe > Settings > VOICE > Sounds controls her sound effects, with Off and volume steps. Voice Volume has its own slider.", "Mainframe.cs", "Sounds"),
            new Topic("Typing sound", "typing sound|typing noise|typing clicks",
                "Mainframe > Settings > VOICE > Typing sound turns the soft typing noise during her text replies on or off. Sounds in the same section controls the sound effects level.", "Mainframe.cs", "Typing sound"),
            new Topic("Free voice in games", "free voice in games",
                "Mainframe > Settings > VOICE > Free voice in games appears while HD voice is selected and is off by default. When on, a full-screen game or video makes the free voice take over from HD so the graphics card is freed for it.", "Mainframe.cs", "Free voice in games"),
            new Topic("Mini player", "mini player|music player|your player",
                "Mainframe > Settings > MOVES > Mini player shows the song Windows reports, with playback controls and a place for her to sit. It hides during full-screen games and videos; Spotify also gets a heart for liking the song.", "Mainframe.cs", "Mini player"),
            new Topic("Wander", "your wandering|her wandering|wander around|wander off|stop wandering|dont wander|why dont you move|why arent you moving",
                "Mainframe > Settings > MOVES > Wander sets Off, Rarely, Sometimes, Often or Restless; right-click > Move > Wander around also toggles it. She wanders while idle and you're active at the PC, and stays put in a spot chosen with Sit here until you choose Stand up.", "Mainframe.cs", "Wander"),
            new Topic("Sleep", "your sleep|her sleep|when do you sleep|when you fall asleep|go to sleep|wake you up",
                "Mainframe > Settings > MOVES > Sleep chooses how long you're idle before she dozes off, or Never. Moving the mouse wakes an idle nap; after asking her to sleep or choosing Move > Go to sleep, click her or tell her to wake up.", "Mainframe.cs", "Sleep"),
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
                "Mainframe > Settings > WATCH > Watch my screen enables occasional comments, with Every setting the time between glances. Seeing requires a vision-capable brain: DeepSeek and text-only models cannot take images; under BRAIN a seeing NVIDIA choice is nvidia/nemotron-3.5-super-vl-120b-a12b. She skips unchanged or blank captures, Quiet pauses glances, and during voice chat she comments only while the mic is muted; asking her to look is a separate action.", "Mainframe.cs", "Watch my screen"),
            new Topic("Keep her caught up", "keep her caught up|keep you caught up|caught up setting|silent glances",
                "Mainframe > Settings > WATCH > Keep her caught up lets glances she stays quiet about still enter her screen memory. It works with Watch my screen and can use more of your brain API; she keeps screen observations for three hours.", "Mainframe.cs", "Keep her caught up"),
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
                "Mainframe > Settings > HABITS > Use her cursor lets quick tasks such as music use her visible pointer when a brain and the mouse permission are available. Off uses the quick tools instead; it does not turn off explicit mouse and keyboard jobs.", "Mainframe.cs", "Use her cursor"),
            new Topic("Remember our chats", "remember our chats|memories md|chat transcripts|open her memories|your memories",
                "Mainframe > Settings > HABITS > Remember our chats keeps daily transcripts and folds older chats into memories.md on this PC, with Open her memories opening their folder. Turning it off stops writing, folding, searching and reading these memories without deleting them. With a cloud brain, relevant recalled text and chats being folded go to that selected brain as context.", "Mainframe.cs", "Remember our chats"),
            new Topic("Perch", "perch fps|perch frame rate|perch setting|your animation speed",
                "Mainframe > Settings > HABITS > Perch selects the frames per second while she sits on a window. The sway takes the same time at each setting; above 30 fps she draws intermediate frames for smoother movement.", "Mainframe.cs", "Perch"),
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
            new Topic("Gaming Mode", "gaming mode|your gaming hud|your game overlay|move the hud|move your hud|reset the hud|hud position|gaming mode key",
                "Mainframe > Settings > GAMES > Gaming Mode, or default Ctrl + Alt + G, docks Capri in a small HUD over the game. Settings > KEYS changes that key; press it again to turn the mode off. While the HUD is up, ask move the HUD down, up, left or right, or reset the HUD to put it back. She saves its position per game; Gaming Mode changes her display and does not by itself ask her to play.", "Mainframe.cs", "Gaming Mode"),
            new Topic("Clip my games", "clip my games|clip my game|clip a game|game clips|saved clips|clips saved|your clips|clip that|record my games",
                "Mainframe > Settings > GAMES > Clip my games is off by default. When on, it keeps up to the last two minutes of a detected game's video and PC playback sound in memory while the game is in front. Say clip that to save the last 30 seconds, or clip the last two minutes for a longer clip of what is buffered. Clips are MP4 files in Videos\\Capri Clips on this PC; she tells you the saved path.", "Mainframe.cs", "Clip my games"),
            new Topic("Mini games", "mini games|mini game|minigames|tic tac toe|tictactoe|connect four|connect 4|rock paper scissors|desktop boards",
                "Ask play tic-tac-toe, play connect four or play rock paper scissors to open her own desktop board beside her, even without a brain connected. Click a move or tell her a square, column or hand. On the board, tic-tac-toe uses 1-9, connect four 1-7, and rock paper scissors R, P or S; arrows and Enter also work. N starts another round and Esc closes the board. Rock paper scissors is first to three points, and scores last for the session.", "Hands.cs", "mini_game"),
            new Topic("Copy a reply", "copy a reply|copy your reply|copy her reply|copy what you said|copy your message|copy your text",
                "Right click her reply bubble to copy the whole reply, even the pages of a long one that aren't showing. The bubble then shows > sys: copied. A left click dismisses the bubble.", null, null),
            new Topic("Handing her a file", "handing her a file|hand you a file|give you a file|drop a file|drag a file|attach a file|read a file|read files|files can you read|files do you read|file types|file formats|read a pdf|read a spreadsheet",
                "Drag a file onto Capri, or ask read followed by its full path; quote a path with spaces. She reads text and code files, Word .docx, Excel .xlsx, PDFs through Windows OCR, and PNG, JPG, JPEG, GIF, BMP and WebP pictures. Pictures use the selected brain when it can see, otherwise Windows OCR. Her selected brain answers from the contents, which stay available for a few follow-up turns; she reports unreadable files and any omitted contents.", "Hands.cs", "read_file"),
            new Topic("Moves", "your moves|move you|move capri|ride cursor|hop off|sit here|hang here|sit on windows|bring here|bring to this screen",
                "Drag Capri to place her, double-click her to ride the cursor, or right-click > Move for Sit here, Hang here, Lie down, Ride cursor and monitor choices. Sit on windows lets her land on window edges; Stand up releases a chosen seat and Hop off ends a cursor ride, also available with Ctrl + Alt + C. Mainframe > CAPRI has Bring here and Hide Capri, and double-clicking her tray icon brings her back.", "Capri.cs", "Sit on windows"),
            new Topic("Notes", "me md|your notes|her notes|open her notes|edit your notes|remember things about me",
                "Mainframe > Settings > YOU > Open her notes opens me.md in Notepad. Tell her remember or forget a fact, or edit the file and save it; she reads these notes when you chat. Get to know me writes your nickname, pronouns and interests there too.", "Mainframe.cs", "Open her notes"),
            new Topic("personality.md", "personality md|edit your personality|change your personality|personality file",
                "This version keeps her personality in Brain.cs, not in an editable personality.md. An old personality.md is not used; nickname, language, voice and her notes are still adjustable in Mainframe > Settings.", null, null),
            new Topic("Reminders", "your reminders|set a reminder|set reminders|reminder setting|do you have reminders|can you set reminders|can you remind me later|can you remind me tomorrow|how do reminders work",
                "Ask remind me in 20 minutes to take the pizza out, or remind me tomorrow at 9 to call Mum. She uses your PC's local clock and saves reminders in reminders.json. Ask list my reminders or cancel the pizza reminder to manage them. Capri needs to be running to deliver them; reminders overdue when she starts are announced as late. Quiet and full-screen mode show reminders without speaking.", "Hands.cs", "remind"),
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
            new Topic("Listen for \"Hey Capri\"", "hey capri|wake word|listen for your name",
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

        internal static string Matching(string asked)
        {
            string text = Plain(asked);
            if (text.Length == 0 || !Question.IsMatch(text)) return null;
            if (Regex.IsMatch(text, @"\b(favou?rite|pretty|beautiful|cute|lovely|opinion|do you like|do you love|think of|think about|feel about)\b")) return null;
            string game = text;
            foreach (string setting in new[] { "gaming mode", "free voice in games", "your game comments", "game commentary setting", "your gaming hud", "your game overlay", "clip my games", "clip my game", "clip a game", "game clips", "record my games", "mini games", "mini game", "game break", "open apps games and sites", "shortcuts screenshots game bar captions discord mute" })
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
                found.Append(Topics[best].Name).Append(": ").Append(Topics[best].Facts);
                scores[best] = 0;
            }
            return found.Length == 0 ? null : found.ToString();
        }
    }
}
