using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DG.Tweening;
using Misc;
using SFB;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;
using UnityEngine.Video;

public class WallpaperController : MonoBehaviour
{
    public static WallpaperController _instance;
    public ThemeTexture currentWallpaper;
    private ThemeTexture localWallpaper;
    [FormerlySerializedAs("videoWallpaper")] public VideoPlayer videoPlayer;

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {
        LoadLocalWallpaper(); // we load it in case it's available
        AssignCurrentWallpaper(); // we assign it based on the theme or the settings
    }

    private void Update()
    {
        currentWallpaper?.UpdateGif();
        if (TVMode.IsTV) BlendGif();
    }

    // TV: a GIF background with few pictures a second (Wii U's: 10) looked choppy. The next picture is faded in over the
    // current one as its turn comes, so the motion looks smooth (one extra layer, only for such GIFs)
    private Graphic blendTarget; private RawImage blendLayer; private float blendLook; private bool? slowGif; private ThemeTexture blendFor;
    private static bool Shows(Graphic g, Sprite s) => g is Image im ? im.sprite == s : g is RawImage ri && s != null && ri.texture == s.texture;
    private void BlendGif()
    {
        var w = currentWallpaper;
        if (w != blendFor) { blendFor = w; slowGif = null; blendTarget = null; }
        if (w == null || !w.isGif || !w.finishedLoading || w.texture == null) { if (blendLayer != null) blendLayer.enabled = false; return; }
        if (slowGif == null) slowGif = w.SlowGif;
        if (slowGif == false) { if (blendLayer != null) blendLayer.enabled = false; return; }
        if ((blendTarget == null || !blendTarget.isActiveAndEnabled || !Shows(blendTarget, w.texture)) && Time.unscaledTime > blendLook)
        {
            blendLook = Time.unscaledTime + 2f;
            blendTarget = null;
            foreach (var g in FindObjectsOfType<Graphic>())
                if (g != blendLayer && g.isActiveAndEnabled && Shows(g, w.texture)) { blendTarget = g; break; }
            if (blendTarget != null)
            {
                if (blendLayer == null)
                {
                    var go = new GameObject("Gif Blend", typeof(RectTransform), typeof(RawImage));
                    blendLayer = go.GetComponent<RawImage>(); blendLayer.raycastTarget = false;
                }
                var rt = blendLayer.rectTransform;
                rt.SetParent(blendTarget.transform, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; rt.SetAsLastSibling();
            }
        }
        if (blendTarget == null || blendLayer == null) return;
        var next = w.NextGifFrame;
        blendLayer.enabled = next != null && blendTarget.isActiveAndEnabled;
        if (!blendLayer.enabled) return;
        var tx = next.texture; var r = next.textureRect;
        blendLayer.texture = tx;
        var uv = new Rect(r.x / tx.width, r.y / tx.height, r.width / tx.width, r.height / tx.height);
        if (blendTarget is RawImage tri) { var u0 = tri.uvRect; uv = new Rect(uv.x + u0.x * uv.width, uv.y + u0.y * uv.height, uv.width * u0.width, uv.height * u0.height); }
        blendLayer.uvRect = uv;
        var c = blendTarget.color; c.a *= w.GifBlend; blendLayer.color = c;
    }

    private void AssignCurrentWallpaper()
    {
        if (ThemeController._instance.IsOpen() || PREFS.UseThemeWallpaper.GetBool())
        {
            // we replace it if we're inside the editor, or if the theme has one and we want to use it
            ThemeTexture wall = ThemeController._instance.currentTheme.wallpaper;
            // a TV plays the video too, unless it couldn't keep up with that theme's video before (TVVideoWall): then a still picture
            bool tvStill = wall != null && TVMode.IsTV && wall.isVideo && !TVVideoWall.Allowed(ThemeController._instance.currentTheme.GetImportedFileName());
            if (tvStill)
            {
                var still = TVStill.Get(wall, ThemeController._instance.currentTheme.GetImportedFileName()); // a picture from the video instead of playing it
                if (still != null) { currentWallpaper = still; return; }
            }
            if (wall != null && !tvStill && (wall.isVideo || wall.texture is not null))
            {
                currentWallpaper = wall;
                return;
            }
        }

        // if no wallpaper replacement found, we use our own local one
        currentWallpaper = localWallpaper;
    }

    public void RequestNewWallpaper(Theme targetTheme = null)
    {
        FileManager.RequestFile("Choose a new wallpaper", FILETYPE.IMAGES_WITH_GIF_AND_VIDEOS,path =>
        {
            if (targetTheme is null)
            {
                // it's our own wallpaper
                SaveLocalWallpaper(path);
                LoadLocalWallpaper();
            }
            else
            {
                // it's the theme's
                LoadThemeWallpaper(path, targetTheme);
            }
            PopupController.ShowPopup("popup.changedwallpaper");
        });
    }

    private void SaveLocalWallpaper(string path)
    {
        // we delete the local wallpaper, if it exists
        DeleteLocalWallpaper();
        
        // we copy the new one
        string ext = Path.GetExtension(path);
        string newPath = SaveManager.SAVE_FOLDER_WALLPAPER + ext;
        FileManager.CopyFile(path, newPath);
        
        // and we set it to our own now
        PREFS.UseThemeWallpaper.SetBool(false);
    }

    private void LoadThemeWallpaper(string path, Theme targetTheme)
    {
        bool isGif = Path.GetExtension(path).Equals(".gif");
        bool isVideo = FileManager.IsValidExtension(path, FILETYPE.VIDEO);
        Texture2D tex = new Texture2D(2, 2); // Empty texture
        tex.name = "wallpaper";

        // it's a theme wallpaper
        if (isVideo || isGif)
        {
            // we only replace it
            targetTheme.ReplaceTexture("wallpaper", path, isWallpaper: true, isGif: isGif, isVideo: isVideo);
        }
        else
        {
            // We load the image data
            byte[] rawData = FileBrowserHelpers.ReadBytesFromFile(path);
            tex.LoadImage(rawData, true);

            // Then we apply it to the image
            targetTheme.ReplaceTexture("wallpaper", path, isWallpaper: true);
        }

        StartCoroutine(_LoadThemeWallpaper(targetTheme));
    }

    private IEnumerator _LoadThemeWallpaper(Theme targetTheme)
    {
        ThemeController._instance.UpdateWallpaperName();
        
        // We wait until the theme is over and reset
        yield return new WaitUntil(() => targetTheme is null || targetTheme.FinishedLoading());
        ResetBackgrounds();
    }

    private void LoadLocalWallpaper()
    {
        // we try to find the ext
        string path = GetLocalWallpaperPath();
        if (string.IsNullOrEmpty(path))
        {
            // no local wallpaper.
            return;
        }
        string ext = Path.GetExtension(path);
        
        // we check if it's special
        bool isGif = Path.GetExtension(path).Equals(".gif");
        bool isVideo = FileManager.IsValidExtension(path, FILETYPE.VIDEO);
        
        // and we load it
        localWallpaper = new ThemeTexture();
        localWallpaper.imageFileName = "wallpaper" + ext;
        localWallpaper.textureName = "wallpaper";
        localWallpaper.isGif = isGif;
        localWallpaper.isVideo = isVideo;
        localWallpaper.SetupImage(SaveManager.SAVE_FOLDER);
        
        // and we refresh once it's ready
        StartCoroutine(_LoadLocalWallpaper(localWallpaper));
    }

    private IEnumerator _LoadLocalWallpaper(ThemeTexture texture)
    {
        ThemeController._instance.UpdateWallpaperName();
        
        // We wait until the theme is over and reset
        yield return new WaitUntil(() => texture.finishedLoading);
        ResetBackgrounds();
    }

    public void DeleteLocalWallpaper()
    {
        // we destroy our texture if it exists
        localWallpaper?.Unload();
        localWallpaper = null;
        
        // we search and delete the wallpaper file
        string path = GetLocalWallpaperPath();
        if (!string.IsNullOrEmpty(path))
        {
            File.Delete(path);
        }
        
        // we allow theme wallpapers again (in case they exist)
        PREFS.UseThemeWallpaper.SetBool(true);
        
        // and we reset the backgrounds
        ResetBackgrounds();
    }

    private string GetLocalWallpaperPath()
    {
        foreach (string ext in FileManager.GetAllowedExtensions(FILETYPE.IMAGES_WITH_GIF_AND_VIDEOS))
        {
            string path = SaveManager.SAVE_FOLDER_WALLPAPER + "." + ext;
            if (File.Exists(path))
            {
                // found default wallpaper
                return path;
            }
        }

        return null;
    }

    public void StopVideoWallpaper()
    {
        videoPlayer.Pause();
    }
    
    public void ResumeVideoWallpaper()
    {
        if (currentWallpaper != null && currentWallpaper.isVideo)
        {
            videoPlayer.Play();
        }
    }

    public void ResetBackgrounds()
    {
        // we reassign the wallpaper
        AssignCurrentWallpaper();
        
        // we check if we need to load a video wallpaper
        bool isVideo = currentWallpaper != null && currentWallpaper.isVideo;
        videoPlayer.sendFrameReadyEvents = isVideo && !TVMode.IsTV; // TV: TVVideoWall hands the picture over once (an event every frame costs the menu time)
        
        if (isVideo)
        {
            videoPlayer.url = currentWallpaper.filePath;
            videoPlayer.Play();
        }
        else
        {
            videoPlayer.Stop();
        }

        UpdateElements();
    }

    private void UpdateElements()
    {
        // now we reload the textures
        foreach (ThemedWallpaper wallpaper in FindObjectsOfType(typeof(ThemedWallpaper), true))
        {
            wallpaper.UpdateElement();
        }
    }

    public void OnClickNewWallpaper()
    {
        RequestNewWallpaper();
    }

    public void OnClickResetWallpaper()
    {
        if (localWallpaper is null)
        {
            PopupController.ShowPopup("popup.nowallpaper");
        }
        else
        {
            PopupController.ShowPopup("popup.confirmresetwallpaper", () =>
            {
                PREFS.UseThemeWallpaper.SetBool(false);
                DeleteLocalWallpaper();
                PopupController.ShowPopup("popup.resetwallpaper");
            }, PopupController.ClosePopup);
        }
    }

}
