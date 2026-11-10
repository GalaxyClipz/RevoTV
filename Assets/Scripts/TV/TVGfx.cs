using System.IO;
using UnityEngine;

/// <summary>
/// Which graphics driver the NEXT start uses (RevoActivity reads it before the engine starts). Measured 2026-11-07 on the
/// projector: a big custom theme (Frutiger) took 627 MB on Vulkan vs 509 MB on OpenGL ES, ~3% fewer fps on ES, and running out of
/// memory caused the worst stalls (kswapd at 80%+, 9-19 fps). The built-in look and Glass 2026 need little memory and run ~6% faster
/// on Vulkan, so they keep it. Themes switch with a clean restart, so the choice always applies at once.
/// </summary>
public static class TVGfx
{
    private const long BigTheme = 2L * 1024 * 1024; // theme files above this get OpenGL ES (Rii Dark/Matrix are tiny)

    public static bool Wanted()
    {
        if (!TVMode.IsTV || PREFS.Glass2026.GetBool()) return false;
        string file = PREFS.CurrentTheme.GetString();
        if (string.IsNullOrEmpty(file) || SaveManager.SAVE_FOLDER_THEMES == null) return false;
        try { var fi = new FileInfo(SaveManager.SAVE_FOLDER_THEMES + file); return fi.Exists && fi.Length > BigTheme; }
        catch (System.Exception) { return false; }
    }

    // stores the choice for the next start (called at every start and right before every restart)
    public static void Sync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            bool gles = Wanted();
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var cls = new AndroidJavaClass("com.karuzohikari.revotv.RevoActivity");
            cls.CallStatic("setGles", activity, gles);
            Debug.Log($"[PERF] graphics now {SystemInfo.graphicsDeviceType}, next start {(gles ? "OpenGL ES (big custom theme: less memory)" : "Vulkan")}");
        }
        catch (System.Exception e) { Debug.LogWarning("[PERF] graphics choice not saved: " + e.Message); }
#endif
    }
}
