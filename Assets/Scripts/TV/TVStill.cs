using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// A TV never plays a theme's video wallpaper (it froze small projectors), but a theme made around a video should still look like
/// its preview: one picture is cut out of the video once (the video is pulled out of the theme's zip for a moment, then deleted)
/// and used as a still wallpaper.
/// </summary>
public static class TVStill
{
    private static readonly Dictionary<string, ThemeTexture> cache = new Dictionary<string, ThemeTexture>();

    public static ThemeTexture Get(ThemeTexture video, string themeZipName)
    {
        if (video == null || !video.isVideo || string.IsNullOrEmpty(video.imageFileName) || string.IsNullOrEmpty(themeZipName)) return null;
        string key = themeZipName + "|" + video.imageFileName;
        if (cache.TryGetValue(key, out var have) && have != null && have.texture != null) return have;
        try
        {
            string jpg = Path.Combine(Application.temporaryCachePath, "still_" + Mathf.Abs(key.GetHashCode()) + ".jpg");
            if (!File.Exists(jpg))
            {
                string zip = SaveManager.SAVE_FOLDER_THEMES + themeZipName;
                if (!File.Exists(zip)) return null;
                string tmp = Path.Combine(Application.temporaryCachePath, "still_video" + Path.GetExtension(video.imageFileName));
                bool got = false;
                using (var za = ZipFile.OpenRead(zip))
                    foreach (var e in za.Entries)
                        if (e.Name == video.imageFileName) { e.ExtractToFile(tmp, true); got = true; break; }
                if (!got) return null;
                bool ok;
                try { using var helper = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV"); ok = helper.CallStatic<bool>("videoFrame", tmp, jpg); }
                finally { try { File.Delete(tmp); } catch (System.Exception) { } }
                if (!ok) return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(File.ReadAllBytes(jpg))) { Object.Destroy(tex); return null; }
            tex.name = "wallpaper"; tex.wrapMode = TextureWrapMode.Clamp;
            var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sp.name = "wallpaper";
            var still = new ThemeTexture { textureName = "wallpaper", imageFileName = video.imageFileName, filePath = jpg, isVideo = false, texture = sp, finishedLoading = true };
            cache[key] = still;
            return still;
        }
        catch (System.Exception e) { Debug.Log("[Theme] no still for the video wallpaper: " + e.Message); return null; }
    }
}
