using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colour of the hover light (TVNav's focus glow). "auto" (Normal) = Wii blue, or the custom theme's own hover colour when one is in
/// use; otherwise one of the presets or any "#RRGGBB" picked in Settings or the Channel Server's colour picker.
/// </summary>
public static class TVGlowColor
{
    public static readonly List<(string name, string hex)> Presets = new List<(string, string)>
    {
        ("Normal", "auto"), ("Wii Blue", "#47C2FF"), ("Cyan", "#3CF0FF"), ("Green", "#4CFF7A"), ("Lime", "#B6FF3C"),
        ("Yellow", "#FFE53C"), ("Orange", "#FF9A3C"), ("Red", "#FF4C4C"), ("Pink", "#FF6FD2"), ("Purple", "#A77CFF"), ("White", "#FFFFFF"),
    };

    public static int Version { get; private set; } // bumps when the colour changes, so the light is redrawn

    public static string Current => string.IsNullOrEmpty(PREFS.TvGlowColor.GetString()) ? "auto" : PREFS.TvGlowColor.GetString();

    public static void Set(string value)
    {
        if (string.IsNullOrEmpty(value)) value = "auto";
        PREFS.TvGlowColor.SetString(value);
        Version++;
        // the tiles' own hover rings take the colour now
        foreach (var e in Object.FindObjectsByType<ThemedElement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (System.Array.IndexOf(e.GetAllColors(), ThemeColor.ChannelHighlight) >= 0) e.UpdateElement();
    }

    public static string NameOf(string value)
    {
        foreach (var p in Presets) if (string.Equals(p.hex, value, System.StringComparison.OrdinalIgnoreCase)) return p.name;
        return "Custom " + value.ToUpperInvariant();
    }

    // null = the built-in Wii blue
    public static Color? Base()
    {
        if (TVGlass.On) return null; // 2026 keeps its own light, like it keeps out custom themes
        string v = Current;
        if (v != "auto") return ColorUtility.TryParseHtmlString(v, out var c) ? c : (Color?) null;
        var tc = ThemeController._instance; var t = tc != null ? tc.GetCurrentTheme() : null;
        if (t == null || TVGlass.On) return null;
        string f = t.GetImportedFileName() ?? "";
        if (f.StartsWith("theme_rii") || f == ThemeController.defaultThemeFileName) return null; // Revo's own looks keep the Wii blue
        return t.GetColor(ThemeColor.ChannelHighlight); // the colour the theme gives its own hover ring, so both rings match
    }

    // a key that changes whenever the colour the light should have changes (theme switched, setting changed)
    private static string cachedKey; private static float keyAt = -1f; private static int keyVersion = -1;
    public static string Key()
    {
        if (cachedKey != null && keyVersion == Version && Time.unscaledTime < keyAt) return cachedKey; // checked twice a second, not every frame
        var b = Base();
        keyVersion = Version; keyAt = Time.unscaledTime + 0.5f;
        return cachedKey = Version + ":" + (b.HasValue ? ColorUtility.ToHtmlStringRGB(b.Value) : "wii");
    }

    public static void Shades(out Vector3 rim, out Vector3 near, out Vector3 wide)
    {
        rim = new Vector3(0.86f, 0.97f, 1.00f); near = new Vector3(0.28f, 0.76f, 1.00f); wide = new Vector3(0.06f, 0.48f, 1.00f);
        var b = Base(); if (!b.HasValue) return;
        Color.RGBToHSV(b.Value, out float h, out float s, out float v);
        v = Mathf.Max(v, 0.85f); // a dark accent would make an invisible light
        var n = Color.HSVToRGB(h, s * 0.85f, v);
        var w = Color.HSVToRGB(h, Mathf.Min(1f, s * 1.1f + 0.1f * Mathf.Sign(s)), v * 0.9f);
        var r = Color.Lerp(n, Color.white, 0.78f);
        rim = new Vector3(r.r, r.g, r.b); near = new Vector3(n.r, n.g, n.b); wide = new Vector3(w.r, w.g, w.b);
    }
}
