using UnityEngine;

/// <summary>
/// Detects Android TV / Google TV / projector devices.
/// On those the launcher is driven by a remote: no touch, D-pad moves a virtual cursor.
/// </summary>
public static class TVMode
{
    private static bool? cached;

    public static bool IsTV
    {
        get
        {
            if (cached == null)
            {
                cached = Detect();
            }
            return cached.Value;
        }
    }

    // classic look: tiles were ~2.0:1 next to the Wii's ~1.88:1 (2026-11-12 side-by-side): a little narrower and taller
    // Wii tiles are ~1.9:1; the phone-made ones are ~1.3:1. On a landscape TV the home tiles are widened a little to read like the Wii.
    public static float StretchX = 1.22f; // set by Grid.ResizeGrid so tiles fill the Wii column pitch
    public static Vector2 TileStretch => IsTV && !CameraController.IsPortrait() ? Vector2.Scale(new Vector2(StretchX * 1.05f, 1.03f) * (TVGlass.On ? 1f : TVTune.Get("tile", 1.03f)), ClassicTiles ? new Vector2(0.9875f, 1.0125f) : Vector2.one) : Vector2.one; // art fills ~21% of the screen inside a 22% column, ~20% of the height per row: gutters like the reference

    // the Wii's tile shape (2026-11-12) is only for tiles drawn with the built-in tile pictures: the normal look, or a theme that
    // keeps them (e.g. "Wii 3.0", which only swaps the empty-slot static). A theme with its own tile frame / shape (GameCube,
    // 3DS...) keeps the proportions it was made with. Worked out once per theme.
    private static Theme tilesTheme; private static bool tilesClassic = true;
    public static bool ClassicTiles
    {
        get
        {
            if (TVGlass.On) return false;
            var tc = ThemeController._instance;
            var cur = tc != null ? tc.GetCurrentTheme() : null;
            if (cur == null || tc.IsDefaultLook()) return true;
            if (!ReferenceEquals(cur, tilesTheme))
            {
                tilesTheme = cur;
                tilesClassic = cur.GetThemeTexture("channel_border") == null && cur.GetThemeTexture("channel_mask") == null && cur.GetThemeTexture("channel_highlight") == null;
                Debug.Log("[Tiles] theme tiles: " + (tilesClassic ? "built-in (Wii shape)" : "its own (kept as made)"));
            }
            return tilesClassic;
        }
    }
    // filled tiles' extra width / height on a classic TV: Wii shape for built-in tiles, the earlier proportions for a theme's own
    public static Vector3 TileFill => ClassicTiles ? new Vector3(TVTune.Get("fillx", 1.033f), TVTune.Get("filly", 0.992f), 1f) : new Vector3(1.052f, 0.974f, 1f);

    // Touch gestures only make sense on phones/tablets
    public static bool UseTouch => Application.isMobilePlatform && !IsTV;

    private static bool Detect()
    {
#if UNITY_EDITOR
        return false;
#elif UNITY_ANDROID
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var pm = activity.Call<AndroidJavaObject>("getPackageManager");
            if (pm.Call<bool>("hasSystemFeature", "android.software.leanback")
                || pm.Call<bool>("hasSystemFeature", "android.hardware.type.television"))
            {
                return true;
            }

            using var uiMode = activity.Call<AndroidJavaObject>("getSystemService", "uimode");
            return uiMode.Call<int>("getCurrentModeType") == 4; // UI_MODE_TYPE_TELEVISION
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("TVMode detection failed: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }
}
