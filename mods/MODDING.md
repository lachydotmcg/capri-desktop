# Making Capri mods

Capri reads four kinds of mod. Import any of them from **Mainframe > CAPRI > Mods > Import mod**.

| File | What it does |
|---|---|
| `.capri-mod` | Settings only: extra names, spellings, colours, the hearing log |
| `.capri-skin` | A zip of her sprite strips that changes how she looks. Several **stack**: each one turned on goes on top |
| `.capri-skill` | Know-how for a game that she follows when she plays it |
| `.capri-code` | C# that runs inside Capri, Windows only for now |

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

She asks before a code mod runs, and asks again whenever the file changes. The approve box lists the permissions it asks for. A code mod still has full access to the PC, so permissions describe what it does through Capri. They don't sandbox it.

`Start` gets an `ICapriHost`. Hooks run on her window's thread. If your handler throws, the error goes in `errors.txt` and she carries on. When the mod is turned off or removed, its hooks, timers, menu items, notes, options, drawing, brain tools, context, hotkeys and page are removed with it.

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

### The host

| Member | Permission | What it does |
|---|---|---|
| `Folder` | | A data folder of the mod's own |
| `Say(line)` | | She says it, out loud if her voice is on |
| `Log(line)` | | A line in `errors.txt` |
| `Art(name)`, `Sound(name)`, `PlayIntro()` | | Her sprite strip, one of her sounds, a startup intro |
| `Api` | | `3` here. Brain tools, context, the store, system events, hotkeys, sounds, pages, moving her and mod messages came in 3; the other hooks in 2 |
| `CapriVersion`, `Has(permission)` | | Her version, and whether this mod asked for a permission |

### `capri.Chat`

| Member | Permission | What it does |
|---|---|---|
| `Sending` | chat | What they typed or said, before her brain sees it. Change `Text`, or set `Cancel` and she ignores it |
| `Replying` | chat | Her reply before it shows and is spoken. Change `Text` |
| `Replied` | chat | Her line once it shows. `Source` is `reply` or `remark` |
| `ToolUsed(tool, result)` | chat | She used one of her tools |
| `ToolUsing` | tools | She's about to use a tool. `CapriToolCall` has `Tool`, `Doing` (like "open Spotify"), `Name`, `Action` and `Text`. Set `Cancel`, and `Reason` for what her brain is told instead |
| `AddContext(func)`, `ClearContext()` | chat | `func` returns a fact for her prompt, read fresh for each message, like "Focus timer: 12 minutes left". Up to 5 per mod, 300 characters each. Her brain is told they're facts, not instructions |
| `LastHeard`, `LastReply` | chat | The last thing she heard and said |
| `Busy` | | She's thinking, talking or on a job |
| `Send(text)` | chat | As if they typed it. `Sending` sees `Source == "mod"` |
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
| `AddTool(name, description, takes, run)` | tools | A tool her brain can pick by itself, like her own. `name` is letters, numbers and `_`. `description` tells her brain when to use it, and `takes` what its text is, or `""`. `run` gets the text and returns what her brain hears back. Up to 8 per mod. False if another running mod has that name |
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

## Examples

- [hello-code.capri-code](hello-code.capri-code) answers "mod check".
- [hooks-tour.capri-code](hooks-tour.capri-code) grows or shrinks her when asked, counts head pats, draws a heart over her head, and adds a menu item and options.
- [startup-intros.capri-code](startup-intros.capri-code) plays a startup intro.
