# Revo Launcher – Android TV fork (personal, local only)

Based on KaruzoHikari/Revo-Launcher (CC BY-NC-SA 4.0). Keep it non-commercial and share-alike if it's ever shared.

## What changed (all under Assets/Scripts/TV unless noted)
- `Plugins/Android/AndroidManifest.xml`: LEANBACK_LAUNCHER + HOME/DEFAULT filters, leanback/touchscreen optional, TV banner.
- `TVMode`: detects Android TV / Google TV / projectors.
- `RInput` + `TVNav`: remote navigation. D-pad moves a focus between everything clickable, OK clicks, Back closes popups / settings / channel.
  The focus is a soft breathing Wii-blue glow that matches each item's shape (circle, pill, tile). A real mouse/air-mouse still shows the hand pointer.
- `HdmiButton` + `TVHdmi`: round Wii-style HDMI button in the home bar. Preferences (TV only): show/hide it and pick HDMI 1-4.
- `TVLayout`: channel preview widened to the screen.
- `TVPerf`: render quality (Smooth 540p default / Balanced 720p / Sharp native) – the projector GPU is the bottleneck.
- Portrait locking is skipped on TV (Settings/Shop used to squash the window into a strip).
- `AndroidLinker`: lists/launches TV-only (leanback) apps; hold-hint popup disabled on TV.

## Build
Unity 6000.3.23f1 + Android Build Support. The project's iOS-only `Editor/PostprocessBuild.cs` doesn't compile without the iOS module,
and the original author's keystore isn't in the repo (use debug signing). `RevoTV.apk` here is a ready debug-signed build.
Install: `adb connect <tv-ip:port>` then `adb install -r RevoTV.apk`.

## Perf notes (projector, 1080p panel, ARMv7 + weak GPU)
Native 1080p ≈ 14 fps, 720p ≈ 27 fps, 540p ≈ 38 fps. The 8 channel tiles are the whole cost (hiding the grid = 60 fps).
Not the cause: soft masks (none used), icon animation ticking, per-tile canvases (slower).
