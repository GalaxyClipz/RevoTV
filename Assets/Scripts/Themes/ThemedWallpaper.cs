using System.Collections;
using System.Collections.Generic;
using TriInspector;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

public class ThemedWallpaper : ThemedImage
{
    private Texture wallpaperTexture;
    private float originalWidth;
    private float originalHeight;
    private bool loadedRect = false;

    private bool subscribedToFrameEvents = false;
    private bool isUsingVideo = false;

    protected override void FindElements()
    {
        base.FindElements();

        if (!loadedRect && rawImage is not null)
        {
            originalWidth = rawImage.uvRect.width;
            originalHeight = rawImage.uvRect.height;
            loadedRect = true;
        }
    }

    // every wallpaper showing the video (TV: TVVideoWall hands them the video's picture once, instead of an event every frame)
    private static readonly HashSet<ThemedWallpaper> videoWalls = new HashSet<ThemedWallpaper>();
    public static void VideoTextureChanged()
    {
        foreach (var w in videoWalls) if (w != null) w.LoadVideoFrame(null, 0);
    }

    private void LoadVideoFrame(VideoPlayer source, long frameIdx)
    {
        if (!isUsingVideo || rawImage == null)
        {
            return;
        }

        var tex = WallpaperController._instance.videoPlayer.texture;
        if (tex == null) return; // keep the last picture rather than none (an empty picture is drawn white)
        wallpaperTexture = tex;
        if (rawImage.texture != tex) rawImage.texture = tex;
        if (rawImage.color != Color.white) rawImage.color = Color.white;
    }

    [Button]
    public override void UpdateTexture()
    {
        base.UpdateTexture();

        themeTexture = WallpaperController._instance.currentWallpaper;
        wallpaperTexture = null;
        isUsingVideo = themeTexture != null && themeTexture.isVideo;
        if (themeTexture != null)
        {
            if (isUsingVideo)
            {
                // the video's picture if it already has one; otherwise the first frame brings it
                videoWalls.Add(this);
                wallpaperTexture = WallpaperController._instance.videoPlayer.texture;
                // we subscribe to retrieve from the texture and leave
                if (!subscribedToFrameEvents)
                {
                    subscribedToFrameEvents = true;
                    WallpaperController._instance.videoPlayer.frameReady += LoadVideoFrame;
                }
            }
            else if(themeTexture.texture != null)
            {
                // we retrieve the wallpaper regular texture
                wallpaperTexture = themeTexture.texture.texture;
            }
            else
            {
                // no wallpaper
                wallpaperTexture = null;
            }
        }
        
        
        // we need to re-replace it there's a custom wallpaper
        if ((wallpaperTexture is not null || isUsingVideo) && rawImage is not null)
        {
            rawImage.texture = wallpaperTexture;

            // we also reset the color (a video with no frame yet stays see-through: an empty picture is drawn white, which
            // flashed the whole background white for a moment when a page's wallpaper was refreshed)
            rawImage.color = isUsingVideo && wallpaperTexture == null ? new Color(1f, 1f, 1f, 0f) : Color.white;
        }

        // we need to remove the tiling we did for the background, since now it's a custom texture
        if (rawImage is not null)
        {
            Sprite sprite = ThemeController.GetTexture(rawImage.texture);
            if (sprite is not null || wallpaperTexture is not null || isUsingVideo)
            {
                rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            else
            {
                rawImage.uvRect = new Rect(0f, 0f, originalWidth, originalHeight);
            }
        }
    }
}
