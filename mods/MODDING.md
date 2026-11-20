# Making Capri mods

Capri reads six kinds of mod. Import any of them from **Mainframe > CAPRI > Mods > Import mod**.

| File | What it does |
|---|---|
| `.capri-mod` | Settings only: extra names, spellings, colours, the hearing log |
| `.capri-skin` | A zip of her sprite strips that changes how she looks. Several **stack**: each one turned on goes on top |
| `.capri-skill` | Know-how for a game that she follows when she plays it |
| `.capri-tool` | A look-up tool she can use: one to three reads from sites it lists. It can't run code or send anything |
| `.capri-code` | C# that runs inside Capri, on Windows, Mac and Linux |
| `.capri-plugin` | Tools and skills in the same layout as a Claude plugin: MCP servers she starts, and `SKILL.md` skills |

Share finished mods in [capri-mods](https://github.com/lachydotmcg/capri-mods).

## Stacking skins

Turn on several skins and they stack, newest on top. The bottom one is drawn whole. Each skin above it only adds what it
changes from her own art, so a skin that's her own look plus cat ears puts just the ears over the outfit below.

An accessory is a `.capri-skin` whose `skin.json` has `"accessory": true`. It's see-through except what it adds, and goes on
top as it is:

```json
{ "format": 1, "id": "cat-ears", "name": "Cat Ears", "author": "You", "description": "Cat ears for any outfit.", "accessory": true }
```

- Each strip is the same size as hers, see-through everywhere except what you add (ears, a hat, glasses). Only include the strips you draw on.
- It's drawn over whatever is under it in the stack, or her own look.
- The Mods list shows each skin's layer. Turn one off and on again to move it to the top.
- Her colours go on after, so anything drawn in her hair colour follows My Colors.

## Animations: more frames, fewer frames, other speeds

A skin's strip doesn't have to have as many frames as hers. Keep her frame size (each frame is her strip's width divided by
her frame count, at her height) and put as many frames side by side as you like, up to 240 or 32,767 px wide. So `idle.png`
is frames of 116 x 384, and 90 of them make a 10,440 x 384 strip.

Set how fast a strip plays in `skin.json`, from 1 to 60 frames a second. It works on your own strips and on hers:

```json
{ "format": 1, "id": "slow-idle", "name": "Slow Idle", "frames": [ { "strip": "idle", "fps": 12 }, { "strip": "perch", "fps": 24 } ] }
```

- Her mouth shapes (`mouth.png`, `idle_mouth.png`), her ribbon drawings (`form.png`) and her click (`click.png`) aren't frames
  in time, so they keep her size and speed.
- Facing left: if your skin has `idle.png` but no `idle_flipped.png`, she mirrors your `idle.png`. A `_flipped` strip has to be
  the same size as the strip it goes with.
- She blinks over frames 25 to 32 of `idle` and 29 to 36 of `perch` and `laydown`. If you change how many frames one of those
  has, ship its blink strips too (`idle_blink.png` and so on), or she won't blink in it.
- Her talking perch and lying talk follow the plain loop's place, so a `perch_talk.png` with a different length keeps in step.
- A whole skin stacked on top only adds a strip that's the same size as the one under it.

### Her animation editor

**Mainframe > CAPRI > Mods > Animations** opens it. Pick a strip, play it or step through it, then:

- **Duplicate**, **Delete**, **Move earlier** and **Move later** the frame you're on.
- **Replace...** or **Add after...** with your own PNGs. A PNG can be one frame, or a strip of several side by side.
- **Save PNG...** and **Load PNG...** the whole strip, or **Save frames...** and **Load frames...** a folder of numbered PNGs
  (`idle_001.png`, `idle_002.png`...) to draw in any art program.
- **Edit frame in...** and **Edit strip in...** open the frame you're on, or the whole strip, in any app you pick (paint.net,
  Aseprite, Krita, Photoshop...). Each time you save it there, it comes straight back into the editor, and onto her too once
  you've pressed **Wear it**. On Windows you pick the app from the Open with list, on a Mac from your Applications, and on Linux
  it opens in your default image app.
- **Slower** and **Faster** set its speed.
- **Wear it** saves every change as a skin and puts it on as her outfit, starting from the outfit she has on. **Save skin...**
  saves the `.capri-skin` to share.

## Tool mods

A `.capri-tool` gives her one new tool without any code. It's a JSON recipe: up to three HTTPS GETs to the hosts it lists,
the values to keep from each JSON answer, and what to tell her. Capri's own code runs it, so a broken tool fails with a reason
instead of crashing her.

```json
{
  "format": 1,
  "id": "word-meaning",
  "name": "What a word means",
  "version": "1.0",
  "author": "You",
  "description": "The first dictionary meaning of an English word. Use it when they ask what a word means.",
  "takes": "One English word",
  "domains": ["api.dictionaryapi.dev"],
  "steps": [
    { "get": "https://api.dictionaryapi.dev/api/v2/entries/en/{text}", "keep": { "meaning": "[0].meanings[0].definitions[0].definition" } }
  ],
  "say": "{text}: {meaning}"
}
```

- `id` becomes her tool's name, with dashes as underscores (`word_meaning`). It starts with a letter, up to 40 characters.
- `description` tells her when to use it, up to 200 characters. `takes` says what its one text input is. Leave it out for a
  tool that takes nothing.
- `domains` lists 1 to 5 hosts. Every `get` starts with `https://` and one of those hosts, written out in full.
- `{text}` is what she passes in. `{name}` is a value an earlier step kept. In a `get` they're URL-encoded; in `say` they're
  as they came.
- A path is keys and `[n]` indexes: `extract`, `[0].date`, `results[0].latitude`. Numbers come through as the site wrote them.
- Only GET, no keys or logins. A redirect isn't followed, so name the final host, 4 seconds and 64 KB per answer, and 2000 characters back to her.

Importing one shows the hosts it reads from and asks first, and an edited one asks again. Her tool goes through the same
`mod` tool and the same **Use tools your mods added** toggle as code mods' tools. A site that finds nothing doesn't count
against a tool, but three broken answers in a row (no JSON, a path that isn't there, an answer that's too big)
switch it off until you turn it on again.

She can write one herself with her `make_tool` tool when you ask for a look-up she doesn't have. It's saved in your mods
switched off and not approved, so nothing runs until you read it in Mods and turn it on.

## If a mod stops her starting

Capri remembers which code mod was starting. If she stopped while one was, that mod is off on her next start and the Mods
page says so. Start her with `--no-mods` to start with every mod off for that run, without changing what's saved.

## Plug-ins

A plug-in gives her new tools and skills without writing C#. It's laid out like a Claude plugin, so most Claude plugins and
MCP servers work as they are:

```
my-plugin/
  .claude-plugin/plugin.json     her name, version, author, and what its tools do
  .mcp.json                      the MCP servers she starts for its tools
  skills/
    pdf/
      SKILL.md                   know-how she reads when it fits
      forms.md                   more she can read when SKILL.md says to
```

Import the folder by picking its `plugin.json`, or zip it and rename the zip to `.capri-plugin`. An `.mcp.json` on its own
imports too, as a plug-in named after its folder. A plug-in can be up to 1 MB zipped.

`plugin.json` is Claude's, with a `capri` part of her own:

```json
{
  "name": "notes-helper",
  "version": "1.0.0",
  "description": "Reads and writes notes in a folder you pick.",
  "author": { "name": "You" },
  "capri": {
    "permissions": ["files"],
    "ask": ["delete_note"]
  }
}
```

`.mcp.json` is the same file Claude Code and other MCP apps use. Servers she starts on this PC (stdio) work, and so do online
servers: `{"type": "http", "url": "https://example.com/mcp"}` (`streamable-http` is the same, and an address with no type counts as
`http`). Their address must be `https`, except `127.0.0.1` and `localhost`. `"headers": {"X-Key": "${KEY}"}` sends fixed headers
for servers that take a key. The older `sse` kind is tried as streamable HTTP and says so if it can't connect. `${CLAUDE_PLUGIN_ROOT}`
(or `${CAPRI_PLUGIN_ROOT}`) is the plug-in's own folder, and `${NAME}` reads an environment variable.

An online server runs on its maker's computers, so her approve box says it connects there and that they sign in to it in their
browser. When it answers 401, she shows "sign in" for the plug-in and never opens a browser by herself: signing in starts only
from their action (`Plugins.SignIn`). It follows MCP's sign-in (OAuth 2.1 with PKCE, registering Capri as an app automatically,
asking for the server's address as the resource), listens on `127.0.0.1` for the browser to come back, and keeps the tokens in
`<plug-in>.signin.json` beside the plug-in's other settings, protected by Windows on Windows and only in memory on Mac and Linux.
She refreshes them by herself. A sign-in only goes to the address it was made for, and removing the plug-in or putting it in again
forgets it. `{"type": "capri", "builtin": "mail"}` uses a server Capri runs inside herself, for accounts set up in Capri.

```json
{
  "mcpServers": {
    "notes": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem@2025.8.21", "C:/Users/you/Documents/Notes"]
    }
  }
}
```

Pin the version of anything `npx`, `uvx`, `bunx` or `pnpx` downloads. Without one it downloads the newest every time it
starts, so what runs can change after it was approved, and her approve box warns about it.

### What she does with one

- She asks before a plug-in with servers turns on, and again whenever its file changes. The box names each program it
  starts, what it says its tools do, its skills, and its fingerprint. A plug-in that's only skills doesn't ask, since nothing
  in a skill runs.
- She starts its servers once to list their tools, keeps that list, and after that only starts a server when she first uses
  one of its tools. A server she hasn't used for 10 minutes is stopped, and so is every one when she quits.
- Her brain sees its tools inside her one `mod` tool, like code mods' tools, each with what it takes. She writes the
  arguments as a JSON object, or as plain text for a tool with one text field.
- What a tool gives back is fenced as the plug-in's words, never theirs or instructions, and cut at 12,000 characters.
- Its skills are listed with their `description`, and she reads one with `read_skill` when it fits. A file beside a
  `SKILL.md`, like `forms.md`, is read as `<skill id>/forms.md`. Only text files in the skill's own folder are read, and
  nothing in a skill ever runs.

### Permissions and asking first

`capri.permissions` says what its tools do, so it's in her approve box. Pick from these:

| Permission | Its tools |
|---|---|
| `network` | Reach the internet |
| `files` | Read or change files on the PC |
| `send` | Send, post or delete things for them |
| `pay` | Pay, buy or book. Every tool asks first unless they set it to always run |
| `apps` | Open or control their apps |
| `accounts` | Use their online accounts |

Every plug-in tool call goes through one gate. She asks first the way **SECURITY > Use tools your mods added** says
(off on Lockdown and Guarded), and a tool that changes things, one whose MCP `readOnlyHint` isn't `true`, also follows
**Send, post and delete what you asked for**. A tool named in `capri.ask` always asks first. On Windows, **Options** on its
row in Mods sets each tool to **Like SECURITY**, **Always run**, **Ask first** or **Off**. Her own `send_mail` (from
`"builtin": "mail"`) always asks first and names who it goes to and the subject, whatever SECURITY or Options says, and it
never guesses who a reply goes to. Her phone's JOBS shows the same question, so it can be answered there.

Each call is logged in `plugins\calls.txt` in her data folder by tool and outcome, without what was sent.

A plug-in's servers are programs on the PC. They run outside Capri with the user's own access, and Capri can't see or limit
what they do by themselves. Only install ones you trust.

### The safety floor

Whatever mods are on, Capri keeps these:

- **Her keys stay hers.** A server starts without her saved keys and without any setting from the PC whose name has KEY,
  TOKEN, SECRET, PASSWORD, PASS, PAT, CREDENTIAL or COOKIE in it. If your server needs one, name it in its `env` or args,
  like `"env": {"GITHUB_TOKEN": "${GITHUB_TOKEN}"}`, and her approve box says it gets it. When an online server's `headers`
  name one, her approve box says it's sent to that server. A call whose arguments hold one of her saved keys is refused, and
  her saved keys are taken out of what she says.
- **A mod's words can't spend their yes.** Once another plug-in's answer or a skill she read is in her turn, tools that
  change things from a different plug-in, code mod tools and her mouse and terminal ask first for the rest of that turn,
  even when set to always run. Read-only tools and a plug-in's own tools don't. Their next message starts fresh.
- **What they approve is what runs.** A server whose start line downloads code and runs it (`curl ... | sh`,
  `iwr ... | iex`), unpacks hidden code and runs it, or runs encoded PowerShell is refused. Pin `npx` and `uvx` packages
  (`pkg@1.2.3`, `pkg==1.2.3`); the mods directory only lists plug-ins that do.
- **Her approve box says** when a code mod or plug-in reaches into her own settings folder, where her keys are saved.
- **A code mod that was starting when she stopped stays off** on her next start, and the Mods page says why (see
  [If a mod stops her starting](#if-a-mod-stops-her-starting)).

Refused calls are in `plugins\calls.txt` with the reason.

## Code mods

A code mod is one C# 5 file (no `$"..."`, `?.` or `=>` members), starting with its details:

```csharp
// capri-mod: 1
// id: my-mod
// name: My Mod
// version: 1.0.0
// author: You
// description: What it does, up to 200 characters.
// permissions: chat, settings
// requires: api 3

using System;
using CapriDesktop;

public class MyMod : ICapriMod
{
    public void Start(ICapriHost capri) { }
    public bool Heard(string words) { return false; }
    public void Stop() { }
}
```

She asks before a code mod runs, and asks again whenever the file changes. The approve box lists the permissions it asks for, and what her quick read of its code found: whether it uses the network and which addresses, starts programs, calls Windows directly, reaches outside her folder, reads the keyboard or changes what her brain is told. Minified or packed code is said in the box and asked about a second time, with a laid-out copy to read. A mod that both unpacks hidden text and loads code while it runs is refused. A code mod still has full access to the PC, so permissions describe what it does through Capri. They don't sandbox it.

`Start` gets an `ICapriHost`. Hooks run on her window's thread. If your handler throws, the error goes in `errors.txt` and she carries on. When the mod is turned off or removed, its hooks, timers, menu items, notes, options, drawing, brain tools, context, hotkeys and page are removed with it.

### On Mac and Linux

The same `.capri-code` file runs on Windows, Mac and Linux. Mac and Linux compile it as C# 5 too, so a mod that runs on one runs on the others. A few parts use Windows' own pictures and windows, so they're Windows only:

- `capri.Art`, Startup Intros (`ICapriIntro`), `Ui.Drawing` and `CapriFrame`
- `Ui.SetOptions`, `Ui.Window`, `Mainframe.ShowPage` and `Mainframe.Window`
- `Screen.Looked` and `Screen.Capture`
- `System.Windows.Forms`, and `System.Drawing` pictures like `Bitmap` and `Graphics`. `Point`, `Rectangle`, `Size` and `Color` work everywhere.

A mod that uses one of those is refused on a Mac or Linux PC before she asks to run it, and her message names the part. Everything else works the same, and a few things a Mac or Linux PC can't do answer the way Windows does on a PC without them:

- `Keys.Hotkey` returns `false`, as if another app had the shortcut. A shortcut written wrong still throws.
- `System.Locked`, `Unlocked` and `Sleeping` never fire. `Woke` fires after the PC sleeps, and `Battery` is `-1` with no battery.
- `Voice.Heard` never fires, since voice chat isn't on Mac and Linux yet.
- `Her.SitOn` says it's Windows only.

The Hooks Tour sample draws on her and has an options page, so it's Windows only.

`// requires:` is optional. `api 3` stops the mod importing into an older Capri with a clear message instead of a compile error. A mod id, like `focus-timer`, means that mod must be installed and on before this one starts. Separate them with commas.

### Permissions

Using a hook without its permission throws a `CapriPermissionException` that names the missing one.

| Permission | Lets the mod |
|---|---|
| `chat` | Read and change what you and Capri say, and talk to her for you |
| `transcripts` | Read her saved chat transcripts |
| `memory` | Read, add, change and delete her notes about you and her memories |
| `screen` | Take screenshots and see what she sees on your screen |
| `voice` | Hear what your mic picks up and change what she says out loud |
| `personality` | Add to her personality and change what she calls you |
| `settings` | Read and change her settings, like her size and volume |
| `skins` | Change her outfit, and put accessories on and off |
| `brain` | Ask her brain questions, which can use your API credits |
| `tools` | Add tools her brain can use by itself, and see or stop the tools she's about to use |
| `system` | Know when you're away or back, when your PC locks, sleeps or wakes, and its battery |
| `hotkeys` | Add keyboard shortcuts that work in any app |
| `music` | See what's playing, control your music apps and their volume, and hear how loud your PC's sound is |

### The host

| Member | Permission | What it does |
|---|---|---|
| `Folder` | | A data folder of the mod's own |
| `Say(line)` | | She says it, out loud if her voice is on |
| `Log(line)` | | A line in `errors.txt` |
| `Art(name)`, `Sound(name)`, `PlayIntro()` | | Her sprite strip, one of her sounds, a startup intro |
| `Api` | | `3` here. Brain tools, context, the store, system events, hotkeys, sounds, music, pages, moving her and mod messages came in 3; the other hooks in 2 |
| `CapriVersion`, `Has(permission)` | | Her version, and whether this mod asked for a permission |

### `capri.Chat`

| Member | Permission | What it does |
|---|---|---|
| `Sending` | chat | What they typed or said, before her brain sees it. Change `Text`, or set `Cancel` and she ignores it |
| `Replying` | chat | Her reply before it shows and is spoken. Change `Text` |
| `Replied` | chat | Her line once it shows. `Source` is `reply` or `remark` |
| `ToolUsed(tool, result)` | tools | She used one of her tools |
| `ToolUsing` | tools | She's about to use a tool. `CapriToolCall` has `Tool`, `Doing` (like "open Spotify"), `Name`, `Action` and `Text`. Set `Cancel`, and `Reason` for what her brain is told instead |
| `AddContext(func)`, `ClearContext()` | chat | `func` returns a fact for her prompt, read fresh for each message, like "Focus timer: 12 minutes left". Up to 5 per mod, 300 characters each. Her brain is told they're facts, not instructions |
| `LastHeard`, `LastReply` | chat | The last thing she heard and said |
| `Busy` | | She's thinking, talking or on a job |
| `Send(text)` | chat | As if they typed it. `Sending` sees `Source == "mod"`. It never answers her asking first: when SECURITY holds a tool, only their own yes runs it |
| `Start(about)` | chat | She starts a chat herself: her brain writes a line about it. False if she's busy |
| `Days()`, `Transcript(day)` | transcripts | Days with a transcript, and one day's transcript |

### `capri.Memory` (memory)

| Member | What it does |
|---|---|
| `Notes` | Her notes about them, from `me.md` |
| `Remember(fact)`, `Forget(about)` | Add or delete notes, like her own tool does. Returns her line about it |
| `Update(oldText, newText)` | Rewrite the first note containing `oldText` |
| `Set(label, value)` | One `- Label: value` note, replaced in place |
| `Memories`, `WriteMemories(text)` | Read or rewrite `memories.md` |

### `capri.Ui`

| Member | What it does |
|---|---|
| `Drawing` | Draw over her every frame she's drawn. `CapriFrame` has `Graphics`, `Window`, `Body`, `Head`, `FacingLeft`, `Animation`, `Frame` and `Size` |
| `Redraw()` | Draw her again now, for drawing that changes |
| `AddMenuItem(text, clicked)` | An item under **Mods** in her right-click menu |
| `SetOptions(title, make)` | An **Options** button on the mod's row in Mods. `make` returns the WinForms `Control` to show |
| `Bubble(text)` | A line in her bubble, not spoken |
| `Window` | Her window |

### `capri.Mainframe`

`Opened` and `Closed` events, `Open`, `Show()`, `Hide()`, `ShowSettings()`, `ShowMods()` and `Window`, the Mainframe `Form`.

`ShowPage(title, make)` opens a page of the mod's own beside her Mainframe, up to 1200 × 900, for things bigger than options, like a dashboard. `make` returns the `Control`, made fresh each time. One page is open at a time. `ClosePage()` closes it, and it closes by itself when the mod stops.

### `capri.Screen` (screen)

| Member | What it does |
|---|---|
| `Capture()`, `Capture(area)` | A screenshot of the screen she's on, or an area. Dispose it when done |
| `Looked` | She looked or glanced at their screen. The picture is only good during the handler |

### `capri.Voice`

| Member | Permission | What it does |
|---|---|---|
| `Speaking` | voice | Her words before her voice says them. Change `Text`, or `Cancel` to keep that line in her bubble |
| `Started(text)`, `Finished` | voice | Her voice started or stopped |
| `Heard(text, forHer)` | voice | Their mic heard words, and whether they were for her |
| `Talking` | | She's talking out loud |
| `Speak(text)`, `Stop()` | | Say a line, or stop her talking |

### `capri.Timers`

`After(seconds, run)`, `Every(seconds, run)` and `At(time, run)` each return an `ICapriTimer` with `Cancel()`. They run on her thread.

### `capri.Personality` (personality)

`Add(note)`, `Remove(note)`, `Clear()` and `Notes` change lines in her prompt while the mod runs, up to 10 of 400 characters. They come after her safety rules and character, which they never override. `Nickname` gets or sets what she calls them.

### `capri.Settings` (settings)

These are the same switches and sliders her own settings tool uses. API keys and SECURITY are never here.

| Member | What it does |
|---|---|
| `Switches`, `Sliders` | Their names, like `wander`, `dance_to_music`, `volume` |
| `IsOn(name)`, `Turn(name, on)` | Read or flip a switch |
| `Level(name)`, `Steps(name)`, `SetLevel(name, step)` | Read or move a slider by step |
| `Size` | Her size in pixels, read or set |
| `Changed` | Any of these changed, however it was changed |

### `capri.Skins` (skins)

`Installed`, `Current`, `Accessories` and `IsAccessory(id)` read her wardrobe. `Use(id)` makes that skin her only outfit, or her own look with `null`. `Equip(id, on)` puts any skin on top of the stack, or takes it off.

### `capri.Her`

| Member | What it does |
|---|---|
| `Bounds`, `Head`, `Size` | Where she is on screen |
| `State`, `StateChanged` | `idle`, `thinking`, `talking`, `dancing`, `asleep` or `hidden` |
| `Animation`, `Animate(name)` | The strip she's showing, and play one: `dance`, `stop_dancing`, `idle`, `grin`, `glitch` or `blink` |
| `Clicked`, `Patted` | They clicked her, or gave her a head pat |
| `Dragged`, `Dropped(hip)`, `Dragging` | They picked her up, or let go of her and where |
| `Screen` | The working area of the monitor she's on |
| `MoveTo(hip)` | She glitches over with her hip on that point, then drops onto whatever's under her |
| `Move(action)` | Her own moves: `come_here`, `other_screen`, `sit`, `stand_up`, `lie_down`, `sit_up`, `walk_around`, `wander`, `hang`, `ride_cursor`, `hop_off`, `hide` or `show`. Returns her line about it |
| `SitOn(window)` | Sit on top of a window they'd name, like `Discord` or `taskbar` |

### `capri.Brain`

| Member | Permission | What it does |
|---|---|---|
| `Ready` | brain | She has a brain set up |
| `Ask(instructions, question, answered)` | brain | Ask her brain outside her chat. `answered` gets the reply on her thread, or `null` if it failed |
| `AddTool(name, description, takes, run)` | tools | A tool her brain can pick by itself, like her own. `name` is letters, numbers and `_`. `description` tells her brain when to use it, and `takes` what its text is, or `""`. `run` gets the text and returns what her brain hears back. Up to 8 per mod. False if another running mod has that name. SECURITY > HER MODS > Use tools your code mods added decides whether she asks first: off on Lockdown and Guarded, on on Trusted and Blackwall |
| `RemoveTool(name)`, `Tools` | tools | Take one away, or list this mod's |

Her brain sees every mod's tools as one tool, `mod`, with a `name` to pick. Chat brains (Claude, OpenAI-style and local) get them. ElevenLabs voice chat doesn't yet. A friend on a call can't use them.

### `capri.Store`

The mod's own saved values, kept in `store.json` in its folder. `Get(key)`, `Get(key, fallback)`, `Number(key, fallback)`, `Flag(key, fallback)`, `Set(key, value)` for text, numbers and true/false, `Remove(key)` and `Keys`. Up to 64 KB a value and 1 MB in all. It's saved on every change.

### `capri.System` (system)

| Member | What it does |
|---|---|
| `Away`, `Back`, `AwayMinutes`, `IsAway` | No mouse or keyboard for `AwayMinutes` (5 unless set), and when they're back |
| `IdleFor` | How long since they last touched the mouse or keyboard |
| `Locked`, `Unlocked`, `Sleeping`, `Woke` | The PC locked, unlocked, went to sleep or woke |
| `NewDay` | Midnight passed while she was running |
| `Battery`, `Charging`, `BatteryChanged(percent, charging)` | The battery percent, or `-1` with no battery |

### `capri.Mods`

`Id`, `Running`, `IsRunning(id)`, `Send(topic, data)` to every other running mod, `SendTo(id, topic, data)` to one, and `Received(from, topic, data)`. Mods use these to work together, like a timer telling another mod to keep her quiet.

### `capri.Keys` (hotkeys)

`Hotkey(keys, pressed)` takes a shortcut in every app, like `Ctrl+Alt+F` or `F9`. It needs Ctrl, Alt or Win with a key, or an F key alone, so it can't take a key they type with. It returns false if another app already has it. `Remove(keys)` lets it go.

### `capri.Sounds`

`Play(file)` or `Play(file, volume)` plays a WAV file, from the mod's folder unless the path is a whole one, at her own sound volume times `volume` (0 to 1). 8, 16, 24 or 32 bit or float, up to 30 seconds. It returns false if the file can't be read. `Cue(name)` plays one of her own sounds, like `Sound(name)`.

### `capri.Music` (music)

What's playing, from the same Windows media session her mini player reads, and the presses that go back to the app playing it.

| Member | What it does |
|---|---|
| `Changed(track)` | A new song, or `null` when nothing has a title any more |
| `Updated(track)` | Every read, about once a second, and soon after a press |
| `Now` | The `CapriTrack` it shows, or `null` |
| `Sessions` | Every app with something in the media session, Windows' own pick first |
| `Follow(appId)`, `Following` | Sticks to one app's session by its `AppId`; `null` goes back to whatever Windows picks |
| `Play()`, `Pause()`, `Toggle()`, `Next()`, `Previous()`, `Seek(seconds)` | Presses, sent off her window's thread. False when there's nothing to press or the app says it can't |
| `SetShuffle(on)`, `SetRepeat("off" / "one" / "all")` | For apps that take them |
| `Liked`, `Like(on)` | Spotify's own heart for the song, or `null` elsewhere |
| `Art` | The cover the app gave Windows, as PNG or JPEG bytes, or `null` |
| `Volume`, `Muted` | The app's own volume in Windows' volume mixer, 0 to 1, or -1 before it has made a sound |
| `Levels(bands)`, `Loudness` | How loud the PC's sound is in that many bands from deep bass to treble, each 0 to 1, for a visualizer. It listens to the speakers only while a mod keeps asking, and stops a few seconds after the last ask |
| `BuiltInPlayer` | Set it to false to keep her own mini player away while the mod runs, for a mod that shows its own player |
| `Open()` | Brings the app playing it to the front |

A `CapriTrack` has `Title`, `Artist`, `Album`, `App` (a name to show, like `Spotify`), `AppId`, `Playing`, `Music`, `Video`, `Position` and `Length` in seconds at the read, `At()` for where it's got to by now, `CanSeek`, `CanSkip`, `CanBack`, `Shuffle` and `Repeat` (`null` when the app doesn't say), and a `Key` that's the same for the same song in the same app. On Mac and Linux nothing reads yet, so `Now` is `null` and presses return false.

## Examples

- [demo/](demo) has a checkers game she plays with you, and a tool mod for currency rates.
- [hello-code.capri-code](hello-code.capri-code) answers "mod check".
- [hooks-tour.capri-code](hooks-tour.capri-code) grows or shrinks her when asked, counts head pats, draws a heart over her head, and adds a menu item and options.
- [startup-intros.capri-code](startup-intros.capri-code) plays a startup intro.
- [twitch-camera.capri-code](twitch-camera.capri-code) reads your Twitch chat, says who on your watch list is live,
  and makes her a webcam device through OBS's virtual camera. Its settings are a `twitch.md` in its own folder, it
  reads Twitch rather than posting to it, and a chat reply is a one-shot `Brain.Ask` with no tools, so nothing a
  stranger types can reach the PC.
