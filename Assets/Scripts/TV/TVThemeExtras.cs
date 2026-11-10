using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// TV-only extras a theme can carry in its theme.json: "tvEmptyAlpha" (0.1..1) = how see-through its empty channel slots are.
/// Set from the Channel Server (theme maker, or live on the theme in use); Revo's own looks and Glass 2026 always use 1.
/// </summary>
public static class TVThemeExtras
{
    private static readonly Dictionary<string, float> cache = new Dictionary<string, float>();
    private static string curFile; private static float curAlpha = 1f; private static float checkAt;

    private static string CurrentFile()
    {
        var tc = ThemeController._instance; var t = tc != null ? tc.GetCurrentTheme() : null;
        return t != null ? t.GetImportedFileName() : null;
    }

    public static bool Editable(string file) => !string.IsNullOrEmpty(file) && !file.StartsWith("theme_rii") && file != ThemeController.defaultThemeFileName;

    // called every frame by every empty slot: the zip is only read once per theme
    public static float EmptyAlpha()
    {
        if (TVGlass.On) return 1f;
        if (Time.unscaledTime < checkAt) return curAlpha;
        checkAt = Time.unscaledTime + 0.5f;
        string f = CurrentFile();
        if (f == curFile) return curAlpha;
        curFile = f;
        curAlpha = Editable(f) ? Read(f) : 1f;
        return curAlpha;
    }

    private static float Read(string file)
    {
        if (cache.TryGetValue(file, out float a)) return a;
        a = 1f;
        try
        {
            using var z = ZipFile.OpenRead(SaveManager.SAVE_FOLDER_THEMES + file);
            var e = z.GetEntry("theme.json");
            if (e != null) { using var r = new StreamReader(e.Open()); var v = JObject.Parse(r.ReadToEnd())["tvEmptyAlpha"]; if (v != null) a = Mathf.Clamp((float) v, 0.1f, 1f); }
        }
        catch (Exception ex) { Debug.LogWarning("[ThemeExtras] " + ex.Message); }
        cache[file] = a;
        return a;
    }

    public static object State()
    {
        string f = CurrentFile();
        return TVTiles.D("editable", Editable(f) && !TVGlass.On, "glass", TVGlass.On, "percent", Mathf.RoundToInt((Editable(f) ? Read(f) : 1f) * 100f));
    }

    // live, on the theme in use: written into its theme.json so it stays with the theme (and goes along if it's posted)
    public static string SetEmptyPercent(int percent)
    {
        if (TVGlass.On) return "Turn Glass 2026 off on the TV first (empty slots there are glass)";
        string f = CurrentFile();
        if (!Editable(f)) return "Revo's own looks can't be changed: make or use a custom theme first";
        float a = Mathf.Clamp(percent, 10, 100) / 100f;
        try
        {
            using var z = ZipFile.Open(SaveManager.SAVE_FOLDER_THEMES + f, ZipArchiveMode.Update);
            var e = z.GetEntry("theme.json"); if (e == null) return "That theme has no theme.json";
            JObject json; using (var r = new StreamReader(e.Open())) json = JObject.Parse(r.ReadToEnd());
            json["tvEmptyAlpha"] = Math.Round(a, 2);
            e.Delete();
            var ne = z.CreateEntry("theme.json", System.IO.Compression.CompressionLevel.Fastest);
            using var w = new StreamWriter(ne.Open()); w.Write(json.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex) { Debug.LogError("[ThemeExtras] " + ex); return "Couldn't save it: " + ex.Message; }
        cache[f] = a; curFile = f; curAlpha = a;
        return null;
    }

    // the theme maker writes the same field into a new theme
    public static void Write(JObject themeJson, int percent)
    {
        if (percent <= 0 || percent >= 100) { themeJson.Remove("tvEmptyAlpha"); return; }
        themeJson["tvEmptyAlpha"] = Math.Round(Mathf.Clamp(percent, 10, 100) / 100f, 2);
    }
}
