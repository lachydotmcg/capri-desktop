# Capri for Android

She lives on your phone: the Capri Remote chat, a real AR Room, and a little Capri that floats over your other apps.

- **Chat** is the same Remote page as [capriai.app/remote](https://capriai.app/remote/), bundled inside the app, so it opens instantly and pairs with your PC the same way (scan the QR code, then pick Capri when Android asks which app opens the link). With no PC she uses the phone brain from Remote's settings.
- **Room in AR** (camera button, then *Room in AR*) uses Google ARCore to find your floor or table and stand her on it, so she stays put while you walk around. Phones without ARCore get the Room camera from Remote instead.
- **Float on my screen** (menu, *On this phone*) lets her float over other apps when you leave Capri, like she rides your cursor on the PC. Tap her to chat, drag her to move, drag her onto the X to put her away. Her replies pop up next to her while you're in another app.

## Build it

You need JDK 17, the Android SDK (platform 35) and Gradle 8.9 or newer.

```
cd android
gradle assembleDebug
```

The APK is `app/build/outputs/apk/debug/app-debug.apk`. Every push that touches the app also builds it in the **Android** workflow, smoke tests it on an emulator, and uploads `Capri-android-debug.apk` as an artifact.

The build copies her web files from `site/remote` and `site/assets`, and her sprite sheets from `quest/Assets/Capri/Resources/CapriSprites`, so there is only one copy of each in the repo.

## Thanks

YunoTheAlien, for pushing for an Android Capri. The ARCore Room started as a prototype on 14 Nov 2026.
