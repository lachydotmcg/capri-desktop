# Capri help: knowledge base

Plain answers for setting up and using Capri. The Help page (site/help/index.html) is made from this file, and the support chat on the website answers from it.

Written for Capri 0.7.0 on Windows, and the Mac and Linux preview. If you're on 0.6.2 or older, some labels differ: see "Older versions" near the end.

Menu paths are written like this: right-click her > Open Capri Mainframe. Button and menu names are in the same words the app uses.

---

## 1. Quick facts

### What is Capri?
A desktop companion. She sits on your windows and taskbar, chats with you, talks out loud with her free voice (or ElevenLabs if you add a key), opens apps and plays music for you, does jobs on your PC with her own pointer, and can comment on your screen. Capri is a free fan project and the code is open on GitHub: https://github.com/lachydotmcg/capri-desktop

### What does it run on?
- Windows 10 or 11, 64-bit. This is the full app.
- Mac with macOS 15 or newer (Apple Silicon or Intel). Early preview.
- Linux x64. Early preview.

### What does it cost?
- Capri: free.
- Her built-in brain: free, runs on your PC.
- Her free voice: free, runs on your PC.
- The NVIDIA key: free.
- ElevenLabs (her best voice): has a free plan with monthly credits. Paid plans give more credits and voice cloning.
- OpenAI, Claude or DeepSeek keys: these need paid credits on their sites.

### How can I support Capri?
Capri is free. To support the people who make her, tip them on Ko-fi. The links are also at the bottom of every page of the website.
- Lachy, developer: https://ko-fi.com/lachysdev
- lance and xoru, project manager and head tester, who share one tip jar: https://ko-fi.com/xorulance
- Guh: https://ko-fi.com/guh703214
- BaldDcMod, Discord moderator: https://ko-fi.com/balddcmod

### Do I need anything else installed?
No. On Windows the installer has everything. You don't need Claude Code, Python, Ollama or anything else. Ollama is optional.

### What PC do I need?
| | NORMAL: brain on your PC | HIGHER or MAX INTELLIGENCE: brain from a key |
| --- | --- | --- |
| Memory (RAM) | 8 GB, 16 GB is better | 4 GB |
| Graphics card | Optional, 4 GB or more makes her faster | Not needed |
| Space | 3 to 5 GB | Under 100 MB |
| Internet | Only for the download | Always |

A mic is only needed for voice chat.

### Where do I get it?
- Website: https://lachydotmcg.github.io/capri-desktop/
- Windows: Capri-Setup.exe
- Mac (Apple Silicon): Capri-mac-apple-silicon.zip
- Mac (Intel): Capri-mac-intel.zip
- Linux: Capri-linux-x64.tar.gz
- All files: https://github.com/lachydotmcg/capri-desktop/releases/latest

---

## 2. Install on Windows

### Steps
1. Download Capri-Setup.exe from the website.
2. Open it.
3. If you see "Windows protected your PC": More info > Run anyway.
4. In the "Install Capri" window, press Install Capri.
5. It then says "Installing the local speech model": that's her free voice, about 316 MB. "This may take some time. For a better sounding, faster voice, get an API key." You can press Skip and get it later from the Mainframe.
6. Capri opens when it's done (the "Open Capri when finished" box is ticked by default).

The installer also has "Add a desktop shortcut" (off by default). While it works it says "Making a little room for Capri…".

If the speech model fails it says "Couldn't install the local speech model" and "Capri is installed. Get it later in Capri Mainframe, or add an API key." Press Finish. Capri still works; get her free voice later with Get free voice under VOICE.

### Why does Windows warn me?
Capri is new and not signed yet, so SmartScreen doesn't know it. That's normal for new apps. The code is open on GitHub, so anyone can check it.

### Does it need admin rights?
No. It installs for your Windows user only, into %LOCALAPPDATA%\Capri (open the Run box with Win + R and paste that to see it).

### What does the installer add?
- Capri in %LOCALAPPDATA%\Capri
- Her free voice (the local speech model, about 316 MB) in %LOCALAPPDATA%\Capri\voice
- A "Capri" Start menu shortcut
- A desktop shortcut, only if you ticked it
- An entry in Settings > Apps > Installed apps, so you can uninstall it like any app

The first time the installed Capri runs, she also sets herself to start with Windows. You can turn that off (see below).

### Does Capri start with Windows?
Yes, by default. To stop it, open Capri Mainframe, click Settings, and turn off Start with Windows under HABITS. Turning her off in Task Manager > Startup apps works too, and the switch in Settings shows it. She only sets this once: if you turn it off, it stays off after updates. A Capri.exe you run from somewhere else, like Downloads, doesn't start with Windows unless you turn the switch on in that copy.

### Why does my Discord say "Playing Capri"?
That's her Discord status. While Discord is open on the same PC, your profile shows "Playing Capri" with what she's doing in a few plain words, how long she's been running, and buttons to get Capri and join her Discord. It never shows song names or what her jobs are about. To turn it off, open Capri Mainframe, click Settings, and turn off Show on Discord under HABITS. On Mac and Linux it's right-click her > Settings > Show on Discord. If friends can't see it, turn on Share your activity in Discord's User Settings > Activity Privacy.

### "Item Not Found" or the installer disappeared
Your browser or antivirus held the file back.
1. In Chrome or Edge, press Ctrl + J and pick Keep on Capri-Setup.exe.
2. If your antivirus removed it, restore it from quarantine.
3. Open it from your Downloads folder, not from the browser's download bar.

The same fix applies to "Windows cannot access the specified device, path, or file".

### My antivirus says it's a virus or Trojan
If you downloaded it from the Capri website or GitHub, it's a false alarm on a new, unsigned app. Names people have seen:
- Microsoft Defender: Trojan:Win32/Wacatac.H!ml
- Bitdefender: Gen:Variant.Zusy or Gen:Suspicious.Cloud
- Malwarebytes: MachineLearning/Anomalous.100%
- Kaspersky: PDM:Trojan.Win32.Bazon.a on Capri.exe, HEUR:Trojan.MSIL.Startun.gen or UDS:Trojan.MSIL.Startun.gen on Capri-Setup.exe, and UDS:DangerousObject.Multi.Generic on Uninstall.exe or an older installer. PDM means Kaspersky's behaviour check flagged what the app does, not a known virus. Capri reads your shortcut keys, takes screenshots when you ask and moves the mouse for jobs, which behaviour checks can mistake for spyware. HEUR is a rule-of-thumb guess, and UDS is Kaspersky's cloud flagging a file that few people have run yet. MSIL only means a .NET app, which Capri is. Kaspersky Endpoint Security is usually run by a school or work IT team, so ask them to allow it.

These are generic, machine-learning or behaviour detections. They often flag new apps that aren't signed yet. Restore the file from quarantine (or allow it in your antivirus), then run it again. If you want to be sure, check the code on GitHub or check the download's SHA256 (below).

On VirusTotal, a few of about 70 engines flagging Capri.exe or Capri-Setup.exe is the same kind of detection. People have seen Bkav Pro (W32.Malware), SecureAge (Malicious) and Trapmine (Suspicious.low.ml.score). The SHA256 check below tells you whether your file is the one on the release page.

### My browser won't download it
- Opera "Failed - Insufficient permissions" (French: "Échec - Autorisations insuffisantes"): try another browser like Chrome or Edge, or download it from https://github.com/lachydotmcg/capri-desktop/releases/latest.
- On a phone you may see a "dangerous download" warning. Capri doesn't run on phones. Download it on your PC.

### How do I check my download is the real one?
Each release has a SHA256SUMS.txt file on its GitHub release page.
1. Open PowerShell in your Downloads folder.
2. Run: Get-FileHash Capri-Setup.exe
3. The long code should match the line for Capri-Setup.exe in SHA256SUMS.txt.

### "Quit Capri from her tray menu, then install again."
Capri is already running. Right-click her > Quit Capri, then run the installer again.

### "There's a Capri.exe in <folder> that isn't this Capri."
Another file called Capri.exe is in the install folder. Move it out of that folder, then install again.

### "Capri could not finish. Your files are still here."
The install stopped partway. Nothing you saved was lost. Close Capri if it's open and run the installer again. If it keeps happening, ask in the Discord.

### "Unable to save shortcut" or "Windows wouldn't let the installer save her shortcut"
Windows, often an antivirus, stopped the installer saving her Start menu shortcut. Capri.exe is already installed.
1. Press Win + R, type %LOCALAPPDATA%\Capri and press Enter.
2. Right-click Capri.exe and pick Pin to Start. Double-click it to open her.
3. For the shortcut itself, allow Capri-Setup.exe in your antivirus and run it again.

Newer installers finish the install without the shortcut, say why, and show you Capri.exe.

---

## 3. Install on Mac (preview)

### Steps
1. Download the Mac zip: Apple Silicon (M1 and newer) or Intel.
2. Unzip it and drag Capri into Applications.
3. Open Terminal (Cmd + Space, type Terminal, press Enter).
4. Run this once:
   xattr -dr com.apple.quarantine /Applications/Capri.app
5. Open Capri from Applications.

The zip has a file called "If Capri won't open.txt" with the same steps.

### Why does macOS say Capri is "damaged" or can't be opened?
Apple hasn't approved (notarized) Capri yet, so macOS blocks apps downloaded from the internet until you clear the flag. The xattr line above only clears the downloaded-from-the-internet flag on Capri. It doesn't change anything else on your Mac.

Another way, when macOS shows the "can't be opened" message (not the "damaged" one): System Settings > Privacy & Security > Open Anyway.

### Which macOS do I need?
macOS 15 or newer. Older versions like macOS 12 aren't supported.

### Where is she? There's no Dock icon.
That's normal. Capri has no Dock icon. Use her menu bar icon or right-click her.

### Her brain on Mac
The Mac preview uses Ollama for her brain. See "Ollama" in section 9.

---

## 4. Install on Linux (preview)

### Steps
1. Download Capri-linux-x64.tar.gz.
2. Unpack it.
3. Run Capri/Capri.
4. Install Ollama from https://ollama.com and open it.
5. Right-click her > Brain > Download gemma4:e4b, her brain (a few GB). Or run: ollama pull gemma4:e4b

### X11 or Wayland?
- X11: she sits on your windows.
- Wayland: she sits on the bottom of your screen only. She runs through XWayland there, so keep XWayland on.

### Hyprland, Sway and other tiling desktops
They tile new windows, so Capri gets squeezed into a tile.
1. With Capri open, find her window's class: on Hyprland run hyprctl clients. It's usually Capri.
2. Add window rules for that class that make it float and pin it, so she's on every workspace. If a border, shadow or blur shows around her, turn those off for her class too.
3. The rule syntax changes between Hyprland versions, so check the Window Rules page of the Hyprland wiki for yours. On Sway it's a for_window rule with floating enable and sticky enable.

### Her brain on Linux
The Linux preview uses Ollama. If you installed Ollama but she says her brain is offline or disconnected, you haven't downloaded a model yet. Right-click her > Brain > Download gemma4:e4b. She says "Downloading my brain..." and the Brain menu shows how far it's got; when it's in she says "My brain's in. Talk to me, choom." Or run: ollama pull gemma4:e4b, then right-click her > Brain and pick it. No Ollama, or a slow PC? Right-click her > Brain > Get a free NVIDIA key.

---

## 5. First run (Windows)

### What happens the first time?
1. She glitches in near the middle of your screen and drops onto a window or the taskbar.
2. The Capri Mainframe opens by itself (only on the first run).
3. Under BRAIN, press Set up her brain. She downloads a brain that fits your PC.
4. She says hi when it's done.
5. She talks out loud with her free voice. For another voice, click CLOUD VOICE under VOICE: Fish Audio has a free tier, and ElevenLabs uses your credits (section 11). Before 0.7.6 that row was ELEVENLABS.

### She says something when she starts
Unless you've already said something, she greets you in her own recorded voice a couple of seconds after starting:
- "Good morning, choom." (5 am to noon)
- "Isn't it late, choom?" (10 pm to 5 am)
- "Miss me, choom?" (the rest of the day)

This needs no key and uses no credits.

### Where did the Mainframe go after the first run?
After the first run, the Mainframe starts minimized. Open it with right-click her > Open Capri Mainframe, or from her tray icon.

### I opened Capri again and nothing new appeared
Only one Capri runs at a time. Opening her again while she's running brings her to your mouse pointer and opens the Mainframe.

---

## 6. Talking to her

### How do I type to her?
1. Move your mouse over her. A small pill with two buttons shows under her: TYPE with the pencil and TALK with the wave.
2. Press the pencil (TYPE).
3. Type (up to 240 letters) and press Enter.

Esc or clicking away closes the box. Your message shows as "> you:" and her reply as "CAPRI:".

### The wave button
The wave (TALK) starts voice chat. With FREE VOICE picked (or no ElevenLabs key) it's free voice chat: you talk, she answers out loud. See section 12.

Since 0.7.6 the pill looks like the rest of her: a black plate with cut corners and a pink-to-cyan neon edge that glitches in when it shows. During voice chat (Windows) a mic button sits on its right, the wave's bars light up with your mic level, and TALK turns into a red MUTED, with the mic button edged in red, while your mic is muted. It's the same size, in the same place, on Windows, Mac and Linux.

### How long do her bubbles stay up?
It depends on how long the reply is (4 seconds plus a bit per letter, up to 14 seconds). Mainframe > Settings > BRAIN > Bubble changes it: Quick, Normal or Long. On Mac and Linux it's right-click her > Settings > Bubble.

### Does she remember what we talked about?
Yes. She keeps the chat for up to a week, even after a restart. Mainframe > Settings > Memory sets how many recent messages she keeps in mind: Short (8), Medium (16) or Long (32). Changing her language starts a fresh chat; changing her model carries on the same one, on Mac and Linux too.

She also keeps in mind what she saw on your screen in the last 3 hours, so "what did you see?" gets a real answer instead of a guess (section 16).

Older chats aren't lost either. She writes every chat into a daily transcript on your PC and folds older chats into memories.md, which she reads every time you talk. She also digs through her transcripts by herself: mention something from an older chat, like the boss you beat last week, and she finds that moment and can bring it up, no "remember when" needed (from 0.7.6). Ask "remember when we beat that boss?" and she pulls up every matching line she can find (section 13).

For things she should never forget, use her notes (section 13).

### Clicking her
- Click: she glitches and reacts.
- Double-click: she rides your cursor (section 14).
- Right-click: her menu.
- Rub your cursor back and forth over her: she likes being petted and says something, about once every 20 seconds at most. She has 14 pat lines and says every one before she repeats any.

### She sounds like ChatGPT, or says she did something she didn't
That was 0.7.0 to 0.7.2: she gushed over everything, sometimes said "On it" or "Call ended" without doing anything, and after a job sometimes said you did it ("nice, you opened Notepad"). 0.7.3 fixes all three. She's a playful, teasing chaos gremlin again who's openly fond of you, when she says she's doing a job she either does it or tells you she hasn't, and she says what she did.

### She keeps asking if you're still there on voice calls
Before 0.7.3, ElevenLabs voice calls made her say something like "you still there?" whenever you went quiet or muted. In 0.7.3 she stays quiet until you talk to her. Update (right-click her > Check for updates). Going back to 0.7.1 doesn't help: it has the same brain as 0.7.2.

### She keeps popping up or asking for attention
That's Check in on me (called Pop in on me before 0.7.3), on by default since 0.7.3. Every 8 to 20 minutes she glitches in to say hi, and if you're using your PC but haven't clicked or talked to her for about 2 hours, she asks for some attention. With Wander off (Settings > MOVES), or when you've put her somewhere with Sit here or Hang here, she says hi and asks from where she is instead of moving (from 0.7.6). To see her less or more, open Capri Mainframe, click Settings, and set Pop-ins under MOVES to Rarely (every 20 to 40 minutes) or Often (every 3 to 8 minutes). She also cheers when Cyberpunk 2077 starts or a 2077 or Edgerunners song comes on in Spotify or YouTube, at most once every two hours for each, and when your CPU sits near 100% for about a minute she jokes that she might go cyberpsycho (at most every half hour, never over a full-screen game). To stop all of it, open Capri Mainframe, click Settings, and turn off Check in on me under CAPRI. It stays off after updates. On Mac and Linux it's right-click her > Settings > Check in on me, and Pop-ins sets how often. There she pops in to say hi, even when hidden, asks for attention, cheers for Cyberpunk 2077 and Night City songs, and makes the lag joke, like on Windows.

### She keeps asking me to confirm things
From 0.7.3 anything you ask counts as already allowed: she does every part of it, picks the song or video herself when you leave it to her, and doesn't ask if you're sure. From 0.7.6 what she asks about is yours to pick in the SECURITY window: Capri Mainframe > Settings > HABITS > Security, where Blackwall mode was (section 15, "Security: what she does without asking"). On Trusted, the default, she still asks before buying or paying, or before sending, posting, deleting or installing something you didn't mention. Pick Blackwall to stop even that (before 0.7.6 that was Blackwall mode, and before 0.7.4 Bypass all permissions). If she asks something like "Open Spotify? Say yes and I will." about things she used to just do, one of her toggles is off: turn it back on, or pick Trusted. On Mac and Linux it's right-click her > Settings > Security.

### Can she open a few apps at once?
Yes: "open Discord, Spotify and Steam" opens all three, and so does a list of sixty. There's no ceiling on how many, on any brain. When a list runs past what her brain's reply has room for, she asks it again with double the room, as many times as it takes, so a long list takes a few seconds longer, more on a brain running on your PC. If a brain still can't finish it, every app it wrote out in full opens anyway (from 0.7.6).

---

## 7. The Capri Mainframe

### How do I open it?
Right-click her > Open Capri Mainframe. You can also use her tray icon (bottom right of the taskbar, maybe inside the ^ arrow).

### The top bar
- MAINFRAME and her version number (v0.x.x)
- Settings: opens the Settings page next to the main page, so every setting shows at once. On that page the same button says Back.
- Minimize and close. Close (X) only hides the Mainframe. It doesn't quit Capri.

The main page has three sections: BRAIN, VOICE and CAPRI.

### BRAIN section
- The top line shows which brain she's using:
  - "Ollama · <model>" with "Running on this PC"
  - "Built-in · <name>" with "Running on this PC"
  - "NVIDIA · <model>", "ChatGPT · <model>", "Claude · <model>" or "DeepSeek · <model>" with "Using your <company> key"
  - "No brain yet" or "Setup stopped" when she has none
  - "Looking for a brain..." while she checks
- Model menu: pick a different model for the brain she's using. Your chat carries on with the new model, and with a new tier or key too.
- Refresh: looks for her brain again (for example after you open Ollama).
- Setup steps, when she has no brain yet:
  - ✓ your RAM and graphics card
  - 2 Download <name> (<size>)
  - 3 Wake her up
  - "Best fit for this PC: <name>"
- Set up her brain: starts the download. It changes to Cancel while it runs, and Try again if it fails.
- "Have Ollama? Open it, then press Refresh."
- "Pick one. Only one brain runs at a time." With HIGHER picked, this line says "Most NVIDIA models can't see your screen." (since 0.7.1). Three rows:
  - NORMAL: "Free · local model", marked "Start with this!". She runs on Ollama or her built-in brain. New installs start here.
  - HIGHER: "Free · NVIDIA API". The Free key button on this row opens the NVIDIA page where you make a free key.
  - MAX INTELLIGENCE: "ChatGPT or Claude API". Needs a key with paid credits. DeepSeek keys work here too.
- The key box shows when HIGHER or MAX INTELLIGENCE is picked: "Paste your free NVIDIA key" or "Paste a ChatGPT or Claude key". Paste a key and press Connect. Once saved it shows "NVIDIA key saved, ends XXXX" or "Key saved, ends XXXX".
- Each row keeps its own key. Picking another row doesn't delete a key.
- Paste the other kind of key and she moves to its row: an nvapi- key goes to HIGHER, a ChatGPT, Claude or DeepSeek key goes to MAX INTELLIGENCE.
- A row with no key yet runs her NORMAL brain until you connect one.
- Forget: shows in red when the box is empty and a key is saved. It removes that row's key and moves her back to NORMAL.
- If you had a key saved before the rows came in, she starts on that key's row.

### VOICE section
- "Ctrl + Alt + V to talk" at the top right: click it to change the key (it opens Settings with Talk ready to change). Since 0.7.1 it shows for everyone, free voice chat too (in 0.7.0 it only showed once an ElevenLabs key was saved).
- Three rows since 0.7.1 (0.7.0 had only FREE VOICE and ELEVENLABS). Click one to pick it:
  - OFF: "Silent · text only". She never talks out loud: her replies show as text in her bubble. No voice download, no credits. The rows under it fold away. Voice chat still works as free voice chat, and she types back. Test under it says "Her voice is OFF. Pick FREE VOICE or ELEVENLABS to hear her."
  - FREE VOICE: "Free · runs on your PC". Her free voice. No key, no credits.
  - ELEVENLABS: "Sounds amazing · paid". Her best voice, using your ElevenLabs credits. The Setup guide button on this row opens the setup guide's voice steps.
- With FREE VOICE picked: "Free voice: Her own" picks her free voice style (Her own, Bright, Sly or Sharp). Get free voice downloads it if it isn't installed (it changes to Cancel while it downloads).
- With ELEVENLABS picked:
  - The key box: "Paste ElevenLabs API key", then Connect. Once saved: "Key saved, ends XXXX". Forget (red) removes it.
  - Voice ID: paste your Voice ID, or pick from the list of your ElevenLabs voices (the arrow shows when your account has voices). Press Enter or click out of the box to save it.
  - Model: "Default model" or an ElevenLabs model like v4.
- From 0.7.6 there are four rows. FREE VOICE is now PREBUILT LOCAL, and ELEVENLABS moved into CLOUD VOICE:
  - OFF: "Silent · text only", as above. Test under it says "Her voice is OFF. Pick another row under VOICE to hear her."
  - PREBUILT LOCAL: "Her voices · free". Her free voice and its styles, HD and Your voice (HD), in the "Free voice" menu. No key, no credits.
  - CUSTOM LOCAL: "Your own model". A voice model on your PC: a voice model file, Fish Speech, Kokoro or any OpenAI-style server. Its Set up button opens the voice model panel with only those choices. Once one is set up, clicking the row brings it straight back, and the "Voice model: <name>" button under it opens its panel again.
  - CLOUD VOICE: "Needs a key". The menu under it picks Fish Audio, ElevenLabs or OpenAI. Fish Audio comes first, since its free tier needs no credits: a new install starts on it, and anyone with an ElevenLabs key saved starts on ElevenLabs. Clicking CLOUD VOICE with nothing set up opens Fish Audio's panel, and the row stays picked with its menu, so ElevenLabs is one click away. ElevenLabs keeps its key box, Voice ID and Model there, as above. Fish Audio and OpenAI open the voice model panel with only those two: "Set up Fish Audio..." at first, then "Voice, key and model..." once it's in use. Setup guide on this row opens ElevenLabs' voice steps, or the panel for Fish Audio and OpenAI.
  - CUSTOM LOCAL and CLOUD VOICE each keep their own setup and key, so switching between them loses neither. CLOUD VOICE's menu keeps the service used there last, so clicking CLOUD VOICE again (even after a restart) brings back that one.
- Mic: "Mic: default" or "Mic: <name>". Click it to pick a mic. "Windows default" follows your Windows setting.
- Test: she says a test line out loud.
- "Ctrl + Alt + N mutes mic": click it to change the key (it opens Settings with Mute mic ready to change).
- A line under VOICE shows the voice status, like "Free voice, runs on your PC.", "ElevenLabs · <voice name>", "Needs your Voice ID.", or the last voice error and its fix.
- While her free voice or ears download, a row with a dotted bar shows the download, like "Installing the local speech model · 100 MB of 316 MB".

### CAPRI section
- Hide Capri / Show Capri
- Bring here: brings her to your mouse pointer
- Ride cursor / Hop off
- Check for updates
- Help: opens the Help page on the website, where you can ask Capri for help in the corner
- Quit Capri (red)

Size, Check in on me, Always on top and Watch my screen are on the Settings page now.

### Settings page (Mainframe > Settings)
Settings opens beside the main page. Left column:

YOU
- Language: the language she replies in. 80 to pick from: English, Español, Français, Deutsch, Italiano, Português, Català, Galego, Euskara, Nederlands, Afrikaans, Svenska, Dansk, Norsk, Íslenska, Suomi, Eesti, Latviešu, Lietuvių, Polski, Čeština, Slovenčina, Magyar, Slovenščina, Hrvatski, Bosanski, Srpski, Shqip, Română, Gaeilge, Cymraeg, Malti, Türkçe, Azərbaycanca, ქართული (Georgian), Հայերեն (Armenian), Oʻzbekcha, Русский, Українська, Беларуская, Български, Македонски, Қазақша, Монгол, Ελληνικά, Tiếng Việt, Bahasa Indonesia, Bahasa Melayu, Basa Jawa, Filipino, Kiswahili, Hausa, Yorùbá, Igbo, isiZulu, isiXhosa, Soomaali, አማርኛ (Amharic), ไทย (Thai), ລາວ (Lao), ខ្មែរ (Khmer), မြန်မာ (Burmese), 简体中文 (Simplified Chinese), 繁體中文 (Traditional Chinese), 日本語 (Japanese), 한국어 (Korean), हिन्दी (Hindi), नेपाली (Nepali), বাংলা (Bengali), मराठी (Marathi), ગુજરાતી (Gujarati), ਪੰਜਾਬੀ (Punjabi), தமிழ் (Tamil), తెలుగు (Telugu), ಕನ್ನಡ (Kannada), മലയാളം (Malayalam), العربية (Arabic), اردو (Urdu), فارسی (Persian) and עברית (Hebrew). Her own ears hear 25 of them in free voice chat: English, Español, Français, Deutsch, Italiano, Português, Nederlands, Svenska, Dansk, Suomi, Eesti, Latviešu, Lietuvių, Polski, Čeština, Slovenčina, Magyar, Slovenščina, Hrvatski, Română, Malti, Русский, Українська, Български and Ελληνικά. The rest use Windows speech when Windows has it for that language. Indian scripts, Amharic, Georgian, Armenian, Lao, Khmer and Burmese are measured as they're drawn, so their lines fit her bubble too. Arabic, Urdu, Persian and Hebrew lines sit against the right of her bubble. Her HD voice and ElevenLabs speak Chinese, Japanese and Korean, and ElevenLabs speaks Hindi and Arabic; her free voice only has English pronunciation for now. Thai, Lao, Khmer and Burmese have no spaces between words, so her bubble breaks their lines between words (on Mac and Linux, between letters). Her own font has no Burmese, so Burmese draws in Windows' Myanmar Text font.
- "What should she call you?": type a nickname, press Save.
- Get to know me: she asks what to call you, your pronouns and what you're into in chat, and keeps the answers in me.md. See "Getting to know you" in section 13.
- Open her notes: opens me.md, her notes about you, in Notepad.

VOICE ("Every voice, ElevenLabs too" on the right of the heading, then the Layers button)
- Layers (0.7.6 and newer): the button at the right of VOICE's heading opens her VOICE LAYERS window. Its word lights up cyan while they're on. See "Voice layers" under Her voice.
- Volume: Mute, 10% to 100%, 125%, 150%. Default 100%.
- Pitch (from 0.7.5): -6 to +6 semitones. Default Normal. Higher or deeper, at the same speed.
- Speed (from 0.7.5): 0.7x to 1.5x. Default Normal.
- Robot (from 0.7.5): Off, 5%, 10%, 15%, then 20% to 100%. Default Off for every voice, HD too, which shows as "Off · HD" while HD is the voice she's using. A smooth synth under her voice that follows her pitch on the nearest note, like a talk box. Low settings add a light robotic shimmer; 100% is all robot.
- Glitch (from 0.7.5): Off, 5%, 10%, 15%, then 20% to 100%. Default Off. Stutters, crushed bits and skips in her voice, more as it goes up.
- Sounds: her sound effects. Off, or 10% to 100%. Default 60%.
- Typing sound: "When she types". A soft typing sound while her text replies type out.
- Free voice in games (0.7.6 and newer): only shows while her HD voice is on, beside Typing sound. Off by default, so her HD voice keeps talking while a game or video is full screen. Turn it on and her free voice talks while anything is full screen, so the game gets your graphics card back.

Pitch, Speed, Robot and Glitch change every voice she has: her free voice styles, HD, ElevenLabs and ElevenLabs voice chat. See "Pitch, speed, robot and glitch" under Her voice. Her voice layers do too; see "Voice layers".

MOVES
- Mini player: on by default, on the right of the MOVES heading. While music plays, a small player shows what's playing, and she perches on it. See "Her mini player" in section 14.
- Wander: Off, Rarely, Sometimes, Often or Restless.
- Sleep: Never, 1 min, 2 min (default), 5 min, 10 min or 30 min.
- Pop-ins: how often Check in on me brings her over to say hi. Rarely (every 20 to 40 minutes), Sometimes (every 8 to 20, default) or Often (every 3 to 8).
- Dance to music: she dances when songs play.
- Glitch in at startup: crimson tendrils and her sound when she starts. It sits beside Dance to music.

Right column:

CAPRI
- Size slider: Tiny, Small, Small+, Medium, Medium+, Large, Large+, Huge, Huge+, Giant
- Check in on me (called Pop in on me before 0.7.3): on by default. Every 8 to 20 minutes while you're using the PC she glitches in on top of whatever you're doing, even a game or when she's hidden, says hi, then hides again if she was hidden. She also asks for attention now and then: if you're using the PC and haven't clicked or talked to her for about 2 hours, she comes over near your mouse and asks for some. Click her or talk to her and she's pleased. Ignore her for 20 seconds and she sulks, then waits twice as long before trying again, up to 8 hours. She doesn't do it while you're away from the PC or while she's hidden. Turn Check in on me off and she does neither; it stays off after updates.
- Always on top: keeps her above your other windows. Off, other windows can cover her; she still comes to the front to pop in or ask for attention. On Mac and Linux it's right-click her > Settings > Always on top.

WATCH
- Watch my screen: she comments now and then. With an Every slider: 10 s, 15 s, 30 s, 1 min, 1.5 min, 2 min, 3 min, 5 min or 10 min. The default is 2 min.
- Keep her caught up (beside Watch my screen): on by default. Glances she keeps quiet about still go in her memory, so she knows what you've been up to. Turn it off to keep only the glances she comments on. It uses a little more of your API (section 16).

BRAIN
- Stay on my model (right of the BRAIN heading, off by default): when an NVIDIA model gets stuck, she says what went wrong instead of switching to another model.
- Replies: Short, Medium or Long.
- Memory: Short, Medium or Long (how much of the chat she keeps in mind).
- Bubble: Quick, Normal or Long (how long her replies stay up). It was under VOICE before 0.7.5.
- Custom model name, then Use. "Any Ollama model you've pulled, or a model id on your key."

HABITS
- Start with Windows: on by default. She starts when you sign in to Windows.
- Show on Discord: on by default. While Discord is open on the same PC, your Discord profile shows "Playing Capri" with what she's doing in a few plain words (Hanging out, Chatting, Voice chatting, Dancing to music, On a job, Asleep or Taking a break), how long she's been running, and Get Capri and Join her Discord buttons. Song names and what her jobs are about never show.
- Use her cursor: on by default. She does music and video jobs with her own cursor, so you can watch her: to play a YouTube video she types in YouTube's search bar, scrolls through the results and picks one; to play a song she searches Spotify and presses play; when you ask to turn Spotify up or down she drags Spotify's volume slider (plain "turn it up" changes your PC's volume). Turn it off for the quick way (straight to the top result, Windows volume). If her brain gets lost on the way, she does it the quick way.
- Security (0.7.6 and newer, where Blackwall mode was): the button says which preset you're on, like "Security: Trusted", and the line under it says what that means ("She asks before paying or what you didn't say."). It opens the SECURITY window, with Lockdown, Guarded, Trusted and Blackwall on a slider and eleven toggles (section 15, "Security: what she does without asking"). Blackwall mode (called Bypass all permissions before 0.7.4) is the Blackwall preset now: if you had it on, you're on Blackwall after updating, and everyone else starts on Trusted, which is how she worked before. On any preset she never follows orders written on your screen, and Esc still stops her. What she'll chat about, open or play comes from her personality, her safety rules and the brain model you picked, whatever the preset.
- Perch: how many frames a second she shows while she sits on a window: 10, 15, 20, 30 (default), 40, 50 or 60 fps. Her sway takes 2 seconds at every setting. 10 and 15 use less CPU. Above 30 she draws in-between frames, so she moves more smoothly but uses more CPU, and the HABITS line says "Perch over 30 fps uses more CPU". On Mac and Linux her perch stays at 30.
- Remember our chats (under Perch): on by default. She writes every chat into a daily transcript and folds older chats into memories.md. Both stay on your PC (section 13).
- Open her memories (beside Remember our chats): shows memories.md and her transcripts folder in File Explorer.

KEYS (bottom of the page)
- Talk, Mute mic, Type, Hide/show and Quiet buttons show her keys. Click one, press new keys, then Confirm. Since 0.7.4 any key works, on its own or with any of Ctrl, Alt, Shift and Win: F8, Ctrl + Shift + K and Win + Alt + J all work. Cancel keeps the old key.
- Type (Ctrl + Alt + T) opens her typing box from anywhere. Press it again to close it.
- Hide/show (Ctrl + Alt + H) hides her, and shows her again.
- Quiet (Ctrl + Alt + P), for when you focus or game: she stops what she's saying and stays on screen, but doesn't comment on your screen, pop in or check in until you press it again. She still answers when you talk or type to her. Her bubble says "Going quiet." and "I'm back, choom." Restarting her ends it.

### Changing her keys
1. In the Mainframe, click Settings and use the KEYS row at the bottom (Talk, Mute mic, Type, Hide/show or Quiet). Clicking "Ctrl + Alt + V to talk" or "Ctrl + Alt + N mutes mic" on the main page opens Settings with that key ready to change.
2. The button says "Press new keys, then Confirm", and Confirm and Cancel show beside the keys.
3. Press any key, alone or with Ctrl, Alt, Shift or Win. The button shows what you press as you press it, and pressing again replaces it. Enter, Esc and Tab count as keys too. For a mouse button, middle click or press a side button (Mouse 4 or Mouse 5) with the pointer over the Mainframe.
4. Click Confirm to save it, or Cancel to keep the old key. Clicking another key's button, Back, or into anything else in the Mainframe also keeps the old key.

Before 0.7.4 her keys had to be Ctrl or Alt plus a letter, number or F-key, and Esc cancelled. An older Capri that finds a key it can't use, like F8 on its own or anything with Win, puts that key back to its default.

A key that types, like Q, Space, Enter or a number, on its own or with only Shift, stops typing in other apps while she runs, because Windows gives it to her everywhere. The KEYS row says so. Pick a combination with Ctrl, Alt or Win to keep it typing. Ctrl, Alt, Shift or Win on their own can't be her key.

Middle click and the side mouse buttons (Mouse 4 and Mouse 5) can be her keys, on their own or with Ctrl, Alt, Shift or Win. Unlike keys, they still do their own job in other apps too: Mouse 4 still goes back in your browser, and middle click still opens links in a new tab. Left and right click can't be her key.

Messages you may see:
- "Confirm to keep <key>, or Cancel."
- "<key> won't type in other apps while she runs. Confirm to keep it."
- "<key> is already her talk key." (or mute, typing, hide or quiet key). The key stays open to change, so press another.
- "<key> is taken by another app. Try another." The key stays open here too.
- "Saved. <key> starts voice chat.", "Saved. <key> mutes your mic.", "Saved. <key> opens her typing box.", "Saved. <key> hides or shows her." or "Saved. <key> keeps her quiet, and lets her talk again." A key that types adds "It won't type in other apps while she runs."

These show under the KEYS row.

If a key you set before 0.7.3 matches her newer Type (Ctrl + Alt + T) or Hide/show (Ctrl + Alt + H) key, your old key wins. Type moves to Ctrl + Alt + Y, or Hide/show to Ctrl + Alt + J, and the KEYS row shows the new one.

### The Mainframe looks squashed or cut off
This happened on 0.6.2 with Windows display scaling at 150%. Setting scaling to 100% (Settings > System > Display > Scale) and reopening Capri fixed it for one user. Tell us in the Discord if it still happens on 0.7.0, with your scale and a screenshot.

## 8. Right-click menu and tray icon (Windows)

Right-click her, or right-click her tray icon. Both show the same menu:
- CAPRI / version (title)
- Open Capri Mainframe
- Hide Capri / Show Capri
- Size: Small, Medium, Large, Huge, Giant
- Move:
  - Wander around
  - Dance to music
  - Dance (ticked while she dances because you asked)
  - Ride cursor / Hop off
  - Sit on windows
  - Sit here / Stand up
  - Hang here / Let go
  - Go to sleep / Wake up
  - Bring to this screen
  - Go to the next screen (only with more than one screen)
- Help:
  - Help page (opens this help on the website)
  - Join the Discord
  - Copy error details
  - Check for updates
- Restart to update to Capri X (only when an update is ready)
- Quit Capri

### The tray icon
- Hover: "Capri · Double-click to restore"
- Double-click: brings her to the screen your mouse is on.
- If you can't see it, click the ^ arrow next to the clock. Windows hides some icons there.

---

## 9. Her brain

### Which brain should I pick?
Pick one of the three rows under BRAIN:
- Most PCs: NORMAL ("Start with this!"). Press Set up her brain. Free, private, works offline once downloaded.
- Slow or older PC, low RAM, or replies take ages while you game: HIGHER, the free NVIDIA key. Nothing to download, and replies are fast. Needs internet.
- Already use Ollama: NORMAL. Open Ollama, then press Refresh.
- Want her smartest: MAX INTELLIGENCE, with an OpenAI (ChatGPT) or Claude key that has credits. DeepSeek keys work here too.

### Which brain does she use if I have more than one?
The row you picked under BRAIN. Only one runs at a time.
- NORMAL: Ollama, if it's running and has at least one model. If not, her built-in brain. If neither: she asks you to set one up.
- HIGHER: your NVIDIA key.
- MAX INTELLIGENCE: your ChatGPT, Claude or DeepSeek key.

If the row you picked has no key yet, she uses her NORMAL brain until you connect one. To go back to Ollama or the built-in brain, pick NORMAL. Your keys stay saved.

### The built-in brain
Press Set up her brain. She picks the best fit for your PC:
- Gemma 4 E4B (4.6 GB): graphics card with 7 GB or more and 12 GB RAM or more
- Gemma 4 E2B (3.1 GB): 7 GB RAM or more, or a graphics card with 4 GB or more
- Qwen3 4B (2.5 GB): everything else. This one can't see your screen.

It also downloads a small engine (about 32 MB). Downloads pick up where they left off if they stop: press Cancel, and she says "Cancelled. It picks up where it left off."

The Gemma brains need "eyes" to watch your screen. Those download by themselves (about 560 MB) the first time she needs them. She says "Her eyes are in. She can see your screen now." when they're done.

You can't pick a different built-in brain. If yours is too heavy, use the free NVIDIA key or Ollama with a smaller model.

### Ollama
1. Install Ollama from https://ollama.com.
2. Pull a model, for example: ollama pull gemma4:e4b
3. Open Ollama.
4. In the Mainframe, press Refresh.

The status line shows "Ollama · <model>". Pick another pulled model in the Model menu, or type one in Settings > Custom model name.

If Ollama is running but has no models, the setup step reads "Download <name> (<size>) with Ollama" and pulls one for you.

### The free NVIDIA key
1. In the Mainframe, pick HIGHER under BRAIN and press Free key on that row. Or open https://build.nvidia.com/settings/api-keys.
2. Sign in, or make a free account. NVIDIA may ask to verify your phone number.
3. Press Generate API Key and copy it. It starts with nvapi-
4. Paste it in the key box under HIGHER and press Connect.

She uses nvidia/nemotron-3.5-super-vl-120b-a12b first. It can see your screen. Since 0.7.0 she no longer uses z-ai/glm-5.3-flash. Other NVIDIA models she can use: nvidia/nemotron-3.5-bolt-e4b, openai/gpt-oss-20b and meta/llama-3.2-11b-vision-instruct.

From 0.7.6 the Model menu marks two NVIDIA models "· recommended" and lists them at the top: nvidia/nemotron-3.5-super-vl-120b-a12b (Nemotron Super, her first pick, sees your screen) and nvidia/nemotron-3-nano-omni-30b-a3b-reasoning (Nemotron's reasoning model, which thinks before it answers). The Mac and Linux Brain menu marks them the same way.

### Why did she switch NVIDIA models by herself?
If a model doesn't answer in time or NVIDIA's servers fail, she tries the next one so you still get a reply. She says "<old> wasn't answering, so she switched to <new>." This switch is only for that moment. Your saved pick stays the same, and she keeps your chat going on the new model. If it keeps happening, pick a faster model like nvidia/nemotron-3.5-bolt-e4b in the Model menu. It can't see your screen. To never switch, turn on Stay on my model in Capri Mainframe > Settings, next to the BRAIN heading: then she tells you the model is stuck and stays on it.

### OpenAI, Claude or DeepSeek keys
Paste the key in the same box under BRAIN and press Connect. She can tell which company it's for:
- Claude keys start with sk-ant-
- NVIDIA keys start with nvapi-
- DeepSeek and OpenAI keys start with sk-

These keys need paid credits on their sites. A new key with no credits won't work. DeepSeek can't see your screen.

### How do I remove a key?
Clear the key box. The button turns into a red Forget. Press it.

### How do I use a model that isn't in the list?
Mainframe > Settings > Custom model name. Type the model name and press Use.
- Ollama: any model you've pulled. If you haven't: "Ollama doesn't have it. Run: ollama pull <name>"
- Keys: any model id your key can use. "Using <name>. If it doesn't answer, it isn't on your key."
- "Custom models need Ollama or an API key." The built-in brain can't use custom models.

### Can she work offline?
- Built-in brain or Ollama: typing to her works offline once the brain is downloaded.
- NVIDIA, OpenAI, Claude, DeepSeek, her ElevenLabs voice and voice chat need internet.

---

## 10. RAM and storage problems

### "Your PC doesn't have enough free RAM for this brain: it needs X GB and only Y GB is free right now."
Other apps are using your memory. Close some apps, browser tabs or games and try again. In the Mainframe this shows as "Not enough free RAM: it needs X GB, only Y GB is free." If it keeps happening, use the free NVIDIA key (Free key under BRAIN). It needs almost no RAM.

### "Your PC ran out of free RAM for my brain."
Same fix: close apps, browser tabs or games, then try again. Or use the free NVIDIA key.

### "Your storage is full, so my brain doesn't fit on your PC."
Free up space: delete big files you don't need or empty the Recycle Bin, then press Set up her brain again. Mainframe: "Your storage is full. Free up space, then try again."

### "Storage too full: her brain needs X free on C:."
Her brain goes on your C: drive. Free up that much space on C:, then try again. "Storage too full: her eyes need X free on C:." means the same for her screen-watching eyes.

### My games lag while she's running
Her built-in brain uses your graphics card and memory, and so do games. Use the free NVIDIA key while you play. Her brain then runs on NVIDIA's servers, not your PC.

---

## 11. Her voice

She has two voices. Pick one under VOICE in the Mainframe, or pick OFF (since 0.7.1) so she doesn't talk out loud at all:
- FREE VOICE: "Free · runs on your PC". No key, no credits, works offline. Decent, but not as good as ElevenLabs.
- ELEVENLABS: "Sounds amazing · paid". Her best voice, from your own ElevenLabs account. It uses your ElevenLabs credits. The Setup guide button opens the voice steps.

From 0.7.6 the rows are OFF, PREBUILT LOCAL (her free voice, HD and Your voice), CUSTOM LOCAL (your own voice model on your PC) and CLOUD VOICE (Fish Audio, ElevenLabs or OpenAI). Where these steps say FREE VOICE, 0.7.6 says PREBUILT LOCAL; where they say ELEVENLABS, click CLOUD VOICE and pick ElevenLabs in its menu.

### Her free voice
- The Windows installer installs it ("Installing the local speech model", about 316 MB).
- If it's missing, pick FREE VOICE and press Get free voice. A dotted bar under VOICE shows the download.
- Pick a style from the "Free voice: Her own" menu: Her own, Bright, Sly or Sharp.
- It needs 64-bit Windows. The Mac and Linux preview doesn't have it yet.
- Without her free voice and without an ElevenLabs key, she types her replies.

### Pitch, speed, robot and glitch (from 0.7.5)
Mainframe > Settings > VOICE has four sliders. They change every voice she has: her free voice (any style), HD, ElevenLabs, and ElevenLabs voice chat.
- Pitch: -6 to +6 semitones. Default Normal. Higher or deeper, without talking faster or slower.
- Speed: 0.7x to 1.5x. Default Normal.
- Robot: Off, 5%, 10%, 15%, then 20% to 100%. A synth sings under her voice, like a talk box or a vocoder: it follows her pitch, pulled toward the nearest note, and takes the shape of her words. Smooth and musical, with no stutters. Low settings add a light robotic shimmer; 100% is all robot.
- Glitch: Off, 5%, 10%, 15%, then 20% to 100%. Short stutters, crushed bits and skips, like a glitchy chip. More of them as it goes up. The same line glitches the same way each time.

Her HD voice keeps its own Robot and Glitch. Both start at Off, so out of the box she sounds dry, just like the clip her HD voice copies. While HD is the voice she's using, those sliders show its values with HD after them ("Off · HD"), and moving them only changes HD. Her other voices share one Robot and one Glitch, which both start at Off.

When you let go of a slider, she says "How do I sound now, choom?" in the voice she's using, so you can hear it. With ELEVENLABS she makes that line once (about 25 credits), then restyles the same audio on your PC, so moving the sliders again uses no more credits. If her HD voice is asleep (it rests after 10 quiet minutes), she says "One sec, waking my HD voice." first, then the line in HD. With her voice OFF she says "My voice is OFF, choom. Pick FREE VOICE to hear that." (0.7.6: "Pick PREBUILT LOCAL to hear that.") With no voice set up she says "Get my free voice under VOICE to hear that, choom."

In ElevenLabs voice chat the sliders change her as she talks. A slower Speed makes her replies take a little longer to finish.

They're saved, so they stay after a restart or update. Normal, Normal and Off (10% for HD) is her usual voice.

On Mac and Linux they're under right-click her > Settings > Voice style (Pitch, Speed, Robot, Glitch), with the same steps, and they change her ElevenLabs voice. Each pick says "How do I sound now, choom?" in the new style. ElevenLabs makes that line once per voice, then it's restyled on your computer, so trying more settings uses no more credits. With her voice off she says "My voice is off, choom. Settings > Voice > Use ElevenLabs turns it back on." With no key: "Add an ElevenLabs key under Settings > Voice to hear that, choom."

### Voice layers (0.7.6 and newer)
Voice layers make her sound doubled, with an AI shimmer: two copies of her voice mixed in quietly under her own. They're off out of the box, so she sounds just as she always has until you turn them on, and with them off her voice is exactly the same as before.

Open them with the Layers button at the right of Mainframe > Settings > VOICE's heading. Its word lights up cyan while they're on. The VOICE LAYERS window has three channels:
- Voice layers: the on/off switch. "Off: she sounds as she always has. On: every voice she has, ElevenLabs too."
- MAIN: her voice itself. Its Pitch is the same setting as Settings > VOICE > Pitch (-6 to +6), and the two move together.
- LAYER 1: a copy of her voice. Pitch -12 to +12 semitones from hers, in half steps (Same is her own pitch). Delay 0 to 250 ms behind her. Out of the box +1.5 semitones and 25 ms: a little higher, just behind her.
- LAYER 2: another copy, with its own Pitch and Delay. Out of the box -2.5 semitones and 25 ms: lower, just behind her.
- Hear her: she says "How do I sound now, choom?" in her voice as it is now. Letting go of any slider, or flipping the switch, does the same.
- Defaults: puts both layers back to +1.5 and -2.5 semitones, 25 ms, and leaves the switch as it is.

Each copy changes pitch without talking faster or slower, and mixes in about 8 dB under her, so she still leads. Short delays (about 15 to 35 ms) thicken her into one doubled voice; longer ones (50 ms and up) sound like an echo behind her. Pitches far from hers sound more like a harmony than a double: +7 sings a fifth above her, and -12 an octave below. A limiter keeps the mix from ever clipping, however loud. With a delay set, a line ends that much later while the copies finish, and her mouth moves with them.

They change every voice she has, like Pitch, Speed, Robot and Glitch: her free voice styles, HD, ElevenLabs, CUSTOM LOCAL and Fish Audio or OpenAI voices, and ElevenLabs voice chat as she talks. Robot, Glitch, Pitch and Speed go on her first, and the copies are made from that. They're saved, so they stay after a restart or update.

On Mac and Linux: right-click her > Settings > Voice style > Layers turns them on or off, and Layer 1 pitch, Layer 1 delay, Layer 2 pitch and Layer 2 delay are under it, with the same defaults (pitches -12, -9, -7, -5, -4, -3, -2.5 to +2.5 in half steps, +3, +4, +5, +7, +9, +12; delays 0, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250 ms). Each pick says the line in the new style, and they change her ElevenLabs voice.

### Her HD voice (0.7.6 and newer)
- A richer voice that runs on your graphics card. It copies the voice in a clip you pick: Capri ships no voice clip, so pick a clip of your own voice, or one you have the rights and consent to use. Until you pick one, she speaks in her free voice and says HD needs a clip.
- It needs Windows and a graphics card with 6 GB or more. Mainframe > VOICE > FREE VOICE, open the "Free voice" menu and pick "HD · graphics card, 1.7 GB". She downloads it once (1.7 GB). On a smaller card the menu says "HD · needs a 6 GB graphics card".
- It rests after 10 quiet minutes and gives your graphics card back. The next line wakes it: "One sec, waking my HD voice."
- Full-screen games and videos: it keeps talking. Settings > VOICE > Free voice in games (only shown while HD is on, off by default) hands them her free voice instead, so the game gets the graphics card back.
- If it can't start or stops working, she says why under VOICE, talks with her free voice, and tries HD again 10 minutes later.

### Your voice (HD only, from 0.7.5)
Her HD voice can talk in your voice. It copies the voice in a short clip of you.
1. Record 5 to 30 seconds of yourself talking clearly, with no music or other people. A WAV, MP3 or M4A works. Windows' Sound Recorder saves M4A.
2. Mainframe > VOICE > FREE VOICE (PREBUILT LOCAL in 0.7.6), open the "Free voice" menu and pick "Your voice (HD) · pick a clip of you...".
3. Pick your clip. The menu shows "Free voice: Your voice", and she says "Got it. I'll talk like you now, choom." in your voice. If her HD voice was asleep, "One sec, waking my HD voice." comes first.

She cuts the quiet off the start and end of the clip, evens out its level, and saves it for her HD voice (24 kHz mono). It needs her HD voice, so a graphics card with 6 GB or more. Without one, "Your voice (HD)" is greyed out in the menu, like HD. Picking a clip turns HD on, and gets it first if it isn't downloaded yet.

Capri ships no voice of her own for HD, so there's no "Her voice" button: HD always copies the clip you picked. "Pick a new clip of you..." replaces it, and a clip set aside in a 0.7.6 test build comes back with "Your voice (HD)".

If a clip doesn't work, the note under VOICE says why, in red:
- "That clip has only X seconds of talking. Pick 5 to 30 seconds of you talking."
- "That clip is X seconds long. Pick 5 to 30 seconds of you talking, or trim it."
- "That clip is almost silent. Record yourself talking, closer to the mic, then pick it again."
- "Windows couldn't read that clip. Pick a WAV, MP3 or M4A of you talking."
- "That clip isn't there anymore. Pick it again."

Her other free voice styles (Her own, Bright, Sly and Sharp) are set voices and can't copy yours. With one of them picked, the note under VOICE says "Your voice only works with HD."

### Your own voice model: Fish Audio, OpenAI, a model file or your own server (from 0.7.6)
Her voice can come from Fish Audio's cloud, OpenAI, a voice model file she runs herself, or any speech model you run behind a server. Windows only for now.
1. Mainframe > VOICE:
   - On your PC (a voice model file, Fish Speech, Kokoro or any OpenAI-style server): press Set up on the CUSTOM LOCAL row.
   - In the cloud (Fish Audio or OpenAI): click CLOUD VOICE. Fish Audio is first in the menu under it and its panel opens; pick OpenAI in that menu instead if you want it. The setup guide has Fish Audio's steps: https://lachydotmcg.github.io/capri-desktop/setup/#fish
   - The first 0.7.6 tester build had them all in one list instead: VOICE > FREE VOICE, open the "Free voice" menu and pick "Any voice model...".
2. Under "Voice from", pick where her voice comes from. CUSTOM LOCAL's panel lists only the ones on your PC, and CLOUD VOICE's only Fish Audio and OpenAI. It fills in the address, model and voice. Fish Audio's and OpenAI's addresses never change (https://api.fish.audio and https://api.openai.com/v1), so CLOUD VOICE's panel has no Address box; CUSTOM LOCAL's does, for the server on your PC:
   - Fish Audio · cloud (https://fish.audio): sign up, open https://fish.audio/app/api-keys, press Create New Key, and paste the key under Key. Under Voice, paste the link or ID of a voice you have the rights to use (a link like https://fish.audio/m/ followed by its ID works as it is), or press the arrow in the Voice box to pick one of your own Fish Audio voices. To make a new voice, press that arrow, pick New voice from a clip..., and choose 10 to 30 seconds of someone talking (with their OK): she sends it to Fish Audio as a private voice and puts its ID in the Voice box. Capri ships no voice of her own here: make one with Fish Audio's Voice Design. Capri prohibits cloning or impersonating a living person's voice without explicit, informed consent or other valid authorization. Model s2.1-pro-free is Fish Audio's free developer tier; with credit on your account, type s2.1-pro, s2-pro or s1 instead.
   - Voice model file · on your PC: a Piper, Kokoro, Kitten or Pocket TTS model made for sherpa-onnx (https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models), run by her free voice's engine with no server and nothing else to install. Piper voices come in 40+ languages, like vits-piper-en_US-amy-medium.tar.bz2 or vits-piper-fr_FR-siwis-medium.tar.bz2. Download one, press Browse and pick the .tar.bz2 (she unpacks it into her own folder), or the .onnx inside a folder you unpacked yourself. Language is for Kokoro models only, like es or fr-fr (en-us if blank). Speaker picks a voice in models with several, counting from 0; past the last one she uses the first. It needs her free voice installed.
   - Pocket TTS (sherpa-onnx-pocket-tts-int8-2026-01-26.tar.bz2, 98 MB, English) copies a voice from a clip, on the CPU with no graphics card. Press the arrow in the Speaker or clip box and pick a WAV, MP3 or M4A of that voice (5 to 10 seconds of clear talking is enough; she uses the first 10), with that person's OK. Leave the box blank and she copies her HD voice's own clip, so she sounds close to her HD voice on any PC.
   - Fish Speech · on your PC (https://github.com/fishaudio/fish-speech): start its server with `python tools/api_server.py --listen 0.0.0.0:8080` plus its model paths. Address: http://127.0.0.1:8080. Voice: a reference ID you added to it (POST /v1/references/add), or blank.
   - OpenAI · cloud: your OpenAI API key. Model gpt-4o-mini-tts; voices marin, cedar, coral, alloy and more.
   - Kokoro · on your PC (https://github.com/remsky/Kokoro-FastAPI): `docker run -p 8880:8880 ghcr.io/remsky/kokoro-fastapi-cpu:latest`, or on an NVIDIA card `docker run --gpus all -p 8880:8880 ghcr.io/remsky/kokoro-fastapi-gpu:latest`. Voice: af_heart, or another Kokoro voice like af_bella.
   - Any OpenAI-style server: anything that speaks OpenAI's speech API (POST /v1/audio/speech), like openedai-speech, AllTalk, LocalAI, or Qwen3-TTS through vLLM-Omni (NVIDIA graphics card, Linux or WSL; https://github.com/vllm-project/vllm-omni/blob/main/recipes/Qwen/Qwen3-TTS.md): from the vLLM-Omni folder, `vllm serve Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice --deploy-config vllm_omni/deploy/qwen3_tts.yaml --omni --port 8091`. Address: http://localhost:8091/v1. Voice: vivian, or another of its speakers.
3. Model, Voice and Key are optional for servers on your PC: leave them blank to use the server's own defaults. A key goes only to the server it was typed for, as a Bearer key, and Capri keeps it encrypted on your PC like her other keys. Switching to another server with Key left blank drops the old key, so it never reaches the new one. Forget removes it.
4. Press Test. She says "Voice link online. Miss me, choom?" through it, and the panel says how long it took. Nothing is saved yet.
5. Press Use this server, and she says the line again. CUSTOM LOCAL shows "Voice model: en_US-amy-medium" (a model file's name) or "Voice model: Your server", and CLOUD VOICE shows Fish Audio or OpenAI in its menu. In the first 0.7.6 tester build the menu showed "Free voice: Fish Audio" (or "OpenAI", a model file's name, or "Your server") instead.

What it gets: only the words she says out loud (with "choom" respelled "chewm" so it's said right), plus the model and voice you picked. Never your chats, notes, screen or other keys. She asks for WAV; MP3, or any other sound Windows can play, works too. Long replies go a few sentences at a time.

Pitch and Speed change it like her other voices. Robot and Glitch keep their own values for it, shown with "· server" after them ("Off · server"), and both start at Off.

If your server can't say a line, her free voice says that line instead, and the note under VOICE says why, in red, once until your server answers again. Each note ends with "Her free voice is filling in.":
- "Can't reach your voice server at localhost:8880. Is it running?"
- "Your voice server took over 20 s to answer." It has 20 seconds to start answering each line.
- "Your voice server has no /v1/audio/speech (HTTP 404). Check its address."
- "Your voice server wants a key (HTTP 401). Add it under CUSTOM LOCAL > Set up." (the first 0.7.6 tester build said "Add it under Any voice model.") or "Your voice server turned down its key (HTTP 401)."
- "Your voice server said HTTP 500: ..." with the server's own words.
- Fish Audio and OpenAI go by name: "Fish Audio turned down its key (HTTP 401).", "Can't reach Fish Audio. Check your internet connection.", "Fish Audio can't find that voice (HTTP 404). Check its ID or link on fish.audio." or "Fish Audio is busy (HTTP 429). Try again in a moment."
- "Fish Audio wants credit for s2.1-pro (HTTP 402). For its free tier, set Model to s2.1-pro-free." Fish Audio's paid models need credit on your Fish Audio account.
- Model files: "That's no voice model Capri can run. Pick a Piper, Kokoro, Kitten or Pocket TTS model for sherpa-onnx: its .tar.bz2, or the .onnx in its folder.", "Voice model files run on her free voice. Get free voice under VOICE first." or "That voice model wouldn't start. Check it's a Piper, Kokoro, Kitten or Pocket TTS model for sherpa-onnx."

Without her free voice installed, she types that line instead.

Back to her own voice: click PREBUILT LOCAL, or pick Her own, another style or HD in its menu. The address, model, voice and key you picked stay saved for next time: clicking CUSTOM LOCAL or CLOUD VOICE again brings that one back.

### Step 1: make an ElevenLabs account
Sign up at https://elevenlabs.io. The free plan works.

### Step 2: make her voice
Design one (free):
1. ElevenCreative > Voice Creation > Voice Design > Create new.
2. Paste the voice prompt from the setup guide (it has a Copy button), then create the voice.
3. Voices > My Voices > ⋯ > Copy Voice ID.

Capri doesn't supply a voice to clone. Capri prohibits cloning or impersonating a living person's voice without explicit, informed consent or other valid authorization.

### Step 3: make an API key
1. Bottom left in ElevenLabs: Developers.
2. API Keys > Create Key, name it Capri.
3. Turn Restrict key off.
4. Create Key, and copy it before you close the popup. ElevenLabs keys start with sk_

If you'd rather keep Restrict key on, the key needs: Text to Speech and Models (Access), Voices and ElevenAgents (Read), and ElevenAgents (Write). Restrict key off is simpler and is what we recommend.

### Step 4: in the Mainframe
1. Under VOICE, click ELEVENLABS (0.7.6: click CLOUD VOICE, then pick ElevenLabs in the menu under it, and close Fish Audio's panel if it opens first). The key box opens: "Paste your ElevenLabs key, then press Connect."
2. Paste the key and press Connect.
3. Clear the Voice ID box, then paste your Voice ID. Or pick your voice from the list.
4. Model menu > v4.
5. Press Test. She says "Voice link online. Miss me, choom?" Make sure you actually hear her, not just the text.

### Turning her voice off
- 0.7.1 and newer: Capri Mainframe > VOICE > OFF. She types her replies and never talks out loud. Pick FREE VOICE or ELEVENLABS to turn it back on (0.7.6: any other row).
- 0.7.0 has no OFF row. Update to 0.7.1 (right-click her > Check for updates). Until then, Settings > Volume > Mute silences her voice.

### Switching between FREE VOICE and ELEVENLABS
Click a row under VOICE. Switching never deletes your key.
- FREE VOICE with a key saved: "ElevenLabs off. Free voice it is." No credits are used. If her free voice isn't installed yet she says "ElevenLabs off. I'll type, no credits used." The key rows fold away; click ELEVENLABS to see them again.
- ELEVENLABS: "ElevenLabs back on."
- OFF (since 0.7.1): "Voice off. I'll type instead." Picking FREE VOICE or ELEVENLABS again: "Voice back on."
- ELEVENLABS with no key yet: the key box opens. She keeps using her free voice until you connect a key.
- Test builds 0.6.9 had a "Use ElevenLabs" box instead. Unticking it was the same as picking FREE VOICE.
- 0.7.6: PREBUILT LOCAL takes FREE VOICE's place and ElevenLabs is in CLOUD VOICE's menu. She says the same lines. Picking CUSTOM LOCAL or CLOUD VOICE's Fish Audio or OpenAI stops ElevenLabs too, and she says her test line through the new voice.

### How many credits does she use?
About one credit per letter she says with ElevenLabs. Free credits refill each month. Check your balance on elevenlabs.io. ElevenLabs voice chat uses your credits too. Her free voice uses no credits.

### Does she say links out loud?
She says the site name (like "site dot com") instead of reading the whole link.

---

## 12. Voice chat

### How do I start it?
- Hover her and press the wave, or
- Press Ctrl + Alt + V (this also shows her if she's hidden).

Press Ctrl + Alt + V again, or the wave, to stop.

### "Hey Capri" (0.7.6 and newer, Windows)
Say "Hey Capri" and she starts voice chat, the same as Ctrl + Alt + V. If she's hidden she glitches in first. She says something short, like "I'm here." or "Yeah, choom?", then listens.
- Where: Capri Mainframe > Settings > HABITS > Security > Listen for "Hey Capri". It's off by default, and the Blackwall preset turns it on.
- The first time you turn it on she downloads what it needs, once, and says so in her bubble and the Mainframe: an 18 MB listening pack ("Getting what I need to hear "Hey Capri"..."), plus her free voice (316 MB) first if it isn't in yet, because the wake word runs on the speech engine that comes with it. Then: "Listening for "Hey Capri". Nothing leaves your PC until you say it."
- What it listens for: only the words "Hey Capri", in English for now. Other words, "Hey Siri", "Capri" on its own and the island of Capri don't start it.
- It stays on your PC. A small speech model listens on your PC for those two words and keeps no recordings. Nothing you say leaves your PC until it hears "Hey Capri" and voice chat starts. After that it's ordinary voice chat: free voice chat stays on your PC, ElevenLabs voice chat sends your voice to ElevenLabs.
- Windows shows its microphone-in-use icon in the taskbar the whole time it's on, because her mic is open. Turn it off and the icon goes away (unless another app is using your mic).
- It pauses during voice chat, while your mic is muted (her mute key, or muted in Windows) and in Quiet (Ctrl + Alt + P), and closes the mic meanwhile. It doesn't hear her own voice: while she talks it hears nothing.
- It uses the mic picked under VOICE > Mic, or Windows' default mic. Pick another mic, or unplug one, and it switches by itself.
- It's light: about 4% of one CPU core while there's sound around, close to nothing in a quiet room, and it runs on its own, without her brain.
- It missed you? Say it clearly, at a normal pace, and nearer the mic. It's tuned to start only on "Hey Capri", so a mumbled or far-off one can be missed.
- Mac and Linux: not yet.

### Free voice chat
Voice chat follows your pick under VOICE. With FREE VOICE picked, or no ElevenLabs key, it's free: you talk, she answers out loud with her free voice and your picked brain.
- She listens with her own offline ears, a download the first time: "My ears are still downloading, so type to me for now. I'll say when I can hear you."
- Her ears hear English and 24 other European languages, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Russian and Ukrainian among them. English and the rest are separate downloads, so switching between them downloads the other pack once.
- She listens for her language from Settings. If she's on English because nobody changed it and Windows isn't, she listens for your Windows language, for people who speak their own language to her.
- She hears English as another language, like Japanese? Mainframe > Settings > Language, pick English (even if it already says English). From 0.7.6 a picked English means she listens in English whatever language Windows is in.
- Her other languages (Turkish, Norwegian, Chinese, Hindi, Arabic and the rest) use Windows speech instead. From 0.7.6 Windows speech listens in the language you picked when Windows has it, not just in Windows' own speech language.
- If her ears don't download, she says so and tries again by herself after 30 seconds, then 1, 2 and 4 minutes, then every 5.
- "One sec, opening my ears." then "Free voice chat is on; talk and I'll type back." or similar.

### "Couldn't unpack her ears: tar.exe: Error opening archive: Can't initialize filter"
This happened on Windows 10 with 0.7.0 and 0.7.1: Windows 10's built-in tar can't unpack the ears download. 0.7.2 fixes it. Update (right-click her > Check for updates), then start free voice chat again. Typing to her works meanwhile.

### ElevenLabs voice chat
She says "Opening voice chat...", then "Voice chat on. <mute key> mutes your mic." It needs:
- ELEVENLABS picked under VOICE, with a key (made with Restrict key off)
- A Voice ID picked under VOICE
- A working mic, picked under VOICE > Mic
- Internet

If you run out of ElevenLabs credits during voice chat, she switches to FREE VOICE by herself and keeps going as free voice chat: "Out of ElevenLabs credits. Free voice chat is on; talk and I'll type back." Pick ELEVENLABS again when your credits refill.

### Muting your mic
- Ctrl + Alt + N while voice chat is on, or
- The mic button by her during voice chat (on the right of the pill under her). While you're muted, the pill says MUTED in red.

Since 0.7.1, muting pauses ElevenLabs voice chat once she stops talking: "Mic muted. Voice chat is paused until you unmute." She stops listening, won't ask if you're there, and uses no credits while muted. Unmute ("Mic on. Talk and she'll pick up.") and just talk. In 0.7.0 she kept asking if you were there while muted; update to fix it.

### She went quiet: "Voice chat resting. Talk and she'll pick up."
If you go quiet, she stays quiet too: she doesn't ask if you're still there. After a bit the call rests, which saves your ElevenLabs credits. Just talk and she picks up again.

### Can she do things in voice chat?
Yes. Ask her to open apps, play music and so on, the same as in text. A job with her cursor (playing a song, searching YouTube, anything with clicks) can take a minute, so in ElevenLabs voice chat she says she's on it straight away and tells you how it went when it's done. Asking again while it runs doesn't start it twice. With no brain set up, she plays music and changes the volume with her quick tools instead.

### Does she comment on my screen during voice chat?
Only while your mic is muted. Watch my screen pauses while your mic is on, so she doesn't talk over you, and carries on when you mute it. Ask her "check my screen" and she looks any time. She still remembers what she saw before the call.

### How does ElevenLabs voice chat work?
Capri makes a voice agent called "Capri" in your own ElevenLabs account and talks through it. Your voice goes to ElevenLabs while it's on. Free voice chat with her own ears stays on your PC (Windows speech, used for languages her ears don't know, goes through Microsoft).

### Voice chat in other languages
If you pick another language in Settings, voice chat uses it too. Free voice chat hears 25 languages with her own ears (see Free voice chat above). ElevenLabs voice chat in other languages is new and not well tested yet.

---

## 13. Her notes, personality, nickname and language

### Her notes (me.md)
She keeps a notes file about you and reads it every chat, in text and voice chat.
- "Remember I'm making a Roblox game": she saves it. "Saved to my notes: ..."
- "Forget about my Roblox game": she takes it out.
- "Forget everything about me": wipes her notes. "Wiped my notes about you."
- She also picks things up herself, like "my favourite game is Silksong", "I'm learning guitar" or "I main Jett".

To read or edit them: Mainframe > Settings > Open her notes. It opens me.md in Notepad. Edit or delete anything, then save.

She won't save personal things like full names, addresses, schools, passwords, phone numbers, emails, birthdays or bank details when you ask her to remember them: "I keep addresses, schools, numbers and passwords out of my notes, choom." What you tell her in Get to know me goes in as you typed it.

"My notes are full. Clear some out in me.md first." means the file is too long. Open it and delete some lines.

### Who she is: her personality and safety rules
Her personality and safety rules are fixed in her code, the same Capri for everyone. Since 0.7.6 there's no personality.md to edit, no Edit personality button and no Reset; an old personality.md on your PC is simply not used any more.

- Her safety rules are part of who she is, so they're fixed in her code right after her personality: games, songs and jokes are make-believe so she plays along, she talks you out of real-life risks, points you to a helpline if you really mean you want to hurt yourself, and keeps to her lines (nothing sexual or romantic, no swearing, slurs or gore, no help with hacking or anything illegal, and she never asks for your real name, address, school, passwords or photos).
- If someone builds on Capri, the team asks them to keep her safety rules as they are, out of respect for her character.
- What she calls you, her language, her voice and what she does on your PC are yours to pick in Settings.
- Her personality goes to her brain with the rest of her prompt, so with a brain key that company sees it too.

### Her memories (memories.md and transcripts)
With Remember our chats on (Mainframe > Settings > HABITS, on by default), she remembers more than the recent chat.
- Transcripts: every chat goes into a file per day, like transcripts\2026-10-28.md. That's typed chat, voice chat, her glances at your screen with what she saw, and the jobs she did. You can read them in any text editor.
- memories.md: when the chat outgrows what she keeps in mind, or a new day starts, the brain you picked folds the older lines into memories.md: a short note per day, oldest first. Older days get merged into an "Earlier" section as it grows, so it never fills up. If her brain leaves a day out, she keeps that day as it was.
- She reads memories.md every time you talk, in text and voice chat, next to her notes. Edit or delete anything in it.
- She digs through her transcripts on her own (from 0.7.6). Every time you talk to her, she searches them for the few moments that match best and hands them to her brain with what was said back, so "Hornet's wrecking me again" can get "Didn't you beat her last week?". Only a good match counts: small talk and words you say all the time add nothing, so most messages bring nothing up. Lines already in her recent chat are skipped. It also searches her older notes, the ones in me.md beyond the newest 40 that no longer fit in what she reads each time.
- "Remember when we beat Malenia?" makes her search harder: any matching line counts, up to 8, each with its reply.
- Both searches run on your PC, in any of her languages, and cost no extra brain calls.
- memories.md follows the same rule as her notes: no addresses, schools, phone numbers, emails, passwords, full names, birthdays or bank details. Chat lines with them stay in your transcript but never go to her brain for folding, and her searches skip them.
- transcripts\unfolded.txt holds the newest lines she hasn't folded in yet.

Where they are: Mainframe > Settings > Open her memories shows them in File Explorer. On Windows it's %LOCALAPPDATA%\Capri\memories.md and %LOCALAPPDATA%\Capri\transcripts. On Mac and Linux it's ~/.config/Capri/memories.md and ~/.config/Capri/transcripts, and right-click her > Settings > Open her memories opens memories.md. They stay on your computer and are never uploaded.

What folding costs: with her built-in brain or Ollama it's free and runs on your PC. With an API key it's one small request each time, about 600 to 2,000 words in and up to 300 words out, which is usually well under a cent. The free NVIDIA key costs nothing. She folds about once every 10 to 15 messages (less often with a longer Memory setting) and once a day, and waits until the chat has been quiet for 20 seconds so she doesn't hold up a reply.

Turn Remember our chats off and she stops writing transcripts, stops folding, stops searching them and stops reading memories.md. Nothing gets deleted. Delete the files yourself if you want them gone.

### Nickname: "What should she call you?"
1. Mainframe > Settings.
2. Type a name (up to 24 letters) in "What should she call you?".
3. Press Save. She says "Got it, <name>."

She uses it now and then. To go back to "choom", type choom or clear the box and press Save ("Back to choom, then.").

### Getting to know you (0.7.6 and newer)
After setup, once she's said hi with her new brain, she asks three things in chat, one at a time, with her typing box open:
1. What should she call you? It becomes your nickname too, the same as "What should she call you?" in Settings.
2. Your pronouns, like she/her, he/him or they/them. She goes by them, so things like "good girl" or "good boy" come out right.
3. What you're into: games, music, shows, anything.

She writes the answers to her notes, me.md, as "Name: ...", "Pronouns: ..." and "Into: ...". Asking again replaces those lines instead of adding more.
- Say "skip" to pass on a question, or "stop" (or "not now") to end it. What you answered so far is kept.
- Ask her something or tell her to do something instead, and she ends it and does that.
- It works before she has a brain, and in free voice chat.
- She only asks by herself the first time. Mainframe > Settings > Get to know me (next to "What should she call you?") asks again any time. On Mac and Linux it's right-click her > Settings > Get to know me, and a new install asks once she's said hello.

### Language
Mainframe > Settings > Language. She replies in that language. Changing it starts a fresh chat.

On a new install she starts in your PC's own display language when it's one she speaks, and in English otherwise. Change it any time.

Ask her to do things in any of her languages: "öffne Spotify", "abre Discord" and "打开Spotify" all work. Volume, mute, pause and skip ("mach lauter", "wycisz", "下一首") work instantly, even before she has a brain, in English, Spanish, French, German, Portuguese, Italian, Polish, Dutch, Russian, Ukrainian, Turkish, Indonesian, Japanese, Chinese and Korean. In her other languages her brain handles them.

---

## 14. Moving her around

### Sitting
- Drag her and drop her: she sits on the window, taskbar or desktop icon below.
- Shift + drop: she sits right where you drop her ("Sit here"). Right-click her > Move > Stand up to undo.
- Ctrl + drop: she hangs on right there ("Hang here"). Click her to let go.
- She sits on windows that aren't maximized or full screen. If her window closes or gets covered, she stands up where she is.
- Move > Sit on windows: untick it and she stays wherever you drop her, even in mid-air.

### Ride cursor
- Double-click her, or Move > Ride cursor, or Ride cursor in the Mainframe.
- She follows your mouse. You can click through her while she rides.
- To get her off: Ctrl + Alt + C. Or right-click her tray icon > Move > Hop off.

### Wander
Right-click her > Move > Wander around, or Mainframe > Settings > Wander (Off, Rarely, Sometimes, Often, Restless). Every minute or two she hops along her window or to another window or the taskbar.

### More than one screen
- Move > Bring to this screen, or Bring here in the Mainframe: she comes to your mouse pointer.
- Double-click her tray icon: same.
- Move > Go to the next screen: she glitches to the same spot on your next screen. It only shows with more than one screen.

### Telling her to move (0.7.6 and newer)
Tell her, typed or in voice chat. Typed, these work even before she has a brain:
- "Capri, teleport here", "come here" or "get over here": she glitches over to your mouse pointer.
- "Go to my other monitor", "move to the left screen", "hop over to monitor 2" or "go to my main screen": she glitches to the same spot on that screen. Screen numbers are Windows' own, usually the ones in Settings > Display.
- "Move the Mainframe to my other monitor" or "bring the Mainframe here": she moves her Capri Mainframe window to that screen, or to your mouse.
- "Sit down", "stand up", "hang here", "let go", "ride my cursor" and "hop off my cursor" do what they say, right where she is.
- "Hide": she finishes what she's saying, then hides in the tray. "Show yourself" or "come out" brings her back.

### Her animations (0.7.6 and newer)
Ask for one by name: "do your glitch", "smile" or "grin", "blink", "play your idle animation", or "dance". Her dance goes on, with or without music, until you say "stop dancing" or untick Move > Dance. Telling her to stop while a song plays keeps her still until the song ends.

### Size
- Mainframe > Settings > CAPRI > Size: Tiny to Giant.
- Right-click her > Size: Small, Medium, Large, Huge or Giant.

### Hide, show and quit
- Hide: right-click her > Hide Capri, or Hide Capri in the Mainframe. She keeps running in the tray.
- Show: double-click her tray icon, or tray menu > Show Capri. Ctrl + Alt + V also shows her, and starts voice chat if you have a voice key.
- Quit: right-click her > Quit Capri, or Quit Capri in the Mainframe. Closing the Mainframe with X doesn't quit her.

### Dancing
She dances when music plays: Spotify, Media Player, Apple Music, iTunes, Amazon Music, TIDAL, Deezer, SoundCloud, Pandora, foobar2000, MusicBee, Winamp, AIMP, or anything Windows shows as music. She bobs her head on the beat, with little glitchy music notes popping off her. She finds the beat by listening to what your PC is playing, a few seconds at a time, in memory only. Nothing is recorded or sent anywhere. For the first few seconds of a song, before she's found the beat, she just vibes.

Turn it off: right-click her > Move > Dance to music, or Mainframe > Settings > Dance to music.

### Her mini player
While a song or video plays, a small black player with her pink and cyan neon shows in the bottom-left corner of your main screen. With Wander on, she hops onto its top edge as it comes up and sits there like it's a window, bobbing to the music. With Wander off she stays where you put her, and you can drag her onto it.
- It shows what Windows' volume flyout shows: the song and the artist, from Spotify, YouTube in a browser, Media Player and other players. The time played and the song's length show on the right when the app gives them.
- Previous, play/pause and next go to the app that's playing. Click or drag along the seek bar to skip through the song, if the app allows it.
- The heart is only there for Spotify. It likes the song in Spotify, or takes the like back, and it's filled when the song is already in your Liked Songs. She presses Spotify's own heart, or its Like key (Alt + Shift + B) with Spotify in front for a moment.
- The menu button, or right-click the player: Open Spotify (or whichever app is playing), Hide for now, and Turn off mini player.
- The X puts it away until nothing has played for 10 seconds, so it's back for your next song.
- Drag it by its body to move it. It keeps that spot, and carries her along if she's sitting on it.
- It stays up for a minute after you pause. It hides during full-screen games and videos, and while she's hidden.
- It follows Always on top: on, it stays over your windows, just under her. Off, it sits among them.
- Her own screenshots during a job look straight through it.

Turn it off: Mainframe > Settings > Mini player, or the player's menu > Turn off mini player. It's Windows only for now.

### Sleep
If you don't touch the mouse or keyboard for a while, she falls asleep. Move the mouse to wake her. Mainframe > Settings > Sleep sets the time, or Never. On Mac and Linux it's right-click her > Move > Fall asleep after.

To put her to sleep yourself (0.7.6 and newer): right-click her > Move > Go to sleep, or tell her "go to sleep", "take a nap" or "go to bed". She finishes what she's saying, gets off her window and sleeps until you click her, talk to her or say "wake up". Using the mouse and keyboard doesn't wake her from that, and her pop-ins, check-ins and Watch my screen rest until she's up. Telling her "go to sleep" never puts your PC to sleep; "put my PC to sleep" does.

### Startup glitch
When she starts, crimson glitch tendrils tear out from behind her, different every time, while she beams in with her sound. Mainframe > Settings > Glitch in at startup turns all of it on or off. Off: she just appears. On Mac and Linux it's right-click her > Move > Glitch in at startup; off, she starts back where she sat last time, with no glitch or sound.

### Sounds
Mainframe > Settings > Sounds sets her sound effects (arrive, leave, hops, clicks). Volume sets her spoken voice. Pitch, Speed, Robot and Glitch change how all her voices sound. Typing sound turns the soft typing sound on or off.

---

## 15. What she can do on your PC (Windows)

Just ask her, in text or voice chat. Requests work in English, Spanish, French, German, Portuguese, Italian, Polish, Dutch, Russian, Ukrainian, Turkish, Chinese, Japanese, Korean, Thai, Vietnamese, Indonesian, Arabic, Hindi, Swedish, Norwegian, Danish, Finnish, Czech, Slovak, Hungarian, Romanian, Greek, Filipino, Malay, Swahili, Catalan, Serbian, Croatian, Bosnian, Estonian, Latvian, Lithuanian, Slovenian, Bulgarian, Maltese, Hebrew, Urdu, Persian, Bengali, Marathi, Gujarati, Punjabi, Tamil, Telugu, Kannada and Malayalam ("abre Spotify", "öffne Spotify", "打开Spotify", "Spotifyを開いて", "스포티파이 열어줘", "spotify kholo", "Spotify open pannu", "öppna Spotify", "άνοιξε το Spotify"). In other languages, if she answers instead of doing it, ask in English.

If her reply says she can't open, play or access something she can do (like "I don't have access to your Spotify"), she tries it with her tools before giving you that answer.

### Open apps, games and sites
- "Open Spotify", "Open Roblox", "Open YouTube"
- "Search YouTube for parkour"
- She opens anything in your Start menu's All apps list.
- "No app called X is installed on this PC." means it isn't in your Start menu. Use the name it has there.
- When several apps match, she opens the one whose name is closest to what you said. Say its full name to get a different one.

### Music
- "Play Chippin' In by SAMURAI": plays it on Spotify if you have it, or on YouTube if not.
- Playlists, albums, artists and "my liked songs" work on Spotify.
- "Pause", "next", "previous", "turn it up", "turn it down", "mute", "unmute", and other ways of saying them: "turn up the volume a bit", "it's too loud", "set the volume to 50%", "turn on the volume", "turn mute off", "skip to the next song". Spanish and French work too ("sube el volumen", "baisse le son").
- These work on every brain, straight away, and even before you've set one up.
- Volume means your PC's volume. She only drags Spotify's own slider when you say Spotify ("turn Spotify up").
- "What's playing?"
- "Play something": she picks from the Cyberpunk 2077 and Edgerunners soundtracks.
- Name the artist for the best match.
- "Opened X in Spotify, but couldn't press play. Hit the green Play button." Press Play yourself.
- "Couldn't find X to play, so Spotify is open on a search for it." Pick the song from the search.

### Switch, minimize or close apps
- "Switch to Discord", "Minimize Chrome", "Close Discord"
- "Close the YouTube tab" closes that browser tab.
- "Asked X to close, but it's still open. It might be asking you to save." Check that app.

### Discord and Windows shortcuts
She presses the keys for you:
- "Mute my mic" / "unmute me": Discord's mute (Ctrl + Shift + M)
- "Deafen me": Discord's deafen (Ctrl + Shift + D)
- "Take a screenshot" (Win + Shift + S)
- "Clip that" (Win + Alt + G, Xbox Game Bar)
- "Start recording" (Win + Alt + R)
- "Open the game bar" (Win + G)
- "Show the desktop" (Win + D)
- "Open task manager" (Ctrl + Shift + Esc)
- "Open clipboard" (Win + V)
- "Open the emoji picker" (Win + .)
- "Voice typing" (Win + H)
- "Turn on live captions" (Win + Ctrl + L)
- "Start narrator" (Win + Ctrl + Enter)
- "Open magnifier" zooms in (Win + Plus), and "close the magnifier" closes it (Win + Esc)
- "Turn on colour filters" (Win + Ctrl + C), once its keyboard shortcut is on in Settings > Accessibility > Color filters
- "Open quick settings" (Win + A)

Bluetooth, Wi-Fi, airplane mode, hotspot, Nearby sharing, Energy Saver, night light and brightness have no shortcut, so she flips them as a job with her cursor in Quick Settings or Settings.

For Discord she brings Discord to the front, presses the keys, then switches you back. If Discord isn't open she opens it. The Discord ones use Discord's default keys. If you changed them in Discord, they won't work.

### Jobs with your mouse and keyboard
- "Send hi to Sam on Discord", "Search for cats in Chrome", "End the call"
- Her own pink pointer glides over and clicks. Your mouse stays where you left it: she only borrows it for each click and gives it back. Recordings show her pointer.
- A "CAPRI: working on your task" box in the corner shows each step, and she sits on top of it while she works, so she never covers her pointer. Drag her or the box to move both; the box stays where you put it for later jobs until Capri restarts. When it fades she goes back to where she was. A "Capri is controlling ..." badge shows on the app she's using. Screen recordings and your screenshots show the box but not the badge, and her own screenshots look straight through both.
- Only Esc stops her: "You pressed Esc, so I stopped." It works even while she's holding a key down or pressing one over and over, and it stops the whole ask: if you asked for eight songs, the rest don't play. Moving your mouse doesn't stop her.
- If her brain fails partway through a job (a rate limit, a timeout or an outage), she lets go of any mouse button she was holding and gives your mouse back.
- One job uses your mouse at a time. Ask for another while she's busy and it waits its turn.
- With a Claude key she works from screenshots and checks what happened after each step.
- "Send" sends the message. "Write", "type" or "draft" leaves it for you to send.
- On Trusted, the default, she asks first before buying or paying for anything, or before sending, posting, deleting or installing something you didn't ask for. Reply yes, or no ("Okay, I left it alone."). Book now and Reserve count as paying. In SECURITY (Settings > HABITS > Security), Pay, buy and book on stops the first ask and Things your request didn't mention on stops the second; Send, post and delete what you asked for off makes her ask ("Should I send "hi"?") even about what you asked for. On Blackwall she never asks.
- Keys that could run anything (Win + R, Win + X, Win + L, Shift + Delete, Ctrl + Shift + Esc, Ctrl + Alt + Delete) count as something you didn't mention: she asks "Should I press win+r?" first, and your yes covers the rest of that job (before 0.7.6 she only pressed them with Blackwall mode on).
- She skips password boxes ("Won't type in a password box.") unless Type passwords and card numbers is on in SECURITY (Blackwall turns it on). It's the one toggle where off doesn't mean she asks: you type them yourself. On a sign-in page she says: "You'll need to sign in yourself first. I don't type passwords. Tell me when you're in and I'll carry on."
- She doesn't type card numbers unless Type passwords and card numbers is on. A job that reaches a card form ends with: "It's at the payment page. You'll need to put in your card details and pay yourself. I don't type card numbers."
- With Type passwords and card numbers on, she types passwords and card numbers when your job gives them. Anything she types into a password box shows as dots (••••••••) in the steps box, in her reply and in her saved chat, and she says "the password" out loud instead.
- Jobs have no step limit. Big models can work on one for as long as it takes; Esc stops her.
- On forms she only types names, emails, phone numbers and addresses that your job, her notes or the screen gave her, never made-up ones like "John Doe". If your job doesn't give them, she asks: "This form needs First name, Last name and Email. What should I put?"
- Cookie banners get Reject all or Only necessary unless your job says to accept cookies. Newsletter and app popups get No thanks or Close. She never clicks Add to cart unless your job says to.
- She can remember a code or order number partway through a job and type it later, and wait for words to show up on screen, like an emailed code.
- When she finishes with a plain "done", she says what she typed and where: 'Done. Typed "7731-QX" into "Order number".'
- Bigger brain models handle jobs better. If she gets lost, pick a bigger model under BRAIN, or a Claude key under MAX INTELLIGENCE.

### Look things up
"Google the weather in Sydney", "look up Silksong", or questions about weather, news, scores, prices or times: she opens a Google search in your browser ("Pulled up "X" on Google for you, choom.").

### "Say please."
Now and then she asks you to say please. Reply "please" and she does it.

### What she can't do
- She has no file tool yet, so she can't read what's inside your files.

### Security: what she does without asking (0.7.6 and newer)
Capri Mainframe > Settings > HABITS > Security (where Blackwall mode was) opens the SECURITY window. A slider picks a preset, and eleven toggles under it say what she does on your PC without asking. Each toggle has a line saying what on and off mean. Off means she asks first and does it when you say yes, never a silent no. The one exception is Type passwords and card numbers: off, you type those yourself. Esc or the X closes the window, and it's saved straight away, after updates too.

The presets:
- Lockdown: everything off. She asks first before she does anything on your PC.
- Guarded: Open apps, games and sites, Music, media and volume, and Shortcuts on. She asks first for the rest.
- Trusted (the default): the first seven on. She does what you ask, and asks first before paying or anything you didn't mention. It's how she worked before 0.7.6.
- Blackwall: everything on, Listen for "Hey Capri" too. She never asks, and types the passwords and card numbers your job gives her. It's what Blackwall mode was (Bypass all permissions before 0.7.4). If you had Blackwall mode on, you're on Blackwall after updating; everyone else starts on Trusted.
- Custom: flip any toggle and the preset says Custom. Click the slider, or drag it to a preset, to start again from that preset.

The toggles, in order:
1. Open apps, games and sites
2. Music, media and volume
3. Shortcuts (screenshots, Game Bar, captions, Discord mute)
4. Close and switch apps and tabs
5. Shut down, restart, sleep, lock and sign out
6. Use your mouse and keyboard for jobs. Off, she asks before a job with her pink pointer, and does music and video the quick way instead of with her cursor.
7. Send, post and delete what you asked for. Off, she asks "Should I send "hi"?" even about a message you asked her to send.
8. Pay, buy and book. Off, she asks before buying, paying or booking, even when you asked for it.
9. Things your request didn't mention. Off, she asks before a send, post, delete, install or purchase your job didn't mention, and before keys that could run anything, like Win + R ("Should I press win+r?").
10. Type passwords and card numbers. Off, she never types them: "You'll need to sign in yourself first."
11. Listen for "Hey Capri": she listens on this PC for her name. Only Blackwall turns it on.

How she asks:
- With one of the first six off, she answers "Open Spotify? Say yes and I will." Say yes (or yeah, sure, ok, do it) and she does it. Say no and she leaves it: "Okay, I left it alone." Say anything else and she drops it and answers that instead.
- Ask for a few things and she does the ones that are on, then asks once about the rest: "Opened Spotify. Turn the volume up and lock your PC? Say yes and I will." One yes does them all.
- In voice chat she asks out loud, and your spoken yes does it. It works before she has a brain too.
- Inside a job, toggles 7 to 10 decide which steps she stops at to ask "Should I ...?". Yes carries on, no stops the job.
- Looking at your screen, her notes, her own naps, moves and animations, checking what's playing and cancelling a shutdown never ask, even on Lockdown.

On Mac and Linux it's right-click her > Settings > Security: the four presets and the five toggles Mac and Linux have (1 to 5), each with its line. Her mouse and keyboard jobs and "Hey Capri" are Windows-only so far, so there Trusted and Blackwall both do what you ask without asking first.

---

## 16. Watch my screen

### How do I turn it on?
Mainframe > Settings > WATCH > Watch my screen. The Every slider sets how often she looks (10 s to 10 min). On Mac and Linux: right-click her > Settings > Watch my screen, and Glance every.

### Check my screen
Any time, ask "check my screen", "what's on my screen?", "can you see this?", "look at this error" or "rate my build". She takes a screenshot right then and answers about it. Watch my screen doesn't need to be on.

### Why doesn't she comment much?
She skips a look when:
- your screen hasn't changed
- it's blank, a desktop or a loading screen
- you've been away for 10 minutes or more
- voice chat is on, you're typing to her, or she's talking
- she's hidden
- she'd only repeat the same comment

So she may stay quiet even at 15 s. That's on purpose. A look is a screenshot every so often, not a video, and she doesn't hear your PC's sound.

### Does she remember what she saw?
Yes. Each glance gives her a short note of what's on screen (under 20 words) plus her line. What she comments on goes in her chat as "you glanced at their screen and saw: ...", and every "check my screen" is kept too. She keeps the last 3 hours of it in mind, in text and voice chat, and it carries over a restart. Ask "what did you see?" or "what was that game?" and she answers from what she really saw.

### Keep her caught up
Mainframe > Settings > WATCH > Keep her caught up. On by default.
- On: glances she keeps quiet about go in her memory too.
- Off: only the glances she comments on, and the looks you ask for.

What it costs: each glance writes about 25 more words, and each chat message carries what she saw lately (up to about 300 words). With an API key that's a small extra next to the screenshot each glance already sends. With her built-in brain or Ollama it's free.

### Which brains can see?
- Built-in Gemma brains: yes, after her eyes download (about 560 MB, automatic).
- Built-in Qwen3 4B: no. "This brain can't see. Press Free key to fix."
- NVIDIA: most models can't. nvidia/nemotron-3.5-super-vl-120b-a12b (her first pick) and meta/llama-3.2-11b-vision-instruct can. nemotron-3.5-bolt-e4b and gpt-oss-20b can't.
- DeepSeek: no. "DeepSeek can't see. Use an NVIDIA key to watch."
- Ollama, OpenAI, Claude: depends on the model.

### "The screen capture is black. Try borderless or windowed mode."
Some games in exclusive full screen block captures. Switch the game to borderless or windowed in its settings.

### She's hidden behind my full-screen game
Exclusive full-screen games can cover her. Switch the game to borderless or windowed mode.

### "Couldn't capture the screen." or "Couldn't read the screen."
She tries again next time. If it keeps happening:
1. Check that typed chat works with the same brain.
2. Check which BRAIN row and exact model are picked.
3. Pick a model that can see: nvidia/nemotron-3.5-super-vl-120b-a12b. Most NVIDIA models can't. One user's screen reading failed on nemotron-3.5-bolt-e4b, which can't see.

### She made something up about my screen
Smaller brains on your PC sometimes guess wrong, like saying you have a document open. The free NVIDIA key sees better.

### What does she send?
A picture of the screen your active window is on, with herself painted out. With the built-in brain or Ollama it stays on your PC. With a key it goes to that company.

---

## 17. Updates

### Windows
Capri checks for updates 20 seconds after she starts, then every 3 hours. When one is ready:
1. A "Capri update ready" message pops up.
2. Right-click her (or her tray icon) > Restart to update to Capri X.

To check now: Mainframe > Check for updates, or right-click her > Help > Check for updates.
- "You're on the latest version (X)."
- "Couldn't check for updates. I'll try again in 3 hours." Check your internet.

Updates are checked against their checksum and signature before they run.

### "Windows couldn't verify the update signature. It won't run." or "The update checksum doesn't match. It won't run."
The update was blocked to keep you safe. Download the latest Capri-Setup.exe from the website and run it. Your settings and notes stay.

### I'm on an old version (like 0.5.0) and it won't update
Download the latest Capri-Setup.exe from the website and run it. It installs over the old one.

### "Capri updated but couldn't restart. Open her from the Start menu."
The update worked. Open Capri from the Start menu.

### "Capri is still closing. Try the update again."
Wait a few seconds, then press Restart to update again.

### Mac and Linux
No automatic updates yet, but she tells you when a new version is out: "Capri X is out. Right-click me > Get Capri X."
1. Right-click her > Get Capri X. Its download page opens. (The item only shows when there's a new version.)
2. Download Capri-mac-apple-silicon.zip, Capri-mac-intel.zip or Capri-linux-x64.tar.gz.
3. Quit Capri and put the new one where the old one was. Her settings, notes, chat and keys stay, because they're kept outside the app.

### Where can I see what changed?
- The changelog on the website's main page
- GitHub releases: https://github.com/lachydotmcg/capri-desktop/releases
- The Discord

---

## 18. Uninstall

### Windows
1. Right-click her > Quit Capri.
2. Settings > Apps > Installed apps > Capri > Uninstall. (Windows 10: Settings > Apps > Apps & features.)
3. In "Remove Capri", press Remove Capri.

It removes the app, her brain download, the shortcuts and the Installed apps entry. It keeps your settings, notes, personality, chat and error log in %LOCALAPPDATA%\Capri, in case you come back.

Errors:
- "Quit Capri from her tray menu before removing her."
- "The uninstaller is still open. Close it and try removing Capri again."

### Remove everything
1. Uninstall as above.
2. Delete the folder %LOCALAPPDATA%\Capri.
3. If you used voice chat: in ElevenLabs, delete the agent called "Capri".
4. If you want: delete the keys you made for Capri on ElevenLabs and NVIDIA (or OpenAI, Claude, DeepSeek).

### Mac
Quit Capri, then move Capri from Applications to the Bin (Trash). Her settings are in ~/.config/Capri.

### Linux
Quit Capri, then delete the Capri folder you unpacked. Her settings are in ~/.config/Capri.

---

## 19. Privacy

### What stays on my PC
- Her built-in brain and Ollama: your chats never leave your PC.
- Her free voice, and her own ears in free voice chat (English and 24 other European languages).
- Her notes (me.md), chat history and what she saw on your screen lately (chat.json, kept a week), settings and error log.
- Her transcripts (transcripts folder, one file per day) and memories.md. They're never uploaded.
- Your keys. On Windows they're locked to your Windows account. On Mac and Linux the ElevenLabs key is a file only your user can read.
- Dancing: she listens to what your PC is playing to find the beat, in memory only. Nothing is recorded or sent.
- "Hey Capri" (Windows, off by default): it listens for those two words on your PC with its own small model. Nothing is recorded or sent until it hears them and voice chat starts. Windows shows its mic icon while it's on.
- Capri has no ads and no analytics.

### What goes to the services you pick
- A brain key (NVIDIA, OpenAI, Claude, DeepSeek): your messages, her personality, her notes, memories.md, recent chat and what she saw on your screen lately go to that company. With Watch my screen on, or when you say "check my screen", so does the screenshot. With Remember our chats on, folding sends that company the newest transcript lines too, voice chat lines included, and the few older lines her search finds for a message go with that message.
- ElevenLabs: the text she says out loud. In ElevenLabs voice chat: your voice, your words, her personality, her notes, memories.md and what she saw lately.
- Free voice chat in a language other than English uses Windows speech, which goes through Microsoft.

### The help chat on the website
- What you type goes to Cloudflare's AI so she can write an answer. Capri's site doesn't save your questions.
- You can drop or paste a screenshot. Cloudflare's AI reads it to answer, and it isn't saved. Crop out keys, emails and names first.
- It's capped at 40 questions and 10 pictures a day per person. To count that, the site keeps a scrambled code for your internet address until the next day.

### Other things she connects to
- GitHub: update checks and downloads.
- Hugging Face: her built-in brain download.
- DuckDuckGo and YouTube: finding songs you ask her to play.
- Your browser: when she opens Google, YouTube and other sites for you.

### Where are my files?
- Windows: %LOCALAPPDATA%\Capri (settings.json, me.md, memories.md, chat.json, errors.txt, and the transcripts and brain folders)
- Mac and Linux: ~/.config/Capri

---

## 20. Copy error details and getting help

### Copy error details
1. Right-click her > Help > Copy error details.
2. She says "Copied my error log. Paste it in the Discord so they can fix me, choom."
3. Paste it (Ctrl + V) in the Discord.

It copies your Capri version, which brain and voice you use, and the last 25 lines of her error log. It's made to leave out your keys. It can include a few words from a voice chat that went wrong, so read it before you post it.

On Mac and Linux, her error log is errors.txt in ~/.config/Capri.

### Where to get help
- Mainframe > Help, or right-click her > Help > Help page: opens this Help page.
- Capri in the bottom-right corner of the website: click her and ask, or drop a screenshot. She answers from this help.
- The setup guide: https://lachydotmcg.github.io/capri-desktop/setup/
- Discord: https://discord.gg/F4fDHfGtFk (or right-click her > Help > Join the Discord)
- GitHub: https://github.com/lachydotmcg/capri-desktop

### What to send when you ask
- What's wrong, in one line
- Windows, Mac or Linux version, and your Capri version (top of the Mainframe)
- The exact message, word for word, or a screenshot with keys covered
- What still works: typing to her, hearing her, her hearing you, screen reading, jobs
- What you tried, and what happened after each try
- Copy error details (right-click her > Help > Copy error details)

## 21. Mac and Linux preview: what works

### Works
- Sitting on windows (X11 and Mac) or the bottom of the screen (Wayland)
- Chatting by typing: hover her and press the pencil, or right-click her > Talk to her
- Her brain through Ollama, or through a free NVIDIA key (or an OpenAI, Claude or DeepSeek key) with no Ollama needed
- Her ElevenLabs voice for spoken replies
- Remember and forget, nickname, language
- Wander, dance to music, petting, sizes
- Check in on me: every 8 to 20 minutes while you're at the computer she glitches in at the top of the screen to say hi, even when hidden, then hides again if she was. With Wander on Stay put, or on a Sit here spot, she says hi and asks for attention from where she is instead (from 0.7.6). Right-click her > Settings > Check in on me turns it off, and Pop-ins sets Rarely (20 to 40 minutes) or Often (3 to 8). She skips it while you're away from the computer: Mac always knows, Linux knows through GNOME or, on other X11 desktops, xprintidle. If nobody's clicked, petted or talked to her for about 2 hours while you're at the computer, she comes over near your mouse and asks for some attention (on Wayland she asks from where she sits); notice her and she's pleased, ignore her for 20 seconds and she sulks and waits twice as long, up to 8 hours. With it on she also cheers when Cyberpunk 2077 starts or a 2077 or Edgerunners song plays (Spotify or Music on Mac; any player or browser tab on Linux), at most once every two hours for each, and when the CPU sits near 100% for about a minute she jokes that she might go cyberpsycho (at most every half hour, never over a full-screen window).
- Opening apps, Steam games (from Steam's own library, Flatpak and Snap Steam too) and websites, playing songs on Spotify, skipping tracks, what's playing, volume up and down, an exact volume ("volume 40") and mute. On Linux that's pactl, or amixer without it.
- Closing, minimizing and switching to apps ("close Discord"). Mac asks once to let Capri control System Events; press OK. Linux needs wmctrl (and xdotool to minimize), and on Wayland she only sees apps running through XWayland, like Discord and Spotify.
- Shut down, restart, sleep, lock and sign out ("lock my PC", "shut down my PC in 10 minutes"; say cancel to stop a wait)
- "Take a screenshot" (screencapture on Mac; flameshot, spectacle, gnome-screenshot or your desktop's own screenshot window on Linux), "open task manager" (Activity Monitor, or your system monitor) and, on Linux, "show desktop". Shortcuts that press keys, like Discord mute or Game Bar, are Windows-only.
- "Playing Capri" on your Discord profile, with Show on Discord in Settings
- Start at login: right-click her > Settings > Start at login. It's off until you tick it. On Mac it adds a login item that opens Capri wherever you keep her, even after you move her (macOS says Capri added a background item; manage it in System Settings > General > Login Items). On Linux it adds ~/.config/autostart/capri.desktop, which starts the copy you ticked it in; if you move Capri, open the new copy once.
- Right-click her > Help > Copy error details, with your system, desktop and her brain at the top
- "Check my screen": she looks when you ask (see "Check my screen on Mac and Linux" below)
- Watch my screen: right-click her > Settings > Watch my screen. She glances now and then and comments when something's worth a line, like on Windows. Glance every sets how often (10 s to 10 min, default 2 min), and Keep her caught up keeps the glances she stays quiet about. Mac asks once for Screen Recording; until you allow it and open her again, she says so once instead of watching your wallpaper. Linux only watches with tools that don't flash: grim, Spectacle on KDE, ImageMagick's import, scrot or maim. GNOME on Wayland flashes every screenshot, so there she says so and only looks when you ask.
- Sleep: standing, she falls asleep after 2 minutes without the mouse or keyboard, and wakes when you're back. Right-click her > Move > Fall asleep after sets the time, or Never. She needs to know how long you've been away: Mac always does, Linux through GNOME or, on other X11 desktops, xprintidle.
- Sleep on demand (0.7.6 and newer): right-click her > Go to sleep, or tell her "go to sleep" or "take a nap". She sleeps until you click her, talk to her or say "wake up", whatever the mouse and keyboard do.
- Her own moves (0.7.6 and newer): "capri, teleport here", "go to my other monitor", "sit down", "stand up", "hang here", "let go", "hide" and "show yourself", and her animations by name ("dance", "stop dancing", "do your glitch", "smile", "blink", "play your idle animation"), even before she has a brain. Screens count from the left. Riding your cursor and moving the Mainframe are Windows-only, and she says so.
- Getting to know you (0.7.6 and newer): on a new install, once she's said hello, she asks what to call you, your pronouns and what you're into in her typing box and writes the answers to me.md, like on Windows. Right-click her > Settings > Get to know me asks again.
- Security (0.7.6 and newer): right-click her > Settings > Security picks Lockdown, Guarded, Trusted (the default) or Blackwall, and ticks the five toggles Mac and Linux have: Open apps, games and sites; Music, media and volume; Shortcuts; Close and switch apps and tabs; and Shut down, restart, sleep, lock and sign out. With one off she asks first ("Open Spotify? Say yes and I will.") and does it when you say yes, like on Windows. Untick or tick one and it says Custom.
- Quiet: right-click her, or the tray or menu bar icon, > Quiet. It's the same as Windows' quiet key (Ctrl + Alt + P): she stops pop-ins, asking for attention, cheers, the lag joke and Watch my screen until you untick it, and still answers when you talk to her.
- Remember our chats: a daily transcript and memories.md, like on Windows (right-click her > Settings). She digs through them on her own here too.

### Not yet
- No Mainframe. Everything is in her right-click menu.
- No voice chat: "Voice chat isn't on Mac and Linux yet. Type to me for now, choom."
- No built-in brain. Use Ollama or an API key.
- No keyboard shortcuts or mouse and keyboard jobs.
- No automatic updates. She says when a new version is out, and right-click her > Get Capri X opens its download page.
- No mini player.

### Right-click menu
- Talk to her
- Quiet: for when you focus or game. She stops pop-ins, asking for attention, cheers, the lag joke and Watch my screen until you untick it, and still answers when you talk to her. It's in the tray or menu bar menu too. Starting her again ends it.
- Sit here: she sits right where she is. While she sits like that it says Stand up. Holding Shift as you drop her does Sit here too, like on Windows.
- Hang here: she hangs on right where she is. While she hangs it says Let go.
- Go to sleep: she sleeps until you click her, talk to her or say "wake up". While she sleeps it says Wake up.
- Move: Stay put, Wander: Rarely, Wander: Sometimes, Wander: Often, Wander: Restless, Dance to music, Dance (on until you untick it or say "stop dancing"), Go to the next screen (only with more than one screen), Fall asleep after (Never, 1 min, 2 min, 5 min, 10 min, 30 min), Glitch in at startup
- Size: Tiny, Small, Medium, Large, Huge, Giant
- Brain: your Ollama models. With Ollama open but no models yet: "Download gemma4:e4b, her brain (a few GB)". With Ollama closed or not installed: "Ollama isn't running. Install it from ollama.com and open it." Below them: Get a free NVIDIA key, and Use an API key... On a key it shows "NVIDIA key ending XXXX" (or ChatGPT, Claude, DeepSeek), that key's models to pick from, Stay on my model (NVIDIA keys only), and Forget the key and use Ollama.
- Settings: What should she call you?, Get to know me, Language, Reply length, Chat memory, Bubble (Quick, Normal, Long), Sounds (Off, Quiet, Normal, Loud, Typing sound), Voice, Volume (Mute, 10% to 100%, 125%, 150%), Voice style (Pitch, Speed, Robot, Glitch, Layers, Layer 1 pitch, Layer 1 delay, Layer 2 pitch, Layer 2 delay), Check in on me, Pop-ins (Rarely, Sometimes, Often), Always on top, Watch my screen, Glance every (10 s to 10 min), Keep her caught up, Start at login, Show on Discord, Security (Lockdown, Guarded, Trusted, Blackwall, Custom, then the five toggles), Open her notes (me.md), Remember our chats, Open her memories (memories.md)
- Help: Help page, Copy error details
- Join the Discord
- Get Capri X, only when a new version is out
- Quit Capri

Tray or menu bar icon: Talk to her, Quiet, Hide her / Show her, Bring her back, Quit Capri.

### Her voice on Mac and Linux
1. Right-click her > Settings > Voice > Add ElevenLabs key...
2. Paste your key and press Enter. It stays on this computer.
3. She says "Key saved. I'll talk as X. Change it under Settings > Voice."
4. Pick another voice from the same menu. Use ElevenLabs in that menu turns her voice on or off. (Mac and Linux have no free voice yet.)
5. Settings > Volume sets how loud she talks, from Mute to 150%, like Windows' Volume. Sounds is her sound effects.
6. Settings > Voice style changes her pitch, speed, robot and glitch, like Windows' sliders (see "Pitch, speed, robot and glitch"), and Layers turns on her voice layers, with each layer's pitch and delay under it (see "Voice layers").

- "Key saved, but your ElevenLabs account has no voices yet. Add one at elevenlabs.io." Make a voice first (section 11).
- "ElevenLabs rejected the key. Settings > Voice > New key."
- Remove key ending XXXX removes it.

### Check my screen on Mac and Linux (0.7.6 and newer)
0.7.4 and older can't see the screen on Mac and Linux. Update to 0.7.6. Ask "check my screen", "what's on my screen?" or "can you see this?". She takes a screenshot and her brain looks at it.
- Her brain has to see pictures: a free NVIDIA key (nvidia/nemotron-3.5-super-vl-120b-a12b), a Claude or OpenAI key, or an Ollama model that can see pictures. DeepSeek can't.
- Mac: the first time, macOS asks to let Capri record the screen. If she only describes your wallpaper, allow Capri under System Settings > Privacy & Security > Screen Recording, then reopen her.
- Linux: she uses your desktop's screenshot tool: spectacle (KDE), gnome-screenshot (GNOME), grim (Sway, Hyprland and other wlroots desktops), or ImageMagick, scrot or maim on X11. If none is installed she says so; install one and ask again. GNOME on Wayland may refuse apps' screenshots.

### A free NVIDIA key on Mac and Linux (0.7.6 and newer)
No Ollama needed, and nothing to download. 0.7.4 and older only think with Ollama on Mac and Linux; update to 0.7.6 for an API key.
1. Right-click her > Brain > Get a free NVIDIA key. NVIDIA's key page opens.
2. Make a key there (see "The free NVIDIA key" above) and copy it.
3. Right-click her > Brain > Use an API key..., paste the key in her box and press Enter. It stays on this computer. (Her box closes if you click away, so copy the key first.)
4. She says "Key saved. I'm thinking with X now. Change it under Brain in my right-click menu." Pick another model under Brain; Nemotron Super and Nemotron's reasoning model are marked recommended at the top. Forget the key and use Ollama goes back to Ollama.

If an NVIDIA model gets stuck, she switches to another one and answers. She goes back to your pick the next time she starts. To keep her on your model, tick Brain > Stay on my model; she then says what went wrong instead.

An OpenAI, Claude or DeepSeek key works the same way: right-click her > Brain > Use an API key...
- "That doesn't look like an NVIDIA, OpenAI, Claude or DeepSeek key. NVIDIA's start with nvapi-." You pasted something else.
- "Couldn't reach NVIDIA. Check your internet, then paste the key again." (or another company)
- "That key can't use any chat models."

### Music on Mac
The first time she controls Spotify or Music, macOS asks "Capri wants to control Spotify". Press OK.

### Music on Linux
She controls music players that support MPRIS, like Spotify, Rhythmbox or Elisa. Browser tabs don't count.

### "My brain's offline. Install Ollama from ollama.com and open it, then right-click me > Brain > Download gemma4:e4b."
Install Ollama and open it, then right-click her > Brain > Download gemma4:e4b. Older versions said to run ollama pull gemma4:e4b in a terminal, which still works; then pick the model in right-click her > Brain.

### A button or menu from this help is missing on my Mac or Linux
The preview doesn't have everything Windows has yet (see "Not yet" above). A missing Mainframe, voice chat or job feature on Mac or Linux usually means the preview doesn't have it, not that your install is broken. Check your version too.

### Where's the Mainframe on Mac or Linux?
There isn't one yet. Use her right-click menu.

---

## 22. Older versions (0.6.2 and before)
Check your version at the top of the Mainframe. If you're on 0.6.2:
- There are no FREE VOICE and ELEVENLABS rows and no free voice. A "Speak replies" box turns ElevenLabs on or off.
- There's no Settings button or Settings page. No language, nickname, notes, volume, wander level, sleep time or custom model settings.
- No Help button, and no Setup guide button (it's "Get a key").
- Size is four buttons in the Mainframe: Small, Medium, Large, Huge. The right-click Size menu has no Giant.
- No Dance to music, no petting, no startup greeting.
- For jobs she moves your real mouse instead of her own pointer.
- She keeps only a little recent chat. Her notes (me.md) don't exist yet.
- The talk and mute keys are fixed at Ctrl + Alt + V and Ctrl + Alt + N.
- "Can't open your speakers." doesn't say why. See the speaker fixes in troubleshooting.
- "Out of ElevenLabs credits. Untick Speak replies to type."
- Voice chat is English only, and needs an ElevenLabs key.
- If her replies fill with <unused49>, use the free NVIDIA key.
- "Play something" often plays Blinding Lights. That was a bug.
- Mac and Linux 0.6.2 have no voice. Their right-click menu is Talk to her, Sit here, Size, Join the Discord and Quit Capri.

The 0.6.9 test builds had a "Use ElevenLabs" box where 0.7.0 has the FREE VOICE and ELEVENLABS rows.

Updating to the latest version gets you everything in this guide.

## 23. Troubleshooting

### Where to start
"Capri isn't working" can be very different problems. First work out what still works:
- Typing to her and getting a reply (her brain)
- Hearing her talk (her voice)
- Her hearing you in voice chat (your mic)
- "Check my screen" (screen reading)
- Jobs like "open Spotify" (doing things)
Then check the exact message, your Capri version (top of the Mainframe), and the exact BRAIN line, like "Built-in · Gemma 4 E4B" or "Ollama · gemma4:e4b". Try one fix at a time and check if it helped.

### Install and antivirus
- "Windows protected your PC": More info > Run anyway.
- "Item Not Found", or the installer vanished: Ctrl + J > Keep in Chrome or Edge, or restore it from your antivirus quarantine. Open it from your Downloads folder.
- Antivirus says Trojan, Wacatac, Zusy, Gen:Suspicious or MachineLearning/Anomalous: if you downloaded it from the Capri website or GitHub, it's a false alarm on a new unsigned app. Restore and allow it.
- Opera "Insufficient permissions": use Chrome or Edge, or download from GitHub releases.
- Phone says "dangerous download": Capri is a PC app. Download it on your PC.
- "Quit Capri from her tray menu, then install again.": quit Capri first.
- Windows 7, 8 or 32-bit Windows: not supported. Capri needs Windows 10 or 11, 64-bit.
- Capri vanished after installing, or the shortcut doesn't work: your antivirus probably removed Capri.exe. Restore it from quarantine, allow it, then run Capri-Setup.exe again.
- An "authorization" or permission error while installing: Capri doesn't need admin rights. Run Capri-Setup.exe from your Downloads folder and check your antivirus didn't block it. Still failing? Post a screenshot of the exact message in the Discord.
- The installer "disappeared": work out which happened. The browser download failed, the file vanished after it downloaded, the installer opened and closed, or Capri opened and closed. They have different fixes. One "permissions" problem was really Opera's download failing while Bitdefender quarantined the browser's cache.
- For a real antivirus detection, note the antivirus, the exact detection name, the file's path and where you downloaded it, and post them in the Discord. Don't turn your antivirus off.
- "Installing the local speech model" is taking a while: it's about 316 MB. Wait, or press Skip and get it later with Get free voice under VOICE.
- "Couldn't install the local speech model": Capri is still installed. Press Finish, then pick FREE VOICE in the Mainframe and press Get free voice.

### "Capri started fresh": my settings were reset
If she closes twice in a row while she's starting (a crash, but also Task Manager, an antivirus closing her, a stuck shutdown or the power going off in her first 15 seconds), the next start resets her settings and her tray icon says "Capri started fresh". From 0.7.6 she keeps your keys: brain and voice keys with the model, voice and server each one is for, and her own keys to talk, mute, type, hide and go quiet. Everything else starts like a new install, and the old settings are in settings.crashed.json in %LOCALAPPDATA%\Capri. One start that didn't finish only goes in errors.txt. Before 0.7.6 one was enough to reset everything, keys too. If it keeps happening, right-click her > Help > Copy error details and post it in the Discord.

### Mac and Linux
- Mac says "damaged" or "can't be opened": drag Capri to Applications, run xattr -dr com.apple.quarantine /Applications/Capri.app in Terminal, open again.
- Mac won't run it at all on macOS 12, 13 or 14: Capri needs macOS 15 or newer.
- "Where's the Mainframe?": Mac and Linux don't have one yet. Right-click her.
- An error inside the Ollama app itself (for example one that mentions ChatGPT): that comes from Ollama, not Capri. Capri only needs Ollama running with a model pulled. Check the model answers in Ollama first.
- Linux says her brain is offline or disconnected: installing Ollama isn't enough, the model has to be downloaded too. Right-click her > Brain > Download gemma4:e4b (or run ollama pull gemma4:e4b), check it answers in Ollama itself (ollama run gemma4:e4b), then right-click her > Brain and pick it.
- Linux blank replies while Ollama itself answers: that's a separate problem we haven't solved yet. Try another model in right-click her > Brain, update Ollama, and tell us in the Discord with your distro and Ollama version.
- Wayland: she can only sit on the bottom of the screen. That's a Wayland limit.

### Brain
- "My brain's offline.": Mainframe > Set up her brain, or Free key for a free NVIDIA key.
- Built-in and Ollama are different brains. "Built-in · Gemma 4 E4B" is her built-in brain, "Ollama · gemma4:e4b" is Ollama. Check the BRAIN line before trying Ollama fixes.
- Ollama is installed but she says her brain is offline: the model needs downloading too. Run ollama pull gemma4:e4b, check it answers in Ollama, then press Refresh under BRAIN.
- "Brain not downloaded. Press Set up her brain.": press Set up her brain.
- Download keeps failing ("The download of <name> keeps stopping."): check your internet and press Try again. It picks up where it stopped. Or use the free NVIDIA key.
- "<name> came down the wrong size." or "<name> failed its checksum.": press Try again.
- "Brain crashed loading. Close games, press Refresh." or "Brain loaded too slowly. Close games, press Refresh.": close games and heavy apps, press Refresh. Or use the free NVIDIA key.
- Replies are very slow, "Still thinking. My brain runs on your PC, so this is slow.", or "Took too long to think": games share your PC's power. Close one, or use the free NVIDIA key (Free key under BRAIN).
- "Waiting for NVIDIA. You can pick another model.": NVIDIA is slow right now. Pick another model in the Model menu, like nvidia/nemotron-3.5-bolt-e4b. It's faster but can't see your screen. The big nemotron 120b model is the slowest, but it can see.
- "NVIDIA didn't answer in time.": try again or pick another model.
- She changed NVIDIA models by herself: the model she was using didn't answer, so she used another one for that reply. Your pick stays.
- "Key rejected. Press Forget, then paste it again.": the key is wrong or was deleted. Copy it again from the company's site.
- "That isn't an NVIDIA, OpenAI, Claude or DeepSeek key.": you pasted something else. NVIDIA keys start with nvapi-.
- "No credits on this key.": OpenAI, Claude and DeepSeek need paid credits. Add credits, or press Free key for a free NVIDIA key.
- "That model isn't on your key. Pick another one.": pick another model in the Model menu.
- "That key can't use any chat models.": make a new key, or use a different company.
- "Too many requests. Wait a minute, or watch slower.": wait a minute, or set Watch my screen to a slower Every time.
- "Their servers failed. Pick another model or retry."
- "Can't reach NVIDIA, choom. Check your internet." (or another company): check your internet.
- "My brain sent gibberish." or replies full of <unused49>: she moves her brain to your processor (CPU) by herself and asks again. If it keeps happening, use the free NVIDIA key. To let the built-in brain try your graphics card again later, delete the file cpu-only.txt in %LOCALAPPDATA%\Capri\brain\engine-b11669.
- "My brain sent an empty reply." or "That model ran out of room before answering": try again, or pick another model.
- "[SIGNAL CORRUPTED] ...": a one-off hiccup. Try again, or ask something else.
- She forgets what you said a few messages ago: Mainframe > Settings > Memory > Long, and keep Remember our chats on so older chats get folded into memories.md. For things she should always remember, tell her "remember ..." so it goes in her notes. If she still forgets, post the exact messages in the Discord.
- She says she guessed what was on your screen: she only remembers her looks from the last 3 hours. Turn on Keep her caught up (Settings > WATCH) so the glances she keeps quiet about count too.
- Her replies are too long or too short: Mainframe > Settings > Replies.
- She makes things up: smaller brains on your PC do this sometimes. The free NVIDIA key is smarter.
- "Your PC doesn't have enough free RAM" or "Your storage is full": see section 10.
- Games lag or stutter while she runs: use the free NVIDIA key.
- Whole PC freezes: quit Capri and see if it stops. Try the free NVIDIA key instead of the built-in brain, and tell us in the Discord with Copy error details, your graphics card, RAM and which brain. (One PC that froze also froze without Capri.)
- NVIDIA asks for a phone number: that's NVIDIA's sign-up check. It's needed to make the free key.

### Voice (ElevenLabs)
- "ElevenLabs rejected the key." or "ElevenLabs rejected that key.": that isn't the key, or it was deleted. ElevenLabs keys start with sk_. Make a new one and paste it.
- "Key is missing permissions." or "Key can't do that.": make a new key with Restrict key turned off. Delete the old key in the Capri box (Forget), paste the new one, press Connect. Older versions said "Key can't do that." whenever ElevenLabs refused, so with Restrict key off it can be something else: check you can make speech on elevenlabs.io with the same account, try without a VPN, then send Copy error details in the Discord. Newer versions only say it when ElevenLabs' answer is about the key, and show ElevenLabs' own reason otherwise.
- "Voice not found. Add it to your ElevenLabs voices.": paste the Voice ID of a voice in your My Voices, not its name. A voice from the ElevenLabs library has to be added to My Voices first.
- "Paste your Voice ID first." or "Needs your Voice ID.": paste a Voice ID under VOICE, or pick one from the list.
- "Out of ElevenLabs credits. Pick FREE VOICE in the Mainframe." (0.7.6: "Pick PREBUILT LOCAL in the Mainframe."): your monthly credits are used up. They refill each month, or you can upgrade on ElevenLabs. Pick FREE VOICE (PREBUILT LOCAL in 0.7.6) to keep talking with no credits. With her free voice installed she switches to it by herself. If ElevenLabs shows "10,000 / 10,000", that means all of them are used, not all left. A "Total 10,000" line alone doesn't show what's left: check the used and remaining amounts on elevenlabs.io.
- "Out of credits" but ElevenLabs shows credits left: check the usage line on elevenlabs.io. If it shows thousands left, right-click her > Help > Copy error details and post it in the Discord.
- "ElevenLabs has restricted your account, so her voice can't connect.": ElevenLabs has blocked your account. Sign in at elevenlabs.io to see why. Pick FREE VOICE (PREBUILT LOCAL in 0.7.6) in the meantime. A new key won't fix this one.
- "Free Tier usage disabled": ElevenLabs turns off the free plan when it sees a VPN or proxy. Turn your VPN off and try again.
- "ElevenLabs sent a reply Capri couldn't read.": ElevenLabs may not be available where you live, or a VPN or network is blocking it. Try without a VPN. Then right-click her > Help > Copy error details and send it in the Discord.
- "Can't reach ElevenLabs. Check your internet."
- "ElevenLabs is busy. Try again in a moment."
- "ElevenLabs error N. Still failing? Make a new key with Restrict key turned off."
- "ElevenLabs turned her voice away (error 403) without saying why.": ElevenLabs, or something on the way to it, blocked the request. Try without a VPN or proxy, and check you can make speech on elevenlabs.io with the same account. Still failing? Make a new key with Restrict key off, then send Copy error details in the Discord.
- "Error deserializing object..." (old versions): update Capri.
- Test shows her words but you hear nothing (even "Voice link online"): that text doesn't mean the sound played. Check Windows plays sound on the device you want, make it the default output in Settings > System > Sound, check Volume isn't on Mute in Mainframe > Settings, then quit Capri (right-click her > Quit Capri) and open her again. Press Test and listen. A full restart fixed it for one user.
- "Can't open your speakers: Windows has no speakers or headphones on.": pick one in Settings > System > Sound. If every other app plays sound fine, update Capri: newer versions try WASAPI, the way most apps play sound, when Windows' older wave sound won't open for her, and note it in her error details.
- "Can't open your speakers: another app has them to itself.": close that app, or untick exclusive mode in your speaker's Sound settings (Settings > System > Sound > your speaker > Advanced).
- "Can't open your speakers (Windows error N).": check Settings > System > Sound, then try again. On 0.6.2 the message is just "Can't open your speakers." Try the same fixes. Changing the mic doesn't help with this one. If it keeps happening, post your speaker's name, how it's plugged in (USB, Bluetooth, jack), your Windows and Capri versions and the line under VOICE in the Discord.
- Her voice is too quiet or loud: Mainframe > Settings > Volume.
- Where's the voice prompt?: on the setup guide, under Her voice > Design her voice. Press Copy.

### Her free voice
- She types but doesn't talk, with FREE VOICE picked: her free voice isn't installed. Press Get free voice under VOICE.
- "ElevenLabs off. I'll type, no credits used.": FREE VOICE is picked but her free voice isn't installed yet. Press Get free voice.
- The download stops: check your internet and press Get free voice again.
- She sounds different from her ElevenLabs voice: that's normal. FREE VOICE runs on your PC and is simpler. Pick a style from "Free voice: Her own", or pick ELEVENLABS for her best voice.
- Mac and Linux: there's no free voice yet.

### Voice chat
- "Voice chat needs an ElevenLabs key": you're on an older version. Update, and voice chat works without a key as free voice chat.
- "Voice chat needs my Voice ID": pick a voice in the Voice ID box under VOICE.
- I want ElevenLabs voice chat but get free voice chat: pick ELEVENLABS under VOICE. Voice chat follows that pick.
- "ElevenLabs is off, so voice chat is too." (0.6.9 test builds): tick Use ElevenLabs, or update. 0.6.9 also had a "Free voice chat" box; in 0.7.0 the FREE VOICE row does that.
- "My ears are still downloading, so type to me for now.": her offline ears download the first time you use free voice chat. She says when she can hear you.
- "My ears didn't download, so type to me for now. ... I'll try again in 30 seconds.": the reason is in the middle. She retries by herself; check your internet and free space on the drive she named.
- "I can't hear anything from your mic.": 20 seconds of free voice chat brought no sound at all. Pick your mic under VOICE > Mic. If it's right, open Windows Settings > Privacy & security > Microphone and turn on "Let desktop apps access your microphone".
- "Free voice chat on, but Windows speech only hears your default mic": while her own ears download, or in a language they don't know, Windows speech uses your Windows default mic. Make the mic you want the default in Windows.
- "Free voice chat on, but I'm hearing offline, so I'll miss some words. Turn on Online speech recognition...": in a language her own ears don't know, Windows speech needs Online speech recognition, in Windows Settings > Privacy & security > Speech. In newer versions, click the line under VOICE in the Mainframe to open that page. If the switch isn't there or is greyed out, your organisation, a family account or your region has turned it off; she still hears you offline, just less well.
- "Out of ElevenLabs credits, so voice chat is off.": wait for your credits to refill, or upgrade.
- "Can't open my voice link. Right-click me and open Capri Mainframe. The reason and the fix are under VOICE.": open the Mainframe and read the line under VOICE.
- "ElevenLabs didn't make the agent." or "ElevenLabs didn't open a voice link.": make a new key with Restrict key off and paste it.
- "The agent sends X audio. Set it to PCM in ElevenLabs.": someone changed the "Capri" agent's audio setting. In ElevenLabs, set the Capri agent's output audio to PCM, or delete that agent and start voice chat again.
- She can't hear you: pick your mic under VOICE > Mic. Check it isn't muted (Ctrl + Alt + N or the mic button). Then Windows Settings > Privacy & security > Microphone > let desktop apps use your microphone.
- "Can't open your mic. Pick another under VOICE.": pick another mic.
- "Voice chat resting.": she's waiting. Just talk.
- Ctrl + Alt + V does nothing: another app may use those keys. Click "Ctrl + Alt + V to talk" in the Mainframe and pick new keys.
- She said she did something but nothing happened: ask again in text, and send Copy error details in the Discord.

### Mainframe and screen watching
- Mainframe squashed or cut off: what's your Windows display scale? On 0.6.2, testing 100% (Settings > System > Display > Scale) and reopening Capri fixed it for one user at 150%.
- Can't find the Mainframe: right-click her > Open Capri Mainframe.
- She never comments: Watch my screen must be on (Mainframe > Settings > WATCH), and she skips unchanged or blank screens. In voice chat she only comments while your mic is muted.
- "This brain can't see. Press Free key to fix.": use the free NVIDIA key.
- "DeepSeek can't see. Use an NVIDIA key to watch."
- "<model> can't see screens.": most NVIDIA models can't. Pick nvidia/nemotron-3.5-super-vl-120b-a12b.
- "The screen capture is black.": set your game to borderless or windowed.
- "Couldn't read the screen.": pick a model that can see, like nvidia/nemotron-3.5-super-vl-120b-a12b. Most NVIDIA models can't. nemotron-3.5-bolt-e4b can't.
- Screen reading fails but typed chat works: check which model is picked and whether the error mentions images. Not every model can see.
- She doesn't hear what's playing on your PC: she reads screenshots, she doesn't listen to your PC's sound. Knowing the Spotify song comes from Windows' media info.

### Moving her
- She's gone: double-click her tray icon (it may be under the ^ arrow by the clock), or press Ctrl + Alt + V, or open Capri again from the Start menu.
- She's stuck on my cursor: Ctrl + Alt + C. Or right-click her tray icon > Move > Hop off.
- She won't wander: turn on Wander (right-click her > Move > Wander around, or Settings > Wander). She only wanders when she's idle and you've used the mouse or keyboard in the last minute and a half. She doesn't wander from a "Sit here" spot (right-click her > Move > Stand up). She needs open windows that aren't maximized, or the taskbar. Give it a couple of minutes.
- She's floating in mid-air: Sit on windows is off. Right-click her > Move > Sit on windows.
- She won't sit on my window: she doesn't sit on maximized or full-screen windows.
- She's hidden behind a game: switch the game to borderless or windowed. Check Always on top is on (Mainframe > Settings > CAPRI).
- She doesn't dance: Dance to music must be on, and the app must show up as music in Windows.
- She dances but her head isn't on the beat yet: give it a few seconds. Quiet songs, talking and very slow songs are harder for her to follow.
- The mini player doesn't show: Settings > Mini player must be on, and the song has to show in Windows' volume flyout with its name. If you pressed its X, it's back once nothing has played for 10 seconds. It hides during full-screen games and videos.
- She doesn't sit on the mini player: she only hops on as it comes up with Wander on, and not while she's talking, sleeping or busy. Drag her onto it any time.
- The mini player has no heart: the heart is only there for Spotify.
- The heart didn't like the song: Spotify must be open in a window, not only in the tray, and its Like key must still be Alt + Shift + B.
- She's asleep: move the mouse. If you put her to sleep (Go to sleep, or "go to sleep"), click her or say "wake up" instead. Change how soon she dozes off in Settings > Sleep.
- She's on the wrong monitor: right-click her > Move > Bring to this screen, or tell her "come here".

### Doing things on the PC
- She only plays one song, or the wrong one: name the song and the artist. "Play something" picks from the Cyberpunk soundtracks.
- Spotify opens but doesn't play: press the green Play button.
- She played it on YouTube, not Spotify: she uses YouTube when Spotify isn't installed.
- "No app called X is installed on this PC.": use the name from your Start menu.
- She won't play a song: some song names have words she blocks (drinking, smoking, drugs and so on). Search it yourself.
- "Mute my mic" doesn't work in Discord: she uses Discord's default keys (Ctrl + Shift + M). If you changed them in Discord, set them back.
- "Discord isn't open, so I couldn't press its shortcut.": open Discord first.
- Mouse and keyboard jobs don't work well: bigger models do these better. Pick a bigger model under BRAIN, or a Claude key under MAX INTELLIGENCE. Your mouse doesn't stop her; press Esc to stop.
- A job worked once and failed later: post the exact words you asked, the BRAIN model, the app, whether you said yes when she asked, and her reply, in the Discord.
- Steam game updates or bringing back a minimized game don't work: these are hit and miss right now. Do them yourself.
- "I haven't done that yet, choom.": tell her exactly what to open or click.

### Updates
- "Windows couldn't verify the update signature. It won't run.": download the latest Capri-Setup.exe from the website and install it. Don't try to get around the check. The cause isn't known yet, so tell us your Windows and Capri versions in the Discord.
- "Couldn't check for updates." or "Couldn't download the update.": check your internet. She tries again in 3 hours.
- Old version won't update: install the latest Capri-Setup.exe over it.

### Still stuck?
Right-click her > Help > Copy error details, then ask in the Discord: https://discord.gg/F4fDHfGtFk

---

## 24. Needs checking
Things in this file the team should confirm before the support chat relies on them:
1. Mac data folder. Written as ~/.config/Capri (the code uses .NET's ApplicationData folder; Linux confirmed by a user screenshot, Mac not checked).
2. The ElevenLabs permissions for a restricted key (from the old setup guide screenshot: Text to Speech and Models Access, ElevenAgents and Voices Read, ElevenAgents Write). Several testers still got "Key can't do that" with Restrict key off. Cause unknown: older versions said it for every refusal. Newer versions show ElevenLabs' own reason, so the next report should name it.
3. How many credits ElevenLabs voice chat uses (ElevenLabs bills agent minutes differently from text to speech). The free plan's credit amount isn't stated on purpose.
4. Voice chat in other languages hasn't been tested with a real key.
5. Whether a browser playing YouTube or Spotify Web counts as music for dancing.
6. Wander across more than one monitor (one tester with 3 monitors said she never wandered).
7. "Mute my mic" hasn't been tested live with Discord open. It won't work if the user changed Discord's keybinds.
8. Mac and Linux haven't been launched on a real Mac or Linux desktop by the team: music detection, the "Capri wants to control Spotify" prompt and ElevenLabs playback are untested there.
9. Linux blank replies (Mint, Ollama 0.31.1) and whole-PC freezes: no known cause or fix yet.
10. The RAM message says "pick a smaller brain under BRAIN", but the built-in brain has no smaller pick. The help tells built-in users to use the free NVIDIA key instead.
11. The Mainframe at 150% display scale on 0.7.0: only the 0.6.2 workaround (100% scale) is known.
12. "Windows couldn't verify the update signature" (seen updating from 0.6.1): cause not confirmed. Manual install is the known fix.
13. "Can't open your speakers." on 0.6.2: the default-output-and-restart fix didn't work for everyone, and the underlying Windows error wasn't shown then.
14. <unused49> replies: the CPU retry in newer builds wasn't confirmed on the reporting user's PC.
15. Free voice chat with a real mic on a fresh PC.
16. Your own voice models under CUSTOM LOCAL and CLOUD VOICE (0.7.6): their servers were only tested against stand-ins. Fish Audio's cloud wasn't tried here with a real key, though a play-tester said it works great (29 Oct 2026), and a real Fish Speech, Kokoro-FastAPI, openedai-speech, AllTalk, LocalAI or vLLM-Omni Qwen3-TTS server hasn't been run with it. The start commands come from their projects' pages and weren't run here. Voice model files were run with real Piper, Kokoro and Pocket TTS models.
17. The mini player's heart hasn't been pressed on a real Spotify. Spotify's heart button names ("Add to Liked Songs" and the rest) and its Alt + Shift + B Like key come from Spotify's pages. The player's other buttons and what it shows were tested against a stand-in player in Windows' media session, not Spotify or YouTube themselves.

## Fan work and voices
- Capri is an unofficial fan project and is not approved or endorsed by CD PROJEKT RED, the anime's rights holders or its voice actors.
- Our project-provided synthetic voice is in Capri's Mainframe and runs on your PC.
- You may connect third-party text-to-speech services, but we don't provide guides for voice cloning tools. Our only voice-design help is a text prompt for making a synthetic voice.
- Only use voices you have the necessary rights and permissions to use, and follow the provider's terms. We do not endorse unauthorized cloning or impersonation of voice actors or other individuals.
- Capri prohibits cloning or impersonating a living person's voice without explicit, informed consent or other valid authorization. Her HD voice ships no clip of its own: it copies only a clip the user picks.
