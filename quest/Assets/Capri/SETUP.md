# Capri in your room: Quest 3 setup

This folder holds a Unity add-on that puts Capri in your real room with passthrough. She glitches in near you, walks around your floor, sits on the edges of tables, couches and beds, naps when you ignore her, and reacts when you pet, poke, grab or throw her.

**What you need:** a Windows or Mac PC, your Quest 3 or 3S, a USB-C cable, and about an hour for the first build.

---

## 1. Get the headset ready (one time)

1. **Make a free Meta developer account** at developers.meta.com/horizon/sign-up.
2. In the **Meta Horizon** phone app, go to **Devices → your Quest → Developer Mode** and turn it **On**.
3. In the headset, go to **Settings → Physical Space → Space Setup** and scan your room. She uses this scan to find your floor and furniture. Without it she still works, but she only uses the floor.

## 2. Install Unity (one time)

1. Install **Unity Hub** from unity.com.
2. In Unity Hub, go to **Installs → Install Editor** and choose the latest **Unity 6 LTS**.
3. Tick **Android Build Support**, along with **OpenJDK** and **Android SDK & NDK Tools** under it.

## 3. Make the project

1. In Unity Hub, click **New project**, pick the **Universal 3D** template, and name it `CapriRoom`.
2. Open **File → Build Profiles**, choose **Meta Quest** (or **Android** if Meta Quest isn't listed), and click **Switch Platform**.
3. Open **Window → Package Manager**, click **+ → Install package by name…**, and add each of these:
   - `com.meta.xr.sdk.core`
   - `com.meta.xr.mrutilitykit`

   Restart Unity if it asks.
4. Open **Meta → Tools → Project Setup Tool** and click **Fix All** on the Android/Meta Quest tab. Repeat until nothing important is left. You can ignore the "Manually fixable" items.

## 4. Build the scene

1. In the **Hierarchy**, delete **Main Camera**.
2. Open **Meta → Tools → Building Blocks** and add:
   - **Camera Rig**
   - **Passthrough**
   - **MR Utility Kit**
   - **Controller Tracking**
   - **Hand Tracking** (optional, lets you pinch her with your bare hands)
3. Copy the **`Assets/Capri`** folder from this zip into your project's **Assets** folder, either by dragging it into Unity's Project window or copying it in your file browser.
4. If Unity asks to **Import TMP Essentials**, click it. If it doesn't ask, go to **Window → TextMeshPro → Import TMP Essential Resources**.
5. In the top menu, click **Capri → Add Capri to Scene**.
6. Save the scene with **File → Save**, and make sure it's the scene ticked in **Build Profiles → Scene List**.

## 5. Put her on your Quest

1. Plug the Quest into your PC. In the headset, tap **Allow** on the USB debugging prompt.
2. In Unity, open **File → Build Profiles**, make sure your Quest shows under **Run Device**, then click **Build And Run**.
3. The first build takes a while. When it opens, choose **Allow** for spatial data so she can see your room.

Next time, find her in your Quest's **Library → Unknown Sources**.

---

## Playing with her

| Do this | What happens |
|---|---|
| **Grip button** near her, or **pinch** with a tracked hand | Picks her up. Let go to drop her, or swing and let go to throw her. |
| **Thumbstick up or down** while holding her | Makes her bigger or smaller. She remembers the size. |
| Move your hand **gently near her head** | Petting. She grins and says something. |
| **Swipe your hand through her** fast | Poke. She glitches and complains. |
| **A** | While she sits on a ledge, she bops along. |
| **B** | Come here. She glitches over in front of you. |
| **Y** | Opens or closes her settings: a **Wandering** on/off switch. Touch the switch or press A/X to flip it. |
| Put her down **next to the edge** of a table, couch or bed | She sits on the edge. |
| Ignore her for about 2½ minutes | She naps. Look at her or reach toward her to wake her. |

## Changing her habits

Click the **Capri** object in the Hierarchy. In the Inspector, **Capri Pet** has her size, walking speed, how far she wanders from you, how often she sits on furniture, and how long before she naps. **Room Sense** has the backup floor height used when no room scan loads.

## If something goes wrong

- **Red errors mentioning `Meta.XR.MRUtilityKit`, `OVRInput` or `OVRHand`:** a Meta package is missing, so repeat step 3.3. If it's installed and the errors persist, Meta may have renamed something in a newer SDK. Copy the exact error lines and send them to me.
- **Errors mentioning `TMPro` or `TextMeshPro`:** do step 4.4.
- **You see black instead of your room:** the Passthrough building block is missing, or the Project Setup Tool still has fixes to apply.
- **She's floating or sunk into the floor:** do Space Setup on the headset. Also check that **OVRManager → Tracking Origin Type** on the Camera Rig is **Floor Level**.
- **She never sits on furniture:** your Space Setup needs tables, couches or beds marked. You can edit them in the headset's Space Setup.

## What she can't do (yet)

- **Show up inside other games.** The Quest only runs one app at a time, and apps can't draw over each other.
- **Talk with an AI brain.** She says her greeting, plus lines when you pet, poke or throw her. A chat brain could be added later.
- **Walk around furniture.** She takes straight paths and glitch-teleports onto furniture, so she can pass through a chair leg on the floor.

## Credits and licence

Capri, her art and her voice clips come from the Capri project (https://github.com/lachydotmcg/capri-desktop), by Lachy, with art by "Capri's artist". They're used under the PolyForm Noncommercial License 1.0.0 (see `LICENSE.md`). That means this build is for personal, non-commercial use: don't sell it, and keep these credits if you share it. Capri is an unofficial fan project and isn't endorsed by CD Projekt Red or the anime's rights holders.
