using UnityEngine;

/// <summary>
/// "Make Home" on any Android TV: the remote's Home button opens Revo. Android TV, Google TV and Fire TV keep their own
/// home screen whatever an app asks (even when Revo is picked in the system's Home chooser, as on the SDMC projector),
/// so Revo carries a small Accessibility helper (RevoTV.androidlib) that takes the Home key. The one thing only the
/// person can do is turn that helper on in Accessibility, so turning Make Home on explains it and opens that screen;
/// when they come back Revo says whether it is on. Off puts everything back: the Home entry off and the helper idle.
/// </summary>
public static class TVHome
{
    private const string Helper = "com.karuzohikari.revotv.RevoTV";
    private static bool waitingForSwitch;

    public static void Apply(bool on)
    {
        if (!TVMode.IsTV || !Application.isMobilePlatform) return;
        try
        {
            using var helper = new AndroidJavaClass(Helper);
            var activity = AndroidLinker.currentActivity;
            helper.CallStatic("setHomeOn", activity, on);
            AndroidLinker._instance.SetLauncherVersion(on);
            waitingForSwitch = false;
            if (!on) return;
            if (helper.CallStatic<bool>("isHomeServiceAllowed", activity))
            {
                PopupController.ShowPopup("popup.tvhome.done");
                return;
            }
            // one switch only the person can turn on: show where it is, then open that screen
            PopupController.ShowPopup("popup.tvhome.allow", () =>
            {
                PopupController.ClosePopup();
                waitingForSwitch = true;
                using var h = new AndroidJavaClass(Helper);
                if (!h.CallStatic<bool>("openAccessibilitySettings", AndroidLinker.currentActivity))
                {
                    waitingForSwitch = false;
                    PopupController.ShowPopup("popup.tvhome.manual");
                }
            });
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Home] " + e);
        }
    }

    // back in Revo after the Accessibility screen: say whether the switch got turned on
    private static void Returned()
    {
        if (!waitingForSwitch) return;
        waitingForSwitch = false;
        try
        {
            using var helper = new AndroidJavaClass(Helper);
            bool allowed = helper.CallStatic<bool>("isHomeServiceAllowed", AndroidLinker.currentActivity);
            PopupController.ShowPopup(allowed ? "popup.tvhome.done" : "popup.tvhome.notyet");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Home] check: " + e.Message);
        }
    }

    private class Watcher : MonoBehaviour
    {
        private void OnApplicationFocus(bool focus) { if (focus) Returned(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TV Home Watcher");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Watcher>();
    }

    // the helper only reads its own switch: keep it in step with Revo's setting at start (e.g. after data import)
    public static void Sync()
    {
        if (!TVMode.IsTV || !Application.isMobilePlatform) return;
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var helper = new AndroidJavaClass(Helper);
            helper.CallStatic("setHomeOn", activity, PREFS.TvHomeButton.GetBool());
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Home] sync: " + e.Message);
        }
    }
}
