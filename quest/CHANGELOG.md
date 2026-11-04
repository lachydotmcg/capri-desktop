# Capri Room patch notes

## v1.0.1 (2026-11-03)

**Fixed**
- Capri now sees your furniture. Space Setup saves desks, tables, couches and beds as 3D boxes, and she only looked for flat surfaces, so she never found any ledges. She now uses the top of each box.
- Putting her down on a desk no longer drops her to the floor. She dangles below your hand, so her feet could already be inside the desk when you let go, and the ground check missed it.

**Changed**
- Every flat-topped object in your room scan counts as a ledge: tables, desks, couches, beds, shelves, screens, lamps, "other" and unlabeled objects. The room itself (floor, ceiling, walls, doors, windows, wall art) doesn't. Tops smaller than about a hand are skipped.
- Landing anywhere on furniture makes her sit on its nearest edge, within 60 cm (was 20 cm).

**Added (Unity editor tools)**
- Capri → Release → 3. Test sideload from GitHub: downloads the published APK and installs it on a USB-connected Quest.
- Capri → Release → Install built APK on headset: installs your local build before it's published.
- Capri → Debug → Headset report: reads Capri Room's permissions and log from the headset (read-only).

## v1.0.0 (2026-11-03)

First release: Capri in your room on Quest 3 and 3S, with talking, settings (Y), mic mute (X), come here (B) and ledge sitting.
