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

    // Wii tiles are ~1.9:1; the phone-made ones are ~1.3:1. On a landscape TV the home tiles are widened a little to read like the Wii.
    public static float StretchX = 1.22f; // set by Grid.ResizeGrid so tiles fill the Wii column pitch
    public static Vector2 TileStretch => IsTV && !CameraController.IsPortrait() ? new Vector2(StretchX * 1.05f, 1.03f) * (TVGlass.On ? 1f : TVTune.Get("tile", 1.03f)) : Vector2.one; // art fills ~21% of the screen inside a 22% column, ~20% of the height per row: gutters like the reference

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
