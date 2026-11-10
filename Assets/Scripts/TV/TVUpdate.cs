using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Settings > "Check for updates": asks GitHub for the newest RevoTV release, and if it is newer than this app, downloads its
/// APK and hands it to Android's installer (same signature = installs over this one, keeping settings).
/// </summary>
public static class TVUpdate
{
    public const string Repo = "GalaxyClipz/RevoTV"; // set by the release script (tools/release.ps1)
    private static bool busy;

    public static void Check()
    {
        if (busy) return;
        AppController._instance.StartCoroutine(CheckRoutine());
    }

    private static int[] Ver(string v)
    {
        v = (v ?? "").Trim().TrimStart('v', 'V');
        var p = v.Split('.', '-', '+');
        var r = new int[3];
        for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], out r[i]);
        return r;
    }
    private static bool Newer(string a, string b)
    {
        int[] x = Ver(a), y = Ver(b);
        for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] > y[i];
        return false;
    }

    private static IEnumerator CheckRoutine()
    {
        busy = true;
        PopupController.ShowPopup("tv.update.checking", PopupController.ClosePopup);
        string tag = null, url = null, err = null;
        using (var r = UnityWebRequest.Get("https://api.github.com/repos/" + Repo + "/releases/latest"))
        {
            r.SetRequestHeader("User-Agent", "RevoTV-updater");
            r.SetRequestHeader("Accept", "application/vnd.github+json");
            r.timeout = 15;
            yield return r.SendWebRequest();
            if (r.result != UnityWebRequest.Result.Success) err = r.responseCode == 404 ? "no releases yet" : r.error;
            else
            {
                try
                {
                    var j = JObject.Parse(r.downloadHandler.text);
                    tag = (string) j["tag_name"];
                    foreach (var a in (JArray) j["assets"])
                        if (((string) a["name"] ?? "").EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) { url = (string) a["browser_download_url"]; break; }
                }
                catch (Exception e) { err = e.Message; }
            }
        }
        PopupController.ClosePopup();
        yield return new WaitForSecondsRealtime(0.4f);
        if (err != null || tag == null) { busy = false; PopupController.ShowPopup("tv.update.failed", PopupController.ClosePopup, true, new[] { err ?? "no release found" }); yield break; }
        if (!Newer(tag, Application.version) || url == null)
        {
            busy = false;
            PopupController.ShowPopup("tv.update.latest", PopupController.ClosePopup, true, new[] { Application.version });
            yield break;
        }
        bool go = false, answered = false;
        PopupController.ShowPopup("tv.update.found", () => { go = true; answered = true; PopupController.ClosePopup(); }, () => { answered = true; PopupController.ClosePopup(); },
            new[] { tag.TrimStart('v', 'V'), Application.version });
        while (!answered) yield return null;
        if (!go) { busy = false; yield break; }

        yield return new WaitForSecondsRealtime(0.4f);
        PopupController.ShowPopup("tv.update.downloading", PopupController.ClosePopup);
        string path = System.IO.Path.Combine(Application.persistentDataPath, "RevoTV-update.apk");
        using (var d = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        {
            d.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
            d.SetRequestHeader("User-Agent", "RevoTV-updater");
            yield return d.SendWebRequest();
            PopupController.ClosePopup();
            yield return new WaitForSecondsRealtime(0.4f);
            if (d.result != UnityWebRequest.Result.Success) { busy = false; PopupController.ShowPopup("tv.update.failed", PopupController.ClosePopup, true, new[] { d.error }); yield break; }
        }
        Debug.Log("[Update] downloaded " + tag + " to " + path);
        busy = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        using var helper = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV");
        string result = helper.CallStatic<string>("installApk", AndroidLinker.currentActivity, path);
        Debug.Log("[Update] install: " + result);
        if (result == "allow") PopupController.ShowPopup("tv.update.allow", PopupController.ClosePopup);
#endif
    }
}
