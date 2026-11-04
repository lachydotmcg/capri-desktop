# Capri Room (Quest port)

**Capri in your real room, on Meta Quest 3 and 3S.** A mixed-reality port of Capri, the glitchy desktop companion in this repo. This folder is a standalone Unity 6 project; the desktop app is untouched. She walks around your floor, sits on the edges of your furniture, naps when you ignore her, reacts when you pet, poke, grab or throw her, and talks with you through your headset.

> Unofficial fan project, made with Lachy's permission. Not endorsed by CD Projekt Red, the anime's rights holders or its voice actors. Free and non-commercial.

## Install (sideload)

1. **Turn on Developer Mode.** Make a free account at [developers.meta.com](https://developers.meta.com/horizon/sign-up), then in the Meta Horizon phone app go to **Devices → your Quest → Developer Mode → On**.
2. **Download** the newest `CapriRoom-v….apk` from the [Capri Room releases](https://github.com/fadievr/capri-room/releases/latest).
3. **Install it** with [SideQuest](https://sidequestvr.com) or [Meta Quest Developer Hub](https://developers.meta.com/horizon/documentation/unity/ts-mqdh/): plug in your Quest, allow USB debugging in the headset, and drag the APK onto the app.
4. In the headset, open **Library → Unknown Sources → Capri Room**. Allow **spatial data** (so she can see your room) and the **microphone** (so she can hear you).

Do **Space Setup** first (**Settings → Physical Space → Space Setup**) so she knows where your floor, tables, couches and beds are.

What's new in each version: [patch notes](CHANGELOG.md).

## Talking to her

Press **Y** to open her settings, then point with the laser and pull the trigger (or pinch).

| Tab | What to set |
|---|---|
| **BRAIN** | Pick an AI (NVIDIA free key, OpenAI, Claude or DeepSeek) and paste its key. |
| **VOICE** | **Hearing:** Groq (free key from [console.groq.com](https://console.groq.com)). **Voice:** Fish Audio with your key and a voice from [fish.audio](https://fish.audio). |

Keys stay on your headset and only go to the service they belong to. Paste them with her keyboard's **Paste** button after copying them in the Quest browser.

Capri prohibits cloning or impersonating a living person's voice without explicit, informed consent. Make your own voice with Fish Audio's Voice Design.

## Controls

| Control | What it does |
|---|---|
| **Grip** near her (or pinch with bare hands) | Pick her up. Let go to drop her, or swing and let go to throw her. |
| **Thumbstick** while holding her | Resize her. |
| Put her down **next to a ledge** | She sits on the edge. |
| Gentle hand near her head / fast swipe | Pet / poke. |
| **X** | Mute or unmute her mic. |
| **A** | She bops along while sitting on a ledge. |
| **B** | Come here. |
| **Y** | Settings: BRAIN, VOICE, YOU, MOVES. |

## Build it yourself

Unity 6 (6000.6) with Android Build Support, Meta XR Core SDK and Meta MR Utility Kit. Open the `quest/` folder in Unity Hub, then **Capri → Release → 2. Build release APK**. The first build creates your own signing key in `Keystore/`. Keep a backup of it, and never commit it.

Capri's own code is in `Assets/Capri/`.

## Credits

- **Capri**, her personality, voice lines and code: [the Capri project](https://github.com/lachydotmcg/capri-desktop) by Lachy.
- **Art and animations:** Capri's artist.
- **Quest port:** Capri Room.

Licensed under the [PolyForm Noncommercial License 1.0.0](../LICENSE.md). Keep these credits if you share it, and don't sell it.
