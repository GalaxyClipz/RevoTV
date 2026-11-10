using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Animations;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Channel Server > Theme > "Post to the Content Hub": puts one of the TV's themes on the Content Hub under the account the TV
/// is signed in to (Revo Shop > Sign in), so the phone never has to sign in. The name gets "[TV]" (the hub has no tags) and the
/// description says it was made on a TV. The preview pictures are taken from the TV's Home menu when the theme is the one in use,
/// otherwise from the theme's wallpaper.
/// </summary>
public static class TVHubPost
{
    public static bool SignedIn => !string.IsNullOrEmpty(ShopController.GetUserId()) && !string.IsNullOrEmpty(ShopController.GetUserAuth());

    public static object State()
    {
        var list = new List<object>();
        var tc = ThemeController._instance;
        string cur = tc != null && tc.GetCurrentTheme() != null ? tc.GetCurrentTheme().GetImportedFileName() : null;
        if (Directory.Exists(SaveManager.SAVE_FOLDER_THEMES))
            foreach (var f in Directory.GetFiles(SaveManager.SAVE_FOLDER_THEMES, "theme_*.zip"))
            {
                string file = Path.GetFileName(f);
                if (file.StartsWith("theme_rii")) continue; // Revo's own themes aren't anyone's to post
                if (FromHub(f)) continue; // downloaded from the Content Hub: someone else's work
                list.Add(TVTiles.D("file", file, "name", ThemeName(f) ?? file, "inUse", file == cur, "made", file.StartsWith("theme_tvmade_")));
            }
        return TVTiles.D("signedIn", SignedIn, "user", SignedIn ? PREFS.UserName.GetString() : null, "themes", list, "previews", Previews());
    }

    // ---------------------------------------------------------------- channel pictures / previews

    // Revo's own built-in artwork: never anyone's to post
    private static readonly string[] BuiltIn = { "_default", "_discchannel_", "_photochannel_", "_revoshop_", "_riichannel_", "_youtubechannel_" };
    private static string OwnZip(string file, CHANNELTYPE type)
    {
        if (string.IsNullOrEmpty(file)) return null;
        foreach (var b in BuiltIn) if (file.Contains(b)) return null;
        string zip = SaveManager.GetChannelSaveFolder(type) + file;
        return File.Exists(zip) && !FromHub(zip) ? zip : null;
    }

    // the channels whose tile picture and/or preview were made by this person (on the TV or the Channel Server)
    private static List<object> Previews()
    {
        var list = new List<object>();
        var cc = ChannelController._instance;
        if (cc == null) return list;
        foreach (var c in cc.loadedChannels)
        {
            if (c == null || !c.isFullySetup) continue;
            if (c.info != null && TVTiles.IsLocked(c.info.packageName)) continue; // Revo's built-in channels: their art is Revo's
            bool icon = OwnZip(c.iconName, CHANNELTYPE.ICON) != null, banner = OwnZip(c.bannerName, CHANNELTYPE.BANNER) != null;
            if (!icon && !banner) continue;
            list.Add(TVTiles.D("page", c.gridNumber, "pos", c.position, "name", c.GetTag(), "icon", icon, "banner", banner));
        }
        return list;
    }

    // a still picture of an animation zip (its first picture, a GIF's first frame) for the hub's thumbnail
    private static string Still(string zip, string outName)
    {
        try
        {
            string tmp = Path.Combine(Application.temporaryCachePath, "tv_still_" + outName);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            ZipFile.ExtractToDirectory(zip, tmp);
            Texture2D tex = null;
            foreach (var f in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(File.ReadAllBytes(f), false) && (tex == null || t.width * t.height > tex.width * tex.height)) { if (tex != null) UnityEngine.Object.Destroy(tex); tex = t; }
                    else UnityEngine.Object.Destroy(t);
                }
                else if (ext == ".gif" && tex == null)
                {
                    var frames = new List<Texture2D>(); var delays = new List<float>();
                    StaticUtils.LoadGif(f, frames, delays);
                    if (frames.Count > 0) tex = frames[0];
                    for (int i = 1; i < frames.Count; i++) UnityEngine.Object.Destroy(frames[i]);
                }
            }
            Directory.Delete(tmp, true);
            if (tex == null) return null;
            string png = Path.Combine(Application.temporaryCachePath, outName);
            File.WriteAllBytes(png, Scaled(tex, 640));
            UnityEngine.Object.Destroy(tex);
            return png;
        }
        catch (Exception e) { Debug.LogWarning("[Hub] still: " + e.Message); return null; }
    }

    // posts a channel's own tile picture and/or preview, marked [TV] like the themes
    public static async Task<Dictionary<string, object>> PostPreview(int page, int pos, string name, string description)
    {
        Dictionary<string, object> Fail(string why) => TVTiles.D("ok", false, "error", why);
        if (!SignedIn) return Fail("The TV isn't signed in to the Content Hub. On the TV: Revo Shop > Sign in, then try again.");
        name = (name ?? "").Trim(); description = (description ?? "").Trim();
        if (name.Length == 0) return Fail("Give it a name");
        if (description.Length == 0) return Fail("Write a short description (what it shows)");
        var c = TVTiles.At(page, pos);
        if (c == null) return Fail("That channel isn't there any more");
        if (c.info != null && TVTiles.IsLocked(c.info.packageName)) return Fail("That's one of Revo's built-in channels: its pictures are Revo's own");
        string iconZip = OwnZip(c.iconName, CHANNELTYPE.ICON), bannerZip = OwnZip(c.bannerName, CHANNELTYPE.BANNER);
        if (iconZip == null && bannerZip == null) return Fail("This channel has no pictures of your own to post (built-in or downloaded ones can't be posted)");
        string iconPic = iconZip != null ? Still(iconZip, "tv_post_icon.png") : null;
        string bannerPic = bannerZip != null ? Still(bannerZip, "tv_post_banner.png") : null;
        if ((iconZip != null && iconPic == null) || (bannerZip != null && bannerPic == null)) return Fail("Couldn't make the preview pictures");
        if (!name.StartsWith("[TV]")) name = "[TV] " + name;
        if (!description.Contains("Made on a TV")) description += "\n\nMade on a TV with RevoTV (looks best on TVs and projectors).";
        var resp = await ShopController._instance.SendRequest(WebRequestController.SendCreateAnimation(name, description, bannerZip, bannerPic, iconZip, iconPic));
        if (resp == null) return Fail("The Content Hub didn't answer");
        if (resp.result != UnityWebRequest.Result.Success)
            return Fail(resp.responseCode == 401 || resp.responseCode == 403 ? "The Content Hub refused the TV's sign-in: sign in again on the TV" : "The Content Hub said: " + (resp.error ?? "error"));
        Debug.Log("[Hub] posted channel pictures from page " + page + " slot " + pos + " as " + name);
        return TVTiles.D("ok", true, "message", "Posted as \"" + name + "\". New uploads can take a while to show on the hub (unverified creators are checked first).");
    }

    private static bool FromHub(string zip)
    {
        try { using var z = ZipFile.OpenRead(zip); return z.GetEntry("onlineinfo.json") != null; }
        catch (Exception) { return false; }
    }

    private static string ThemeName(string zip)
    {
        try
        {
            using var z = ZipFile.OpenRead(zip);
            var e = z.GetEntry("theme.json"); if (e == null) return null;
            using var r = new StreamReader(e.Open());
            return (string) JObject.Parse(r.ReadToEnd())["name"];
        }
        catch (Exception) { return null; }
    }

    public static async Task<Dictionary<string, object>> Post(string file, string name, string description)
    {
        Dictionary<string, object> Fail(string why) => TVTiles.D("ok", false, "error", why);
        if (!SignedIn) return Fail("The TV isn't signed in to the Content Hub. On the TV: Revo Shop > Sign in, then try again.");
        if (string.IsNullOrEmpty(file) || file.Contains("/") || file.Contains("..")) return Fail("Pick a theme");
        name = (name ?? "").Trim(); description = (description ?? "").Trim();
        if (name.Length == 0) return Fail("Give it a name");
        if (description.Length == 0) return Fail("Write a short description (what it looks like)");
        var tc = ThemeController._instance;
        var theme = tc != null ? tc.GetOrLoadTheme(file) : null;
        if (theme == null) return Fail("That theme couldn't be opened on the TV");
        if (FromHub(SaveManager.SAVE_FOLDER_THEMES + file)) return Fail("That theme came from the Content Hub, so it's someone else's to post");
        string themePath = ShopController._instance.GetThemeFilePath(theme);
        if (themePath == null) return Fail("That theme's file is missing");

        // the preview picture
        string pic = Path.Combine(Application.temporaryCachePath, "tv_post_home.png");
        bool inUse = tc.GetCurrentTheme() != null && tc.GetCurrentTheme().GetImportedFileName() == file;
        byte[] png = inUse ? await HomeShot() : WallpaperShot(theme);
        if (png == null) return Fail("No preview picture: use the theme on the TV first (then the TV takes a picture of its Home menu)");
        File.WriteAllBytes(pic, png);

        if (!name.StartsWith("[TV]")) name = "[TV] " + name;
        if (!description.Contains("Made on a TV")) description += "\n\nMade on a TV with RevoTV (looks best on TVs and projectors).";
        string accent = ColorUtility.ToHtmlStringRGBA(theme.GetColor(ThemeColor.MainAccent));
        var resp = await ShopController._instance.SendRequest(WebRequestController.SendCreateTheme(name, description, accent, themePath, pic, pic,
            theme.HasDifferentColors(), theme.HasDifferentTextures(), theme.HasDifferentAudio()));
        if (resp == null) return Fail("The Content Hub didn't answer");
        if (resp.result != UnityWebRequest.Result.Success)
            return Fail(resp.responseCode == 401 || resp.responseCode == 403 ? "The Content Hub refused the TV's sign-in: sign in again on the TV" : "The Content Hub said: " + (resp.error ?? "error"));
        Debug.Log("[Hub] posted theme " + file + " as " + name);
        return TVTiles.D("ok", true, "message", "Posted as \"" + name + "\". New uploads can take a while to show on the hub (unverified creators are checked first).");
    }

    // the Home menu as it looks right now, without the Channel Server screen over it
    private static Task<byte[]> HomeShot()
    {
        var done = new TaskCompletionSource<byte[]>();
        AppController._instance.StartCoroutine(Shot(done));
        return done.Task;
    }

    private static IEnumerator Shot(TaskCompletionSource<byte[]> done)
    {
        TVServerScreen.SetHidden(true);
        yield return null; yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D tex = null;
        try
        {
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            done.SetResult(Scaled(tex, 960));
        }
        catch (Exception e) { Debug.LogWarning("[Hub] " + e.Message); done.SetResult(null); }
        finally { if (tex != null) UnityEngine.Object.Destroy(tex); TVServerScreen.SetHidden(false); }
    }

    private static byte[] WallpaperShot(Theme theme)
    {
        var tex = theme.wallpaper != null && theme.wallpaper.texture != null ? theme.wallpaper.texture.texture : null;
        return tex != null ? Scaled(tex, 960) : null;
    }

    private static byte[] Scaled(Texture src, int width)
    {
        int w = Mathf.Min(width, src.width), h = Mathf.Max(1, Mathf.RoundToInt(src.height * (w / (float) src.width)));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var png = t.EncodeToPNG(); UnityEngine.Object.Destroy(t);
        return png;
    }
}
