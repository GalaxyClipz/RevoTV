using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Settings > "Reset looks and settings": puts the app's options, colour theme, home music and weather choices back to how
/// Revo comes. Channels, their pictures, page layout, the hub sign-in, language and the Home-button setup are left alone.
/// </summary>
public static class TVReset
{
    private static readonly HashSet<string> Keep = new HashSet<string>
    {
        "UserId", "UserAuth", "UserName", "FirstTimeOpening", "IsLauncherVersion", "TvHomeButton", "SetNewRevoChannels", "NumberOfPages",
        "NumberAppsX", "NumberAppsXHorizontal", "NumberAppsY", "NumberAppsYHorizontal", "GridSize", "LastOpenBundle", "Language",
        "CrashedDuringLoad", "HdmiPort", "Glass2026", "SteamLocation", "messaging_app", "AltHubUrl",
    };

    public static void Run()
    {
        foreach (var p in PREFS.GetAllPreferences())
        {
            if (p == null || Keep.Contains(p.name) || p.name.StartsWith("FirstTimeOpening_") || p.name.StartsWith("PreferredEmulator_")) continue;
            p.Reset();
        }
        foreach (var k in new[] { "tv.weather.auto", "tv.weather.city", "tv.weather.celsius", "tv.weather.kmh", "tv.weather.banner" }) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.DeleteKey("tv_autoquality");
        PREFS.CurrentTheme.Reset(); // the colour theme comes back on the restart: switching it live drew the menu half-way
        PlayerPrefs.Save();
        TVHomeMusic.Clear();
        Debug.Log("[Reset] looks and settings put back");
        // no second prompt to click: close the question and restart straight away
        PopupController.ClosePopup();
        DG.Tweening.DOVirtual.DelayedCall(0.6f, () => AndroidLinker._instance.RestartApp());
    }
}
