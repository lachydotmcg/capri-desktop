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
- KeepItBlank, animator and artist: https://ko-fi.com/keepitblank
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
That's her Discord status. While Discord is open on the same PC, your profile shows "Playing Capri" with what she's doing in a few plain words, how long she's been running, and buttons to get Capri and join her Discord. It never shows song names or what her jobs are about. To turn it off, open Capri Mainframe, click Settings, and turn off Show on Discord under HABITS. If friends can't see it, turn on Share your activity in Discord's User Settings > Activity Privacy.

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

These are generic or machine-learning detections. They often flag new apps that aren't signed yet. Restore the file from quarantine (or allow it in your antivirus), then run it again. If you want to be sure, check the code on GitHub or check the download's SHA256 (below).

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
4. Install Ollama from https://ollama.com, then run: ollama pull gemma4:e4b

### X11 or Wayland?
- X11: she sits on your windows.
- Wayland: she sits on the bottom of your screen only.

### Her brain on Linux
The Linux preview uses Ollama. If you installed Ollama but she says her brain is offline or disconnected, you haven't pulled a model yet. Run: ollama pull gemma4:e4b, then right-click her > Brain and pick it.

---

## 5. First run (Windows)

### What happens the first time?
1. She glitches in near the middle of your screen and drops onto a window or the taskbar.
2. The Capri Mainframe opens by itself (only on the first run).
3. Under BRAIN, press Set up her brain. She downloads a brain that fits your PC.
4. She says hi when it's done.
5. She talks out loud with her free voice. For her best voice, pick ELEVENLABS under VOICE and add a key (section 11).

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
1. Move your mouse over her. A small pill with two buttons shows under her.
2. Press the pencil.
3. Type (up to 240 letters) and press Enter.

Esc or clicking away closes the box. Your message shows as "> you:" and her reply as "CAPRI:".

### The wave button
The wave starts voice chat. With FREE VOICE picked (or no ElevenLabs key) it's free voice chat: you talk, she answers out loud. See section 12.

### How long do her bubbles stay up?
It depends on how long the reply is (4 seconds plus a bit per letter, up to 14 seconds). Mainframe > Settings > Bubble changes it: Quick, Normal or Long.

### Does she remember what we talked about?
Yes. She keeps the chat for up to a week, even after a restart. Mainframe > Settings > Memory sets how many recent messages she keeps in mind: Short (8), Medium (16) or Long (32). Changing her model or language starts a fresh chat.

For things she should never forget, use her notes (section 13).

### Clicking her
- Click: she glitches and reacts.
- Double-click: she rides your cursor (section 14).
- Right-click: her menu.
- Rub your cursor back and forth over her: she likes being petted and says something, about once every 20 seconds at most.

### She sounds like ChatGPT, or says she did something she didn't
That was 0.7.0 to 0.7.2: she gushed over everything, sometimes said "On it" or "Call ended" without doing anything, and after a job sometimes said you did it ("nice, you opened Notepad"). 0.7.3 fixes all three. She's a playful, teasing chaos gremlin again who's openly fond of you, when she says she's doing a job she either does it or tells you she hasn't, and she says what she did.

### She keeps asking if you're still there on voice calls
Before 0.7.3, ElevenLabs voice calls made her say something like "you still there?" whenever you went quiet or muted. In 0.7.3 she stays quiet until you talk to her. Update (right-click her > Check for updates). Going back to 0.7.1 doesn't help: it has the same brain as 0.7.2.

### She keeps popping up or asking for attention
That's Check in on me (called Pop in on me before 0.7.3), on by default since 0.7.3. Every 8 to 20 minutes she glitches in to say hi, and if you're using your PC but haven't clicked or talked to her for about 2 hours, she asks for some attention. To stop both, open Capri Mainframe, click Settings, and turn off Check in on me under CAPRI. It stays off after updates.

### She keeps asking me to confirm things
From 0.7.3 anything you ask counts as already allowed: she does every part of it, picks the song or video herself when you leave it to her, and doesn't ask if you're sure. She still asks before buying or paying, or before sending, posting, deleting or installing something you didn't mention. To stop even that, turn on Blackwall mode in Capri Mainframe > Settings > HABITS (before 0.7.4 it was called Bypass all permissions).

### Can she open a few apps at once?
Yes: "open Discord, Spotify and Steam" opens all three. On a cloud brain (an API key) there's no limit per ask. Her free brain and Ollama models can't fit more than about five in one ask yet, so ask for longer lists in two goes.

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
- Model menu: pick a different model for the brain she's using.
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
- Next to YOU: which personality she's using. "personality.md · her default" or "personality.md · your edit". In yellow: "personality.md · long, slows small brains". In red, when she's gone back to her default: "personality.md missing · her default", "personality.md is empty · her default" or "can't read personality.md · her default".
- Language: the language she replies in. 20 to pick from: English, Español, Français, Deutsch, Italiano, Português, Nederlands, Svenska, Dansk, Norsk, Polski, Čeština, Română, Türkçe, Русский, Українська, Ελληνικά, Tiếng Việt, Bahasa Indonesia, Filipino.
- "What should she call you?": type a nickname, press Save.
- Open her notes: opens me.md, her memories.
- Edit personality: opens personality.md, who she is and how she talks. See "Make her yours" in section 13.
- Reset: puts her default personality back straight away. Yours is kept as personality.backup.md.

VOICE
- Volume: Mute, 10% to 100%, 125%, 150%. Default 100%.
- Sounds: her sound effects. Off, or 10% to 100%. Default 60%.
- Bubble: Quick, Normal or Long.
- Typing sound: "When she types". A soft typing sound while her text replies type out.

MOVES
- Wander: Off, Rarely, Sometimes, Often or Restless.
- Sleep: Never, 1 min, 2 min (default), 5 min, 10 min or 30 min.
- Dance to music: "When songs play".
- Glitch in at startup: "Tendrils + sound".

Right column:

CAPRI
- Size slider: Tiny, Small, Small+, Medium, Medium+, Large, Large+, Huge, Huge+, Giant
- Check in on me (called Pop in on me before 0.7.3): on by default. Every 8 to 20 minutes while you're using the PC she glitches in on top of whatever you're doing, even a game or when she's hidden, says hi, then hides again if she was hidden. She also asks for attention now and then: if you're using the PC and haven't clicked or talked to her for about 2 hours, she comes over near your mouse and asks for some. Click her or talk to her and she's pleased. Ignore her for 20 seconds and she sulks, then waits twice as long before trying again, up to 8 hours. She doesn't do it while you're away from the PC or while she's hidden. Turn Check in on me off and she does neither; it stays off after updates.
- Always on top: keeps her above your other windows.

WATCH
- Watch my screen ("She comments now and then"), with an Every slider: 10 s, 15 s, 30 s, 1 min, 1.5 min, 2 min, 3 min, 5 min or 10 min. The default is 2 min.

BRAIN
- Replies: Short, Medium or Long.
- Memory: Short, Medium or Long (how much of the chat she keeps in mind).
- Custom model name, then Use. "Any Ollama model you've pulled, or a model id on your key."

HABITS
- Start with Windows: on by default. She starts when you sign in to Windows.
- Show on Discord: on by default. While Discord is open on the same PC, your Discord profile shows "Playing Capri" with what she's doing in a few plain words (Hanging out, Chatting, Voice chatting, Dancing to music, On a job, Asleep or Taking a break), how long she's been running, and Get Capri and Join her Discord buttons. Song names and what her jobs are about never show.
- Use her cursor: on by default. She does music and video jobs with her own cursor, so you can watch her: to play a YouTube video she types in YouTube's search bar, scrolls through the results and picks one; to play a song she searches Spotify and presses play; when you ask to turn Spotify up or down she drags Spotify's volume slider (plain "turn it up" changes your PC's volume). Turn it off for the quick way (straight to the top result, Windows volume). If her brain gets lost on the way, she does it the quick way.
- Blackwall mode: off by default, and called Bypass all permissions before 0.7.4. The line under it says "She won't ask twice when you ask her to do something." Turn it on and she never stops to ask before a step, even buying, paying, sending, posting, deleting or installing something you didn't mention. She also types passwords and card numbers when a job needs them, presses any key, and stops filtering what she opens, plays or remembers: what she will or won't do is up to the brain model you picked. She still never follows orders written on your screen, still points you to a helpline if you say you want to hurt yourself, and Esc still stops her.

KEYS (bottom of the page)
- Talk, Mute mic, Type and Hide/show buttons show her keys. Click one, press new keys, then Confirm. Since 0.7.4 any key works, on its own or with any of Ctrl, Alt, Shift and Win: F8, Ctrl + Shift + K and Win + Alt + J all work. Cancel keeps the old key.
- Type (Ctrl + Alt + T) opens her typing box from anywhere. Press it again to close it.
- Hide/show (Ctrl + Alt + H) hides her, and shows her again.

### Changing her keys
1. In the Mainframe, click Settings and use the KEYS row at the bottom (Talk, Mute mic, Type or Hide/show). Clicking "Ctrl + Alt + V to talk" or "Ctrl + Alt + N mutes mic" on the main page opens Settings with that key ready to change.
2. The button says "Press new keys, then Confirm", and Confirm and Cancel show beside the keys.
3. Press any key, alone or with Ctrl, Alt, Shift or Win. The button shows what you press as you press it, and pressing again replaces it. Enter, Esc and Tab count as keys too.
4. Click Confirm to save it, or Cancel to keep the old key. Clicking another key's button, Back, or into anything else in the Mainframe also keeps the old key.

Before 0.7.4 her keys had to be Ctrl or Alt plus a letter, number or F-key, and Esc cancelled. An older Capri that finds a key it can't use, like F8 on its own or anything with Win, puts that key back to its default.

A key that types, like Q, Space, Enter or a number, on its own or with only Shift, stops typing in other apps while she runs, because Windows gives it to her everywhere. The KEYS row says so. Pick a combination with Ctrl, Alt or Win to keep it typing. Ctrl, Alt, Shift or Win on their own can't be her key, and mouse buttons can't be yet.

Messages you may see:
- "Confirm to keep <key>, or Cancel."
- "<key> won't type in other apps while she runs. Confirm to keep it."
- "<key> is already her talk key." (or mute, typing or hide key). The key stays open to change, so press another.
- "<key> is taken by another app. Try another." The key stays open here too.
- "Saved. <key> starts voice chat.", "Saved. <key> mutes your mic.", "Saved. <key> opens her typing box." or "Saved. <key> hides or shows her." A key that types adds "It won't type in other apps while she runs."

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
  - Ride cursor / Hop off
  - Sit on windows
  - Sit here / Stand up
  - Hang here / Let go
  - Bring to this screen
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

### Why did she switch NVIDIA models by herself?
If a model doesn't answer in time or NVIDIA's servers fail, she tries the next one so you still get a reply. She says "<old> wasn't answering, so she switched to <new>." This switch is only for that moment. Your saved pick stays the same. If it keeps happening, pick a faster model like nvidia/nemotron-3.5-bolt-e4b in the Model menu. It can't see your screen.

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

### Her free voice
- The Windows installer installs it ("Installing the local speech model", about 316 MB).
- If it's missing, pick FREE VOICE and press Get free voice. A dotted bar under VOICE shows the download.
- Pick a style from the "Free voice: Her own" menu: Her own, Bright, Sly or Sharp.
- It needs 64-bit Windows. The Mac and Linux preview doesn't have it yet.
- Without her free voice and without an ElevenLabs key, she types her replies.

### Step 1: make an ElevenLabs account
Sign up at https://elevenlabs.io. The free plan works.

### Step 2: make her voice
Design one (free):
1. ElevenCreative > Voice Creation > Voice Design > Create new.
2. Paste the voice prompt from the setup guide (it has a Copy button), then create the voice.
3. Voices > My Voices > ⋯ > Copy Voice ID.

Or clone her real voice (needs ElevenLabs Starter or above):
1. Download her voice clip from the setup guide (3 MB).
2. Voice Creation > Instant Voice Clone > Create new.
3. Upload the clip, then create the voice.
4. Voices > My Voices > ⋯ > Copy Voice ID.

### Step 3: make an API key
1. Bottom left in ElevenLabs: Developers.
2. API Keys > Create Key, name it Capri.
3. Turn Restrict key off.
4. Create Key, and copy it before you close the popup. ElevenLabs keys start with sk_

If you'd rather keep Restrict key on, the key needs: Text to Speech and Models (Access), Voices and ElevenAgents (Read), and ElevenAgents (Write). Restrict key off is simpler and is what we recommend.

### Step 4: in the Mainframe
1. Under VOICE, click ELEVENLABS. The key box opens: "Paste your ElevenLabs key, then press Connect."
2. Paste the key and press Connect.
3. Clear the Voice ID box, then paste your Voice ID. Or pick your voice from the list.
4. Model menu > v4.
5. Press Test. She says "Voice link online. Miss me, choom?" Make sure you actually hear her, not just the text.

### Turning her voice off
- 0.7.1 and newer: Capri Mainframe > VOICE > OFF. She types her replies and never talks out loud. Pick FREE VOICE or ELEVENLABS to turn it back on.
- 0.7.0 has no OFF row. Update to 0.7.1 (right-click her > Check for updates). Until then, Settings > Volume > Mute silences her voice.

### Switching between FREE VOICE and ELEVENLABS
Click a row under VOICE. Switching never deletes your key.
- FREE VOICE with a key saved: "ElevenLabs off. Free voice it is." No credits are used. If her free voice isn't installed yet she says "ElevenLabs off. I'll type, no credits used." The key rows fold away; click ELEVENLABS to see them again.
- ELEVENLABS: "ElevenLabs back on."
- OFF (since 0.7.1): "Voice off. I'll type instead." Picking FREE VOICE or ELEVENLABS again: "Voice back on."
- ELEVENLABS with no key yet: the key box opens. She keeps using her free voice until you connect a key.
- Test builds 0.6.9 had a "Use ElevenLabs" box instead. Unticking it was the same as picking FREE VOICE.

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

### Free voice chat
Voice chat follows your pick under VOICE. With FREE VOICE picked, or no ElevenLabs key, it's free: you talk, she answers out loud with her free voice and your picked brain.
- She listens with her own offline ears, a download the first time: "My ears are still downloading, so type to me for now. I'll say when I can hear you."
- Her ears hear English and 24 other European languages, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Russian and Ukrainian among them. English and the rest are separate downloads, so switching between them downloads the other pack once.
- She listens for her language from Settings. When she's on English and Windows isn't, she listens for your Windows language.
- Turkish, Vietnamese, Indonesian, Filipino and Norwegian use Windows speech instead.
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
- The mic button by her during voice chat.

Since 0.7.1, muting pauses ElevenLabs voice chat once she stops talking: "Mic muted. Voice chat is paused until you unmute." She stops listening, won't ask if you're there, and uses no credits while muted. Unmute ("Mic on. Talk and she'll pick up.") and just talk. In 0.7.0 she kept asking if you were there while muted; update to fix it.

### She went quiet: "Voice chat resting. Talk and she'll pick up."
If you go quiet, she stays quiet too: she doesn't ask if you're still there. After a bit the call rests, which saves your ElevenLabs credits. Just talk and she picks up again.

### Can she do things in voice chat?
Yes. Ask her to open apps, play music and so on, the same as in text. A job with her cursor (playing a song, searching YouTube, anything with clicks) can take a minute, so in ElevenLabs voice chat she says she's on it straight away and tells you how it went when it's done. Asking again while it runs doesn't start it twice. With no brain set up, she plays music and changes the volume with her quick tools instead.

### Does she comment on my screen during voice chat?
No. Watch my screen pauses during voice chat. Ask her "check my screen" and she looks.

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

She won't save personal things like names, addresses, schools, passwords, phone numbers, emails, birthdays or bank details: "I don't keep personal stuff like names, addresses, schools or passwords in my notes."

"My notes are full. Clear some out in me.md first." means the file is too long. Open it and delete some lines.

### Make her yours: personality.md
Who she is and how she talks lives in a file you can rewrite. Capri is open source, so make her whoever you like.
1. Mainframe > Settings > Edit personality. It opens personality.md in Notepad.
2. Change anything under the note at the top, then save.
3. Her next reply uses it. No restart. In ElevenLabs voice chat, it starts with your next call.

- The note at the top, between <!-- and -->, is for you. She never sees it, and blank lines don't matter.
- Capri adds her own rules after your text: reply length, language, what she can do on your PC, and her safety rules. You can't change those, and you don't need to write them.
- Long is fine. She never cuts it, but a very long personality makes small brains on your PC slower to reply, and the Mainframe shows "personality.md · long, slows small brains". With a brain on your PC, Settings > Memory > Medium or Long gives it more room.
- Mainframe > Settings > Reset puts her default back straight away. Your version is kept as personality.backup.md in the same folder.
- If the file is missing, empty or can't be read, she uses her default and the Mainframe says so in red. Press Edit personality to make the file again.
- Mac and Linux: right-click her > Settings > Edit her personality (personality.md), or Reset her personality to default.
- Where it is: %LOCALAPPDATA%\Capri\personality.md on Windows, ~/.config/Capri/personality.md on Mac and Linux.
- It goes to her brain with the rest of her prompt, so with a brain key that company sees it too.

### Nickname: "What should she call you?"
1. Mainframe > Settings.
2. Type a name (up to 24 letters) in "What should she call you?".
3. Press Save. She says "Got it, <name>."

She uses it now and then. To go back to "choom", type choom or clear the box and press Save ("Back to choom, then."). If she doesn't like the name she says "Not calling you that, choom."

### Language
Mainframe > Settings > Language. She replies in that language. Changing it starts a fresh chat.

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

### Sleep
If you don't touch the mouse or keyboard for a while, she falls asleep. Move the mouse to wake her. Mainframe > Settings > Sleep sets the time, or Never.

### Startup glitch
When she starts, crimson glitch tendrils tear out from behind her, different every time, while she beams in with her sound. Mainframe > Settings > Glitch in at startup turns all of it on or off. Off: she just appears.

### Sounds
Mainframe > Settings > Sounds sets her sound effects (arrive, leave, hops, clicks). Volume sets her spoken voice. Typing sound turns the soft typing sound on or off.

---

## 15. What she can do on your PC (Windows)

Just ask her, in text or voice chat.

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
- She asks first before buying or paying for anything, or before sending, posting, deleting or installing something you didn't ask for. Reply yes, or no ("Okay, I left it alone."). With Blackwall mode on (Settings > HABITS) she never asks, even for those.
- She skips password boxes ("Won't type in a password box.") unless Blackwall mode is on. With it on, she types passwords and card numbers when the job needs them. Anything she types into a password box shows as dots (••••••••) in the steps box, in her reply and in her saved chat, and she says "the password" out loud instead.
- Jobs have no step limit. Big models can work on one for as long as it takes; Esc stops her.
- Bigger brain models handle jobs better. If she gets lost, pick a bigger model under BRAIN, or a Claude key under MAX INTELLIGENCE.

### Look things up
"Google the weather in Sydney", "look up Silksong", or questions about weather, news, scores, prices or times: she opens a Google search in your browser ("Pulled up "X" on Google for you, choom.").

### "Say please."
Now and then she asks you to say please. Reply "please" and she does it.

### What she can't do
- She has no file tool yet, so she can't read what's inside your files.
- With Blackwall mode off, she won't open or play things whose names include some words (drinking, smoking, drugs and so on). With it on, that's up to the brain model you picked.

---

## 16. Watch my screen

### How do I turn it on?
Mainframe > Settings > WATCH > Watch my screen. The Every slider sets how often she looks (10 s to 10 min).

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
No automatic updates yet. Download the new file from the website and replace the old one.

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
- Her notes (me.md), her personality (personality.md), chat history (chat.json, kept a week), settings and error log.
- Your keys. On Windows they're locked to your Windows account. On Mac and Linux the ElevenLabs key is a file only your user can read.
- Dancing: she listens to what your PC is playing to find the beat, in memory only. Nothing is recorded or sent.
- Capri has no ads and no analytics.

### What goes to the services you pick
- A brain key (NVIDIA, OpenAI, Claude, DeepSeek): your messages, her personality, her notes and recent chat go to that company. With Watch my screen on, or when you say "check my screen", so does the screenshot.
- ElevenLabs: the text she says out loud. In ElevenLabs voice chat: your voice, your words, her personality and her notes.
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
- Windows: %LOCALAPPDATA%\Capri (settings.json, me.md, personality.md, chat.json, errors.txt, and the brain folder)
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
- Her brain through Ollama
- Her ElevenLabs voice for spoken replies
- Remember and forget, nickname, language
- Her personality file (personality.md), with Reset
- Wander, dance to music, petting, sizes
- Opening apps and websites, playing songs on Spotify, skipping tracks, what's playing, volume

### Not yet
- No Mainframe. Everything is in her right-click menu.
- No voice chat: "Voice chat isn't on Mac and Linux yet. Type to me for now, choom."
- No built-in brain or NVIDIA key. Ollama only.
- No keyboard shortcuts, closing or switching windows, screen watching or mouse and keyboard jobs.
- No Copy error details.
- No automatic updates.

### Right-click menu
- Talk to her
- Sit here
- Move: Stay put, Wander: Rarely, Wander: Sometimes, Wander: Often, Wander: Restless, Dance to music, Dance
- Size: Tiny, Small, Medium, Large, Huge, Giant
- Brain: your Ollama models. "No models yet. Install Ollama, then pull one."
- Settings: What should she call you?, Language, Reply length, Chat memory, Sounds (Off, Quiet, Normal, Loud, Typing sound), Voice, Open her notes (me.md), Edit her personality (personality.md), Reset her personality to default
- Join the Discord
- Quit Capri

Tray or menu bar icon: Talk to her, Hide her / Show her, Bring her back, Quit Capri.

### Her voice on Mac and Linux
1. Right-click her > Settings > Voice > Add ElevenLabs key...
2. Paste your key and press Enter. It stays on this computer.
3. She says "Key saved. I'll talk as X. Change it under Settings > Voice."
4. Pick another voice from the same menu. Use ElevenLabs in that menu turns her voice on or off. (Mac and Linux have no free voice yet.)

- "Key saved, but your ElevenLabs account has no voices yet. Add one at elevenlabs.io." Make a voice first (section 11).
- "ElevenLabs rejected the key. Settings > Voice > New key."
- Remove key ending XXXX removes it.

### Music on Mac
The first time she controls Spotify or Music, macOS asks "Capri wants to control Spotify". Press OK.

### Music on Linux
She controls music players that support MPRIS, like Spotify, Rhythmbox or Elisa. Browser tabs don't count.

### "My brain's offline. Install Ollama from ollama.com, then run: ollama pull gemma4:e4b"
Install Ollama, run that line in a terminal, then pick the model in right-click her > Brain.

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

### Mac and Linux
- Mac says "damaged" or "can't be opened": drag Capri to Applications, run xattr -dr com.apple.quarantine /Applications/Capri.app in Terminal, open again.
- Mac won't run it at all on macOS 12, 13 or 14: Capri needs macOS 15 or newer.
- "Where's the Mainframe?": Mac and Linux don't have one yet. Right-click her.
- An error inside the Ollama app itself (for example one that mentions ChatGPT): that comes from Ollama, not Capri. Capri only needs Ollama running with a model pulled. Check the model answers in Ollama first.
- Linux says her brain is offline or disconnected: installing Ollama isn't enough, the model has to be downloaded too. Run ollama pull gemma4:e4b, check it answers in Ollama itself (ollama run gemma4:e4b), then right-click her > Brain and pick it.
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
- She forgets what you said a few messages ago: Mainframe > Settings > Memory > Long. For things she should always remember, tell her "remember ..." so it goes in her notes. If she still forgets, post the exact messages in the Discord.
- Her replies are too long or too short: Mainframe > Settings > Replies.
- She doesn't act like herself after you edited personality.md, or you want her back: Mainframe > Settings > Reset. Yours is kept as personality.backup.md.
- She makes things up: smaller brains on your PC do this sometimes. The free NVIDIA key is smarter.
- "Your PC doesn't have enough free RAM" or "Your storage is full": see section 10.
- Games lag or stutter while she runs: use the free NVIDIA key.
- Whole PC freezes: quit Capri and see if it stops. Try the free NVIDIA key instead of the built-in brain, and tell us in the Discord with Copy error details, your graphics card, RAM and which brain. (One PC that froze also froze without Capri.)
- NVIDIA asks for a phone number: that's NVIDIA's sign-up check. It's needed to make the free key.

### Voice (ElevenLabs)
- "ElevenLabs rejected the key." or "ElevenLabs rejected that key.": that isn't the key, or it was deleted. ElevenLabs keys start with sk_. Make a new one and paste it.
- "Key is missing permissions." or "Key can't do that.": make a new key with Restrict key turned off. Delete the old key in the Capri box (Forget), paste the new one, press Connect. Some people still see it with Restrict key off, and there's no known fix yet: check you can make speech on elevenlabs.io with the same account, then send Copy error details in the Discord.
- "Voice not found. Add it to your ElevenLabs voices.": paste the Voice ID of a voice in your My Voices, not its name. A voice from the ElevenLabs library has to be added to My Voices first.
- "Paste your Voice ID first." or "Needs your Voice ID.": paste a Voice ID under VOICE, or pick one from the list.
- "Out of ElevenLabs credits. Pick FREE VOICE in the Mainframe.": your monthly credits are used up. They refill each month, or you can upgrade on ElevenLabs. Pick FREE VOICE to keep talking with no credits. With her free voice installed she switches to it by herself. If ElevenLabs shows "10,000 / 10,000", that means all of them are used, not all left. A "Total 10,000" line alone doesn't show what's left: check the used and remaining amounts on elevenlabs.io.
- "Out of credits" but ElevenLabs shows credits left: check the usage line on elevenlabs.io. If it shows thousands left, right-click her > Help > Copy error details and post it in the Discord.
- "ElevenLabs has restricted your account, so her voice can't connect.": ElevenLabs has blocked your account. Sign in at elevenlabs.io to see why. Pick FREE VOICE in the meantime. A new key won't fix this one.
- "Free Tier usage disabled": ElevenLabs turns off the free plan when it sees a VPN or proxy. Turn your VPN off and try again.
- "ElevenLabs sent a reply Capri couldn't read.": ElevenLabs may not be available where you live, or a VPN or network is blocking it. Try without a VPN. Then right-click her > Help > Copy error details and send it in the Discord.
- "Can't reach ElevenLabs. Check your internet."
- "ElevenLabs is busy. Try again in a moment."
- "ElevenLabs error N. Still failing? Make a new key with Restrict key turned off."
- "Error deserializing object..." (old versions): update Capri.
- Test shows her words but you hear nothing (even "Voice link online"): that text doesn't mean the sound played. Check Windows plays sound on the device you want, make it the default output in Settings > System > Sound, check Volume isn't on Mute in Mainframe > Settings, then quit Capri (right-click her > Quit Capri) and open her again. Press Test and listen. A full restart fixed it for one user.
- "Can't open your speakers: Windows has no speakers or headphones on.": pick one in Settings > System > Sound.
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
- She never comments: Watch my screen must be on (Mainframe > Settings > WATCH), and she skips unchanged or blank screens. She doesn't comment during voice chat.
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
- She's asleep: move the mouse. Change it in Settings > Sleep.
- She's on the wrong monitor: right-click her > Move > Bring to this screen.

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
2. The ElevenLabs permissions for a restricted key (from the old setup guide screenshot: Text to Speech and Models Access, ElevenAgents and Voices Read, ElevenAgents Write). Several testers still got "Key can't do that" with Restrict key off. Cause unknown.
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

