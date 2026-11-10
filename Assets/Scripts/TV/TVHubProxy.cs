using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Animations;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The Content Hub for the Channel Server page: the page asks the TV, and the TV asks the same hub its own menus use
/// (same account, same downloads folder). Nothing is rebuilt on the web side. Downloads land exactly where the TV's own
/// Content Hub puts them, so they also show up in Settings.
/// </summary>
public static class TVHubProxy
{
    private static string Origin(string type) => type == "themes" ? "themes" : "animations";

    public static async Task<Dictionary<string, object>> Search(string type, string query, string sort, int page)
    {
        var ct = type == "themes" ? ContentType.THEMES : ContentType.ANIMATIONS;
        if (sort != "newest" && sort != "oldest") sort = "popular";
        using var r = await WebRequestController.SendSearchRequest(ct, string.IsNullOrWhiteSpace(query) ? null : Uri.EscapeDataString(query.Trim()), null, sort, Mathf.Max(1, page));
        if (r.isNetworkError) return TVTiles.D("ok", false, "error", "The TV can't reach the Content Hub right now");
        var items = new List<object>();
        int pages = 1;
        if (!r.isHttpError)
        {
            var json = r.GetJsonResponse();
            pages = Mathf.Max(1, (int?) json["pages"] ?? 1);
            if (json[Origin(type)] is JArray arr)
                foreach (var t in arr)
                    items.Add(TVTiles.D("id", (string) (t[type == "themes" ? "themeId" : "animationId"]), "name", (string) t["name"],
                        "author", (string) t["author"], "downloads", (int?) t["downloads"] ?? 0));
        }
        else if (r.responseCode != 404) return TVTiles.D("ok", false, "error", "The Content Hub answered " + r.responseCode);
        return TVTiles.D("ok", true, "pages", pages, "page", page, "items", items);
    }

    /// <returns>the picture's bytes, or null</returns>
    public static async Task<(byte[] data, string type)> Thumb(string type, string id, string which)
    {
        if (string.IsNullOrEmpty(id) || id.Contains("/") || id.Contains("..")) return (null, null);
        string kind = type == "themes" ? (which == "settings" ? "settings" : "home") : (which == "banner" ? "banner" : "icon");
        using var req = UnityWebRequest.Get(WebRequestController.GetUrl("/" + Origin(type) + "/thumbnail/" + id + "/" + kind));
        req.SetRequestHeader("UserId", ShopController.GetUserId() ?? "");
        req.SetRequestHeader("UserAuth", ShopController.GetUserAuth() ?? "");
        req.timeout = 20;
        await req.SendWebRequest();
        if (req.isNetworkError || req.isHttpError || req.downloadHandler.data == null || req.downloadHandler.data.Length < 16) return (null, null);
        var d = req.downloadHandler.data;
        return (d, d[0] == 0xFF ? "image/jpeg" : "image/png");
    }

    /// <summary>Downloads an animation (tile picture and/or preview) or a theme to the TV.</summary>
    public static async Task<Dictionary<string, object>> Install(string type, string id)
    {
        if (string.IsNullOrEmpty(id) || id.Contains("/") || id.Contains("..")) return TVTiles.D("ok", false, "error", "Unknown item");
        if (type == "themes")
        {
            string path = SaveManager.SAVE_FOLDER_THEMES + "theme_" + id + ".zip";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(SaveManager.SAVE_FOLDER_THEMES);
                Progress = 0f;
                string err = await WebRequestController.DownloadThemeToFile(id, path, p => Progress = p);
                Progress = -1f;
                if (err != null) return TVTiles.D("ok", false, "error", err);
            }
            return TVTiles.D("ok", true);
        }
        bool icon = false, banner = false;
        foreach (var t in new[] { CHANNELTYPE.ICON, CHANNELTYPE.BANNER })
        {
            string folder = SaveManager.GetChannelSaveFolder(t);
            string path = folder + t.ToString().ToLowerInvariant() + "_" + id + ".zip";
            bool have = File.Exists(path);
            if (!have)
            {
                using var r = await WebRequestController.SendDownloadAnimation(id, t);
                if (!r.isNetworkError && !r.isHttpError && r.downloadHandler.data != null && r.downloadHandler.data.Length > 0)
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(path, r.downloadHandler.data);
                    have = true;
                }
            }
            if (t == CHANNELTYPE.ICON) icon = have; else banner = have;
        }
        if (!icon && !banner) return TVTiles.D("ok", false, "error", "That picture couldn't be downloaded");
        return TVTiles.D("ok", true, "hasIcon", icon, "hasBanner", banner);
    }

    // the theme download in progress (0..1), -1 = none: the Channel Server page shows it
    public static volatile float Progress = -1f;

    public static string ApplyTheme(string id, bool restart = false)
    {
        var tc = ThemeController._instance;
        if (tc == null || string.IsNullOrEmpty(id)) return "Theme controller isn't ready";
        if (TVGlass.On) return "Custom themes only work in the classic menu. Turn Glass 2026 off first";
        if (restart)
        {
            // like picking a theme on the TV: a clean restart with it (the page already told the person)
            if (!System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath, "Themes", "theme_" + id + ".zip"))) return "That theme isn't on the TV yet";
            TVThemes.SwitchTo("theme_" + id + ".zip", true);
            return null;
        }
        var theme = tc.GetOrLoadTheme("theme_" + id + ".zip");
        if (theme == null) return "That theme isn't on the TV yet";
        tc.SetNewTheme(theme, unloadPrevious: !theme.Equals(tc.GetCurrentTheme()));
        return null;
    }
}
