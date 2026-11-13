using System.Collections;
using UnityEngine;

/// <summary>
/// Android TV / projector SoCs are weak and drive a big 1080p/4K panel. The Wii menu was 480p anyway,
/// so by default we render at 720p (native 1080p ran ~13 fps on the projector) and let the display scale it up.
/// Switchable in Preferences: Smooth (540p) / Balanced (720p) / Sharp (native).
/// For testing: `adb shell am start -n <pkg>/com.karuzohikari.revotv.RevoActivity --ei tvheight 720` overrides the setting.
/// </summary>
public class TVPerf : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVPerf");
        DontDestroyOnLoad(go);
        go.AddComponent<TVPerf>();
    }

    private IEnumerator Start()
    {
        while (AppController._instance == null) yield return null;
        yield return new WaitForSeconds(1.5f); // let preferences load
        LogPrefs();
        // test switch: adb shell am start ... --ez glass2026 false (lets a test flip the look without driving the menu)
        if (HasExtra("glass2026")) { PREFS.Glass2026.SetBool(Flag("glass2026")); Debug.Log("[PREFS] Glass2026 set by test switch to " + PREFS.Glass2026.GetBool()); }
        if (!PREFS.AllowHorizontal.GetBool()) PREFS.AllowHorizontal.SetBool(true); // the TV layout is horizontal; the switch is no longer in Settings
        // a weak stick (1 GB or less, or two cores) starts on the lightest picture, once; the person can still change it
        if (PlayerPrefs.GetInt("tv_autoquality", 0) == 0)
        {
            PlayerPrefs.SetInt("tv_autoquality", 1);
            if ((SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 1300) || SystemInfo.processorCount <= 2)
                PREFS.TvRenderHeight.SetInt(540);
            PlayerPrefs.Save();
        }
        Apply();
        NoLock = Flag("fps60");
        TVHoliday.Forced = StringExtra("holiday"); // test switch: --es holiday christmas
        TVInputModule.Off = Flag("fullinput");
        AppController._instance.RefreshFramerate();
        TVGfx.Sync(); // the graphics driver for the next start (a cold boot after a theme change made elsewhere gets it right next time)
        // the warning page waits for a press on every boot (the user wants it); only the app's own restarts (theme switch, back from Photos) skip it
        if (Flag("resume") && WarningController._instance != null) WarningController._instance.ContinueWhenReady();
        TVHome.Sync();
        TVWeather.EnsureChannel(this);
        TVWeatherPreview.Ensure(this);
        StartCoroutine(MeasureChip());
        StartCoroutine(HideSplashOnceDrawn());
        TVPower.Ensure(this);
        if (Flag("server")) { yield return new WaitForSeconds(6f); TVServerScreen.Show(); } // test switch: opens the Channel Server screen
        if (Flag("weather")) { yield return new WaitForSeconds(8f); TVWeather.Show(); } // test switch: opens the Forecast Channel
        StartCoroutine(RefreshThemeOnceReady());
        StartCoroutine(DebugSwitches());
        StartCoroutine(CoolIdle());
        if (Flag("tilecanvas")) StartCoroutine(IsolateTiles()); // experiment only: made things slower

        // lightweight frame-rate meter in logcat: `adb logcat -s Unity | grep FPS`
        float last = Time.realtimeSinceStartup;
        int frames = 0;
        while (true)
        {
            yield return null;
            frames++;
            float now = Time.realtimeSinceStartup;
            if (now - last >= 5f)
            {
                string timing = "";
                FrameTimingManager.CaptureFrameTimings();
                var ft = new FrameTiming[1];
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                    timing = $" cpuMain={ft[0].cpuMainThreadFrameTime:0.0}ms cpuRender={ft[0].cpuRenderThreadFrameTime:0.0}ms gpu={ft[0].gpuFrameTime:0.0}ms";
                Debug.Log($"[FPS] {frames / (now - last):0.0} fps at {Screen.width}x{Screen.height}{timing}");
                last = now;
                frames = 0;
            }
        }
    }

    // a custom theme applied while the app is still starting is laid out before the screen has settled: once the menu has been up
    // a few seconds its pieces are placed again, the same way applying the theme later does
    // a TV whose chip can't draw the menu at 30+ frames a second gets a steady 20 (even motion, less heat); measured once,
    // remembered (a strong box like a Shield or Fire TV 4K Max keeps its full rate)
    private const string SlowKey = "tv.gpu.slow";
    public static bool SlowChip => PlayerPrefs.GetInt(SlowKey, 0) == 1;
    private IEnumerator MeasureChip()
    {
        if (SlowChip) yield break;
        var app = AppController._instance;
        while (app == null || app.mainView == null || !app.mainView.activeInHierarchy || app.warningView.activeInHierarchy) yield return new WaitForSecondsRealtime(0.5f);
        yield return new WaitForSecondsRealtime(8f); // start-up work done
        float t = 0f; int n = 0;
        while (t < 4f) { if (!Cooling) { t += Time.unscaledDeltaTime; n++; } yield return null; }
        float ms = t * 1000f / Mathf.Max(1, n);
        Debug.Log("[PERF] menu frame time " + ms.ToString("0.0") + " ms" + (ms > 30f ? ": slow chip, steady 20 fps from now on" : ": full rate"));
        if (ms > 30f) { PlayerPrefs.SetInt(SlowKey, 1); PlayerPrefs.Save(); if (!Cooling) app.RefreshFramerate(); }
    }

    // RevoActivity's banner cover goes once the menu (or the first-run warning) has drawn a frame
    // when the start-up cover began to lift (-1 = still up): the corner power button fades in with the menu from then
    public static float SplashOffAt = -1f;
    private IEnumerator HideSplashOnceDrawn()
    {
        var app = AppController._instance;
        while (app == null || app.mainView == null || !(app.mainView.activeInHierarchy || app.warningView.activeInHierarchy)) yield return null;
        yield return null; yield return new WaitForEndOfFrame();
        Debug.Log("[PERF] menu drawn at t=" + Time.realtimeSinceStartup.ToString("0.0") + ", start-up cover off");
        SplashOffAt = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
        try { using var c = new AndroidJavaClass("com.karuzohikari.revotv.RevoActivity"); c.CallStatic("hideSplash"); } catch (System.Exception) { }
#endif
    }

    private IEnumerator RefreshThemeOnceReady()
    {
        var app = AppController._instance;
        while (app == null || app.mainView == null || !app.mainView.activeInHierarchy) yield return null;
        var tc = ThemeController._instance;
        // the theme's button places (Settings, SD card) are only right after a refresh: done once as soon as the menu exists,
        // while it is still coming in, so the later refresh no longer moves them in front of the viewer
        if (tc != null && !tc.IsDefaultLook()) { LogPlaces("before"); tc.TriggerThemeReload(); LogPlaces("early"); }
        while (app.warningView.activeInHierarchy) yield return new WaitForSeconds(0.25f);
        yield return new WaitForSeconds(4f);
        // behind a still picture of the bar: the refresh re-sizes the clock, which showed as the time jumping once after start-up
        var gcR = GridController._instance;
        if (tc != null && !tc.IsDefaultLook())
        {
            LogPlaces("pre4s");
            if (gcR != null) yield return gcR.StartCoroutine(gcR.StillBarWhile(tc.TriggerThemeReload, 10));
            else tc.TriggerThemeReload();
            LogPlaces("post4s");
        }
        // start-up leaves pictures nothing uses any more (theme loading, splash): give their memory back once, in the moment the
        // theme refresh already takes, so the 1.7 GB projector swaps less later (swapping caused the worst stalls measured)
        yield return new WaitForSeconds(2f);
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();
        Debug.Log("[PERF] unused assets released after start-up");
    }

    private static void LogPlaces(string when)
    {
        var sb = new System.Text.StringBuilder("[PLACES] " + when + " t=" + Time.unscaledTime.ToString("0.00"));
        foreach (var tt in UnityEngine.Object.FindObjectsByType<ThemedTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            sb.Append(" " + tt.category + "=" + ((RectTransform) tt.transform).anchoredPosition.ToString("0"));
        Debug.Log(sb.ToString());
    }

    // developer switches for profiling on a real device: adb shell am start ... --ez nosm true
    private static bool Flag(string key)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent != null && intent.Call<bool>("getBooleanExtra", key, false);
        }
        catch (System.Exception) { }
#endif
        return false;
    }

    // Every animated channel tile dirties the one big canvas each frame, so the whole menu (400+ elements) was rebuilt and
    // re-batched every frame. Giving each tile its own sub-canvas keeps that work local to the tile.
    private IEnumerator IsolateTiles()
    {
        while (true)
        {
            foreach (var h in FindObjectsByType<ChannelHandler>(FindObjectsSortMode.None))
            {
                if (h.GetComponent<Canvas>() != null) continue;
                h.gameObject.AddComponent<Canvas>();
                h.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private static bool HasExtra(string key)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent != null && intent.Call<bool>("hasExtra", key);
        }
        catch (System.Exception) { }
#endif
        return false;
    }

    // one line per saved preference in logcat (`adb logcat -s Unity | grep PREFS`), so settings can be checked before/after tests
    private static void LogPrefs()
    {
        var sb = new System.Text.StringBuilder("[PREFS]");
        foreach (var f in typeof(PREFS).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var v = f.GetValue(null);
            if (v == null || f.Name.StartsWith("User")) continue; // never log account fields
            var get = v.GetType().GetMethod("GetBool") ?? v.GetType().GetMethod("GetInt") ?? v.GetType().GetMethod("GetFloat") ?? v.GetType().GetMethod("GetString");
            if (get == null || get.GetParameters().Length != 0) continue;
            object val;
            try { val = get.Invoke(v, null); } catch (System.Exception) { continue; }
            sb.Append(' ').Append(f.Name).Append('=').Append(val);
        }
        Debug.Log(sb.ToString());
    }

    private static string StringExtra(string key)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            return intent != null ? intent.Call<string>("getStringExtra", key) : null;
        }
        catch (System.Exception) { }
#endif
        return null;
    }

    private IEnumerator DebugSwitches()
    {
        bool noSoftMask = Flag("nosm");
        bool noMask = Flag("nomask");
        string hide = StringExtra("hide"); // comma separated GameObject names to switch off
        yield return new WaitForSeconds(20f);
        if (Flag("nonav")) { foreach (var n in FindObjectsByType<TVNav>(FindObjectsSortMode.None)) n.enabled = false; foreach (var g in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (g.name == "TV Focus Glow") g.gameObject.SetActive(false); Debug.Log("[PERF] TVNav off"); }
        if (Flag("novsync")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Debug.Log("[PERF] vsync off, target 60"); }
        if (Flag("fps30")) { QualitySettings.vSyncCount = 2; Application.targetFrameRate = 30; Debug.Log("[PERF] vsync/2, target 30"); }
        Debug.Log($"[PERF] graphics={FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None).Length} softmasks={FindObjectsByType<Coffee.UISoftMask.SoftMask>(FindObjectsSortMode.None).Length} masks={FindObjectsByType<UnityEngine.UI.Mask>(FindObjectsSortMode.None).Length} canvases={FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length}");
        if (Flag("memdump")) MemDump(); // test switch: which textures hold the graphics memory (logcat [MEM])
        if (!string.IsNullOrEmpty(hide))
        {
            foreach (var name in hide.Split(','))
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == name.Trim()) t.gameObject.SetActive(false);
        }
        while (noSoftMask || noMask)
        {
            if (noMask)
                foreach (var m in FindObjectsByType<UnityEngine.UI.Mask>(FindObjectsSortMode.None)) m.enabled = false;
            if (noSoftMask)
                foreach (var sm in FindObjectsByType<Coffee.UISoftMask.SoftMask>(FindObjectsSortMode.None)) sm.enabled = false;
            yield return new WaitForSeconds(1f);
        }
    }

    // estimated GPU size of every loaded texture, grouped by what it is (a release build can't ask Unity for exact sizes)
    private static void MemDump()
    {
        var rows = new System.Collections.Generic.List<(long bytes, string what)>();
        long total = 0; int count = 0;
        // who shows each texture (path of the first picture using it)
        var owner = new System.Collections.Generic.Dictionary<Texture, string>();
        string Path(Transform t) { string p = ""; for (int i = 0; t != null && i < 5; i++, t = t.parent) p = t.name + "/" + p; return p; }
        foreach (var ri in FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ri.texture != null && !owner.ContainsKey(ri.texture)) owner[ri.texture] = (ri.isActiveAndEnabled ? "" : "(hidden) ") + Path(ri.transform);
        foreach (var im in FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (im.sprite != null && im.sprite.texture != null && !owner.ContainsKey(im.sprite.texture)) owner[im.sprite.texture] = (im.isActiveAndEnabled ? "" : "(hidden) ") + Path(im.transform);
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
        {
            if (t == null) continue;
            int bpp = 4;
            if (t is Texture2D t2)
            {
                var f = t2.format;
                bpp = f == TextureFormat.Alpha8 || f == TextureFormat.R8 ? 1 : f == TextureFormat.RGB24 ? 3 : f == TextureFormat.ETC2_RGBA8 || f == TextureFormat.ASTC_4x4 ? 1 : f == TextureFormat.ETC_RGB4 || f == TextureFormat.ETC2_RGB ? 0 : 4;
            }
            long b = (long) t.width * t.height * Mathf.Max(bpp, 1) / (bpp == 0 ? 2 : 1);
            if (t is Texture2D m && m.mipmapCount > 1) b = b * 4 / 3;
            total += b; count++;
            rows.Add((b, $"{t.GetType().Name} {t.width}x{t.height} '{t.name}' used by {(owner.TryGetValue(t, out var o) ? o : "nothing on screen")}"));
        }
        rows.Sort((a, c) => c.bytes.CompareTo(a.bytes));
        Debug.Log($"[MEM] textures={count} estimated={total / 1048576f:0.0} MB");
        for (int i = 0; i < Mathf.Min(40, rows.Count); i++) Debug.Log($"[MEM] {rows[i].bytes / 1048576f:0.00} MB  {rows[i].what}");
        // the same, grouped: GIF/animation frames share a name
        var groups = new System.Collections.Generic.Dictionary<string, (long b, int n)>();
        foreach (var r in rows) { int q0 = r.what.IndexOf('\''), q1 = r.what.IndexOf('\'', q0 + 1); string k = r.what.Substring(q0, q1 - q0 + 1); groups.TryGetValue(k, out var g); groups[k] = (g.b + r.bytes, g.n + 1); }
        var gl = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, (long b, int n)>>(groups);
        gl.Sort((a, c) => c.Value.b.CompareTo(a.Value.b));
        for (int i = 0; i < Mathf.Min(20, gl.Count); i++) Debug.Log($"[MEM] group {gl[i].Value.b / 1048576f:0.0} MB x{gl[i].Value.n} {gl[i].Key}");
    }

    // TV chips run flat out trying to reach 60 fps (this projector manages ~28), so a launcher left on screen keeps the
    // GPU at full load for hours. After a minute without a button press the menu drops to 15 fps: the slow glass and
    // tile animations still move, nothing is switched off, and the first press puts the full rate back at once.
    // Not while a channel preview is open (its banner plays at full speed).
    private const float IdleAfter = 60f;
    private const int IdleFps = 15;
    private const int VideoIdleFps = 20; // a moving (video) wallpaper looked choppy at 15: 20 while idle (30 kept the projector at full heat)
    private const float VideoRestAfter = 60f;
    // custom themes (big pictures, videos) ran the projector's chip flat out until it shut itself down: they cool down after 25 s
    // without a press (the normal look and Glass 2026 keep 60 s)
    private const float ThemeIdleAfter = 25f;
    private static float IdleWait()
    {
        var tc = ThemeController._instance;
        return tc != null && !TVGlass.On && !tc.IsDefaultLook() ? ThemeIdleAfter : IdleAfter;
    } // idle this long: the video wallpaper stops on its frame and the menu cools at 15 fps
    public static bool Cooling { get; private set; }
    public static bool NoLock; // test switch --ez fps60 true: no Glass 30 fps hold (for A/B measurements)

    private IEnumerator CoolIdle()
    {
        while (true)
        {
            bool open = ChannelController._instance != null && ChannelController._instance.currentChannel != null;
            bool idle = PREFS.TvCoolIdle.GetBool() && !open && !Input.anyKey
                        && Time.unscaledTime - TVNav.LastUserInput > (TVWeather.Open ? 4f : IdleWait()) && Time.unscaledTime > IdleWait()
                        && Time.unscaledTime - TVWeather.MovedAt > 1f;
            // the weather pages and the globe are still pictures: after a few seconds without a press they're drawn at the idle rate
            // (no visible change, much less heat on small TV chips); any press brings the full rate back at once
            // a video wallpaper left playing for long kept the projector hot enough to shut itself down: after a while with no
            // press it holds its frame (any press plays it again)
            TVVideoWall.Rest(Cooling && Time.unscaledTime - TVNav.LastUserInput > VideoRestAfter);
            int idleFps = TVVideoWall.Playing && !TVWeather.Open ? VideoIdleFps : IdleFps;
            if (idle != Cooling)
            {
                Cooling = idle;
                if (idle) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = idleFps; }
                else { QualitySettings.vSyncCount = 1; AppController._instance.RefreshFramerate(); }
                Debug.Log("[PERF] idle cooling " + (idle ? "on (" + idleFps + " fps)" : "off"));
            }
            else if (Cooling && Application.targetFrameRate != idleFps) { Application.targetFrameRate = idleFps; Debug.Log("[PERF] idle rate " + idleFps + " fps"); }
            // while cooling, look every frame so the first press wakes it at once; otherwise twice a second is plenty
            if (Cooling) yield return null; else yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    // 0 = native
    private static int WantedHeight()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            if (intent != null && intent.Call<bool>("hasExtra", "tvheight"))
            {
                return intent.Call<int>("getIntExtra", "tvheight", 0);
            }
        }
        catch (System.Exception) { }
#endif
        return PREFS.TvRenderHeight.GetInt();
    }

    // Revo stays alive behind the HDMI input or another app (the Home helper keeps its process), so when it goes to the
    // background it lets go of whatever it can: coming back from Home is then instant instead of a cold start
    private void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        Application.targetFrameRate = 5;
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }

    private void OnApplicationFocus(bool focus)
    {
        if (focus) AppController._instance?.RefreshFramerate();
    }

    public static void Apply()
    {
        if (!TVMode.IsTV) return;
        int nativeW = Display.main.systemWidth, nativeH = Display.main.systemHeight;
        if (nativeW <= 0 || nativeH <= 0) return;

        int wanted = WantedHeight();
        if (wanted > 0 && wanted < nativeH)
        {
            int w = Mathf.RoundToInt(wanted * (float) nativeW / nativeH);
            if (Screen.height != wanted) Screen.SetResolution(w, wanted, true);
        }
        else if (Screen.height != nativeH)
        {
            Screen.SetResolution(nativeW, nativeH, true);
        }
    }
}
