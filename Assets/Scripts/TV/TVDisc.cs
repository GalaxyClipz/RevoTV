using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The Disc Channel opens a GameCube / Wii emulator. Revo can carry two installers in StreamingAssets/Dolphin:
/// dolphin.apk (official Dolphin, 64-bit devices only) and dolphin32.apk (a Dolphin fork that also runs on 32-bit TVs).
/// Installed: it opens. Not installed: the installer that suits this TV is offered (Android asks to confirm). With no
/// suitable installer in the build, a 64-bit device gets Dolphin's store page and a 32-bit one a plain explanation.
/// The app inside each installer is read from the file itself, so a fork with its own package name works too.
/// </summary>
public static class TVDisc
{
    public const string Package = "org.dolphinemu.dolphinemu";
    private const string Helper = "com.karuzohikari.revotv.RevoTV";

    public static void Open()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AppController._instance.StartCoroutine(OpenRoutine());
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Disc] " + e);
            AndroidLinker.LaunchApp(Package, true);
        }
#else
        AndroidLinker.LaunchApp(Package, true);
#endif
    }

    private static IEnumerator OpenRoutine()
    {
        bool is64;
        using (var helper = new AndroidJavaClass(Helper)) is64 = helper.CallStatic<bool>("is64Bit");

        // the installer for this TV, copied out of Revo (Android can't install straight from inside another app)
        string file = is64 ? "dolphin.apk" : "dolphin32.apk";
        string apk = Path.Combine(Application.persistentDataPath, is64 ? file : "dolphin32-tv2.apk"); // -tv2: the build without on-screen buttons
        if (!File.Exists(apk))
        {
            using var req = UnityWebRequest.Get(Path.Combine(Application.streamingAssetsPath, "Dolphin/" + file));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.data != null && req.downloadHandler.data.Length > 1024)
                File.WriteAllBytes(apk, req.downloadHandler.data);
        }

        using var h = new AndroidJavaClass(Helper);
        var activity = AndroidLinker.currentActivity;
        string bundledPackage = File.Exists(apk) ? h.CallStatic<string>("apkPackage", activity, apk) : null;

        // already installed (Dolphin, or the fork this build carries): open it
        foreach (var pkg in new[] { bundledPackage, Package })
        {
            if (!string.IsNullOrEmpty(pkg) && h.CallStatic<bool>("isInstalled", activity, pkg))
            {
                // an emulator built for old Android never asks for Files access itself, so it can't see any games
                // until it's allowed in its Settings page: offer that first (No just opens it)
                if (!h.CallStatic<bool>("hasPermission", activity, pkg, "android.permission.READ_EXTERNAL_STORAGE"))
                {
                    string target = pkg;
                    PopupController.ShowPopup("popup.disc.files", () =>
                    {
                        PopupController.ClosePopup();
                        using var helper = new AndroidJavaClass(Helper);
                        if (!helper.CallStatic<bool>("openAppSettings", AndroidLinker.currentActivity, target)) AndroidLinker.LaunchApp(target, false);
                    }, () =>
                    {
                        PopupController.ClosePopup();
                        AndroidLinker.LaunchApp(target, false);
                    });
                    yield break;
                }
                // an older copy of the 32-bit emulator (with its touch buttons over the game): offer Revo's TV build once
                if (!is64 && pkg == bundledPackage && File.Exists(apk) && !h.CallStatic<bool>("sameSigner", activity, pkg, apk) && PlayerPrefs.GetInt("tv.disc.update.asked", 0) == 0)
                {
                    string target = pkg;
                    PlayerPrefs.SetInt("tv.disc.update.asked", 1); PlayerPrefs.Save();
                    PopupController.ShowPopup("popup.disc.update", () =>
                    {
                        PopupController.ClosePopup();
                        using var helper = new AndroidJavaClass(Helper);
                        helper.CallStatic<bool>("askUninstall", AndroidLinker.currentActivity, target); // then the Disc Channel offers the new one
                    }, () => { PopupController.ClosePopup(); TVControllers.TuneDolphin(); TVGameScreen.Show(target); });
                    yield break;
                }
                // the 32-bit fork's own screens don't work with a remote: Revo lists the games itself
                if (!is64) { TVControllers.TuneDolphin(); TVGameScreen.Show(pkg); yield break; }
                Debug.Log("[Disc] opening " + pkg);
                AndroidLinker.LaunchApp(pkg, false);
                yield break;
            }
        }

        if (bundledPackage == null)
        {
            Debug.Log("[Disc] no installer for this " + (is64 ? "64" : "32") + "-bit device in the build");
            if (is64) AndroidLinker.LaunchApp(Package, true);
            else PopupController.ShowPopup("popup.disc.no64");
            yield break;
        }

        PopupController.ShowPopup("popup.disc.install", () =>
        {
            PopupController.ClosePopup();
            using var helper = new AndroidJavaClass(Helper);
            string result = helper.CallStatic<string>("installApk", AndroidLinker.currentActivity, apk);
            Debug.Log("[Disc] install " + bundledPackage + ": " + result);
            if (result == "allow") PopupController.ShowPopup("popup.disc.allow");
        }, PopupController.ClosePopup);
    }
}
