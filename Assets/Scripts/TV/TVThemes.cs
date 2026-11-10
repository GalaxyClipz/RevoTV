using Animations;
using UnityEngine;

/// <summary>
/// Turning a custom theme on from a TV: always one question first, never during Glass 2026, and the same way from
/// Settings and from the Content Hub. "Back to normal look" turns every custom theme off again.
/// </summary>
public static class TVThemes
{
    // Glass 2026 draws from the built-in theme only: say so instead of half-applying a downloaded one
    public static bool GlassBlocks(string key = "tv.theme.glass")
    {
        if (!TVMode.IsTV || !TVGlass.On) return false;
        PopupController.ShowPopup(key, PopupController.ClosePopup);
        return true;
    }

    public static void Offer(Theme theme, bool inHub = false)
    {
        if (theme == null || GlassBlocks()) return;
        PopupController.ShowPopup("tv.theme.enable", () => AskWallpaper(theme, inHub), PopupController.ClosePopup, new[] { theme.name });
    }

    private static void AskWallpaper(Theme theme, bool inHub)
    {
        if (theme.wallpaper != null && (!theme.wallpaper.isVideo || TVStill.Get(theme.wallpaper, theme.GetImportedFileName()) != null))
            PopupController.ShowPopup("popup.usewallpaper", () => Apply(theme, true, inHub), () => Apply(theme, false, inHub));
        else
            Apply(theme, false, inHub);
    }

    private static void Apply(Theme theme, bool wallpaper, bool inHub)
    {
        Switch(theme.GetImportedFileName(), wallpaper);
    }

    // Changing the look is done by a clean restart: the menu then starts drawn entirely from the chosen theme (switching live left
    // pieces of the old one behind: colours, tile pictures, shapes)
    private static bool restarting;
    private static void Switch(string fileName, bool wallpaper)
    {
        if (restarting) return;
        restarting = true;
        PREFS.UseThemeWallpaper.SetBool(wallpaper);
        PREFS.CurrentTheme.SetString(fileName);
        PlayerPrefs.Save();
        PopupController.ShowPopup("tv.theme.restart", () => AndroidLinker._instance.RestartApp());
        DG.Tweening.DOVirtual.DelayedCall(1.8f, () => AndroidLinker._instance.RestartApp());
    }

    // a theme made in the Channel Server: switch to it straight away (the person asked for it on their phone)
    public static void SwitchTo(string fileName, bool wallpaper)
    {
        PlayerPrefs.SetInt(TVServerScreen.ReopenKey, 1);
        Switch(fileName, wallpaper);
    }

    // hold OK on a theme in the list: delete it (the built-in Rii themes stay)
    public static void AskDelete(AnimationBasicInfo info)
    {
        if (info == null) return;
        string file = info.animationFileName ?? "";
        if (file.StartsWith("theme_rii")) { PopupController.ShowPopup("popup.deleteforbiddentheme", PopupController.ClosePopup); return; }
        PopupController.ShowPopup("popup.confirmdeletetheme", () =>
        {
            var tc = ThemeController._instance;
            var cur = tc.GetCurrentTheme();
            if (cur != null && cur.GetImportedFileName() == file)
            {
                PREFS.UseThemeWallpaper.SetBool(false);
                tc.DeleteTheme(cur); // it is the one on screen: the normal look comes back first
            }
            else SaveManager.DeleteTheme(file);
            PopupController.ShowPopup("popup.deletedtheme", () => SettingsController._instance.themeApplySettingView.Show(), true, new[] { info.animationName });
        }, PopupController.ClosePopup, new[] { info.animationName });
    }

    public static void AskNormalLook()
    {
        PopupController.ShowPopup("tv.theme.normal", () =>
        {
            var tc = ThemeController._instance;
            // the normal look is the classic Revo menu: Glass 2026 goes off too, and the hover light goes back to its normal colour
            TVGlowColor.Set("auto");
            if (tc.IsDefaultLook() && !PREFS.Glass2026.GetBool()) { PopupController.ShowPopup("popup.themeapplied"); return; }
            PREFS.Glass2026.SetBool(false);
            Switch(tc.defaultTheme.GetImportedFileName(), false);
        }, PopupController.ClosePopup);
    }
}
