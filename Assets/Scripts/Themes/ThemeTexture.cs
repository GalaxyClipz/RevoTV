using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ThreeDISevenZeroR.UnityGifDecoder;
using UnityEngine;
using UnityEngine.Networking;

public class ThemeTexture
{
    public string textureName;
    public string imageFileName;
    public string filePath;
    [JsonIgnore] public Sprite texture;
    [JsonIgnore] public bool finishedLoading;
    
    public bool isGif = false;
    public bool isVideo = false;
    [JsonIgnore] public List<Sprite> gifFrames = new List<Sprite>();
    [JsonIgnore] public List<float> gifFrameDelays = new List<float>();
    public int gifFrame = 0;
    public float gifDelay = 0;
    
    private Theme theme;

    public void LinkTheme(Theme theme)
    {
        this.theme = theme;
    }
    
    public void SetupImage(string extractedPath)
    {
        // Now we load it, whether it's a gif or image
        filePath = extractedPath + "/" + imageFileName;
        if (!File.Exists(filePath))
        {
            Debug.Log($"Texture file at {filePath} doesn't exist!");
            finishedLoading = true;
            return;
        }

        if (isVideo)
        {
            // we only need the filepath, done
            finishedLoading = true;
            return;
        }
        
        if (isGif)
        {
            SyncContext.RunOnUnityThread(LoadGif);
            return;
        }
        try
        {
            SyncContext.RunOnUnityThread(() => { AppController._instance.StartCoroutine(LoadImage(extractedPath)); });
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }
    
    private void LoadGif()
    {
        if (TVMode.IsTV) { AppController._instance.StartCoroutine(LoadGifTv()); return; }
        List<Texture2D> myFrames = new List<Texture2D>();
        StaticUtils.LoadGif(filePath, myFrames, gifFrameDelays);
        foreach(Texture2D tex in myFrames)
        {
            tex.name = textureName;
            Sprite sprite = Sprite.Create(tex, new Rect(0.0f, 0.0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sprite.name = textureName;
            gifFrames.Add(sprite);
            tex.MarkAsNonReadable();
        }
        myFrames.Clear();
        
        texture = gifFrames.Count > 0 ? gifFrames[0] : null;
        finishedLoading = true;
    }

    // TV: a theme's animated pictures are decoded off the main thread and made smaller there (a big theme's GIFs froze the menu for
    // many seconds while every frame was decoded at full size); the frames then become textures a few per frame
    private System.Collections.IEnumerator LoadGifTv()
    {
        string fp = filePath; int cap = textureName == "wallpaper" ? 1280 : 512;
        var frames = new List<(Color32[] px, int w, int h, float d)>();
        var job = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using (var gs = new GifStream(fp))
                    while (gs.HasMoreData)
                    {
                        if (gs.CurrentToken != GifStream.Token.Image) { gs.SkipToken(); continue; }
                        var im = gs.ReadImage();
                        int w = gs.Header.width, h = gs.Header.height, m = Mathf.Max(w, h);
                        Color32[] px;
                        if (m <= cap) px = (Color32[]) im.colors.Clone();
                        else
                        {
                            float k = (float) m / cap; int nw = Mathf.Max(1, (int) (w / k)), nh = Mathf.Max(1, (int) (h / k));
                            px = new Color32[nw * nh]; var src = im.colors;
                            for (int y = 0; y < nh; y++) { int sy = Mathf.Min(h - 1, (int) ((y + 0.5f) * k)) * w; for (int x = 0; x < nw; x++) px[y * nw + x] = src[sy + Mathf.Min(w - 1, (int) ((x + 0.5f) * k))]; }
                            w = nw; h = nh;
                        }
                        frames.Add((px, w, h, im.SafeDelaySeconds));
                    }
            }
            catch (Exception e) { Debug.Log("Error while loading gif: " + e); }
        });
        while (!job.IsCompleted) yield return null;
        int n = 0;
        foreach (var f in frames)
        {
            var tex = new Texture2D(f.w, f.h, TextureFormat.RGBA32, false) { name = textureName };
            tex.SetPixels32(f.px); tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0f, 0f, f.w, f.h), new Vector2(0.5f, 0.5f)); sp.name = textureName;
            gifFrames.Add(sp); gifFrameDelays.Add(f.d);
            if (++n % 4 == 0) yield return null;
        }
        texture = gifFrames.Count > 0 ? gifFrames[0] : null;
        finishedLoading = true;
    }

    // a TV (1.7 GB, drawn at 720p) never needs a theme's 1024-2400 px pictures at full size: a big theme took 600+ MB and the whole projector
    // lagged. Pictures are redrawn smaller (wallpapers up to 1280 px, everything else 512), without mip levels, and the big original is freed.
    private Texture2D TvShrink(Texture2D src)
    {
        int cap = textureName == "wallpaper" ? 1280 : 512;
        int w = src.width, h = src.height, m = Mathf.Max(w, h);
        if (m <= cap) { try { src.Apply(false, true); } catch (System.Exception) { } return src; }
        float k = (float) cap / m;
        int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
        var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var small = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        small.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
        small.Apply(false, true);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        GameObject.Destroy(src);
        return small;
    }

    private IEnumerator LoadImage(string path)
    {
        string finalPath = "file://" + path + "/" + imageFileName;
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(finalPath, !TVMode.IsTV))
        {
            yield return uwr.SendWebRequest();

            if (uwr.isNetworkError || uwr.isHttpError)
            {
                Debug.Log(uwr.error);
                texture = null;
            }
            else
            {
                Texture2D tex = ((DownloadHandlerTexture)uwr.downloadHandler).texture;
                if (tex != null && TVMode.IsTV) tex = TvShrink(tex);
                if (tex != null)
                {
                    texture = Sprite.Create(tex, new Rect(0.0f, 0.0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    texture.name = textureName;
                    texture.texture.name = textureName;
                }
            }

            finishedLoading = true;
        }
    }

    public void SaveImage(string extractedPath)
    {
        string newPath = extractedPath + "/" + imageFileName;
        if (!File.Exists(newPath))
        {
            FileManager.CopyFile(filePath, newPath, false);
        }

        filePath = theme?.MoveToDeserializedFolder(filePath) ?? filePath;
    }

    public void Unload()
    {
        GameObject.Destroy(texture);
        texture = null;
        gifFrameDelays.Clear();
        gifFrames.ForEach(GameObject.Destroy);
        gifFrames.Clear();
    }
    
    private int gifShown; private float gifClock; private bool gifChecked;
    // for blending a slow GIF (Wii U's background is 10 pictures a second): the picture after this one, and how far along we are
    public Sprite NextGifFrame => isGif && gifFrames.Count > 1 ? gifFrames[(gifFrame + 1) % gifFrames.Count] : null;
    public float GifBlend => isGif && gifFrameDelays.Count > 0 ? Mathf.Clamp01(gifDelay / Mathf.Max(0.02f, gifFrameDelays[Mathf.Min(gifFrame, gifFrameDelays.Count - 1)])) : 0f;
    public bool SlowGif
    {
        get
        {
            if (!isGif || gifFrames.Count < 2 || gifFrameDelays.Count == 0) return false;
            float total = 0f; foreach (var d in gifFrameDelays) total += Mathf.Max(0.02f, d);
            return gifFrames.Count / total < 16f;
        }
    }
    public void UpdateGif()
    {
        if (isGif && finishedLoading)
        {
            if (gifFrames.Count == 0 || gifFrameDelays.Count == 0) return;
            // keep the GIF on real time: when the menu draws fewer frames a second than the GIF has (a TV at 20), skip ahead
            // instead of showing every frame late (it played at half speed and stuttered), and keep the leftover time
            gifDelay += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            int start = gifFrame, guard = 0;
            while (guard++ < 64)
            {
                float targetDelay = Mathf.Max(0.02f, gifFrameDelays[Mathf.Min(gifFrame, gifFrameDelays.Count - 1)]);
                if (gifDelay < targetDelay) break;
                gifDelay -= targetDelay;
                gifFrame = gifFrame >= gifFrames.Count - 1 ? 0 : gifFrame + 1;
            }
            if (gifFrame != start) { texture = gifFrames[gifFrame]; gifShown++; }
            // once per GIF: how fast it really plays against how fast it should (TV log, for checking)
            if (TVMode.IsTV && !gifChecked)
            {
                gifClock += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
                if (gifClock > 8f)
                {
                    gifChecked = true;
                    float total = 0f; foreach (var d in gifFrameDelays) total += Mathf.Max(0.02f, d);
                    Debug.Log("[GIF] " + imageFileName + ": " + (gifShown / gifClock).ToString("0.0") + " pictures/s shown, plays at real speed (" + (gifFrames.Count / Mathf.Max(0.01f, total)).ToString("0.0") + " frames/s in the file, menu at " + (1f / Mathf.Max(0.001f, Time.smoothDeltaTime)).ToString("0") + " fps)");
                }
            }
        }
    }
    
    public void RefreshGif()
    {
        if (isGif)
        {
            gifFrame = 0;
            gifDelay = 0;
            if (HasGifFrames())
            {
                texture = gifFrames[0];
            }
        }
    }
    
    public bool HasGifFrames()
    {
        return gifFrames.Count != 0;
    }
}