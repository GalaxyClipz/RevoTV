using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Forecast Channel's preview (the screen with Start before it opens): a live sky that follows the time of day (dawn,
/// day, sunset, night with stars), the sun or moon on its arc, drifting clouds, the channel's name and your town, and a short
/// chime made right here in code (nothing to download, nothing to license). It is built in: the Channel Server and the
/// channel's edit page can't replace it (TVTiles.IsBuiltInPreview); the tile picture stays changeable.
/// </summary>
public class TVWeatherPreview : MonoBehaviour
{
    private RectTransform root;
    private RawImage sky;
    private Texture2D skyTex;
    private RectTransform sun, moon, stars;
    private Image sunImg, moonImg;
    private readonly RectTransform[] clouds = new RectTransform[4];
    private TextMeshProUGUI title, sub;
    private AudioSource chime;
    private static AudioClip chimeClip;
    private float builtAt, nextSky;
    private RectTransform screenRt;
    private readonly Vector3[] wc = new Vector3[4];
    // the holder zooms in from the tile: the preview is laid over the whole screen every frame, at an even scale
    private void Fit()
    {
        if (screenRt == null || root == null) return;
        screenRt.GetWorldCorners(wc);
        Vector3 ps = root.parent.lossyScale; if (Mathf.Abs(ps.x) < 1e-6f || Mathf.Abs(ps.y) < 1e-6f) return;
        float W = Mathf.Abs(wc[2].x - wc[0].x), H = Mathf.Abs(wc[2].y - wc[0].y);
        float us = H / 720f; // 720 units tall, whatever the screen
        root.localScale = new Vector3(us / ps.x, us / ps.y, 1f);
        root.sizeDelta = new Vector2(W / us, 720f);
        root.position = (wc[0] + wc[2]) * 0.5f;
    }

    public static void Ensure(MonoBehaviour host)
    {
        if (TVMode.IsTV && host.GetComponent<TVWeatherPreview>() == null) host.gameObject.AddComponent<TVWeatherPreview>();
    }

    private static bool Wanted()
    {
        var cc = ChannelController._instance;
        var ch = cc != null ? cc.currentChannel : null;
        return ch != null && ch.info != null && ch.info.packageName == TVWeather.Package && cc.channelView != null && cc.channelView.activeInHierarchy
               && !TVWeather.Open;
    }

    private void Update()
    {
        bool want = Wanted();
        if (want && root == null) Build();
        else if (!want && root != null) { Destroy(root.gameObject); root = null; }
        if (root == null) return;
        Fit();
        if (Time.unscaledTime >= nextSky) { nextSky = Time.unscaledTime + 30f; PaintSky(); }
        Animate();
    }

    // 0..24 local hour, with minutes
    private static float Hour() { var n = DateTime.Now; return n.Hour + n.Minute / 60f; }

    private static Color Lerp4(float hr, Color night, Color dawn, Color day, Color dusk)
    {
        // night <5, dawn 5-8, day 8-17, dusk 17-20, night >20 (soft blends between)
        if (hr < 4.5f || hr >= 21f) return night;
        if (hr < 6.5f) return Color.Lerp(night, dawn, (hr - 4.5f) / 2f);
        if (hr < 9f) return Color.Lerp(dawn, day, (hr - 6.5f) / 2.5f);
        if (hr < 16.5f) return day;
        if (hr < 19f) return Color.Lerp(day, dusk, (hr - 16.5f) / 2.5f);
        return Color.Lerp(dusk, night, (hr - 19f) / 2f);
    }

    private void Build()
    {
        var parent = AnimationController._instance != null ? AnimationController._instance.channelParent : null;
        if (parent == null) return;
        // the preview picture: the biggest picture under the preview's holder
        Transform host = parent.transform; float best = 0f;
        foreach (var g in parent.GetComponentsInChildren<Graphic>(false))
        {
            var r = g.rectTransform.rect; float a = Mathf.Abs(r.width * g.rectTransform.lossyScale.x * r.height * g.rectTransform.lossyScale.y);
            if (a > best) { best = a; host = g.transform; }
        }
        root = new GameObject("Forecast Preview", typeof(RectTransform)).GetComponent<RectTransform>();
        // drawn last in the holder, laid exactly over the preview picture, with even scale (the holder is squashed)
        // the whole screen (the preview fills it; the picture itself runs past the frame)
        root.SetParent(parent.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        var cc = ChannelController._instance; // the channel's frame (it zooms in from the tile)
        screenRt = cc != null && cc.channelObjects != null ? (RectTransform) cc.channelObjects.transform : (RectTransform) host;

        Fit();
        root.SetAsLastSibling();
        builtAt = Time.unscaledTime; nextSky = 0f;

        sky = Make<RawImage>("Sky", root);
        Stretch(sky.rectTransform);
        if (skyTex == null) { skyTex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; }
        sky.texture = skyTex;

        stars = new GameObject("Stars", typeof(RectTransform)).GetComponent<RectTransform>();
        stars.SetParent(root, false); Stretch(stars);
        var rnd = new System.Random(7);
        for (int i = 0; i < 40; i++)
        {
            var s = Make<Image>("Star", stars); s.sprite = TVServerScreen.DotSprite();
            float k = 0.004f + (float) rnd.NextDouble() * 0.006f;
            Place(s.rectTransform, (float) rnd.NextDouble(), 0.45f + (float) rnd.NextDouble() * 0.55f, k, k * 16f / 9f);
            s.color = new Color(1f, 1f, 0.9f, 0.5f + (float) rnd.NextDouble() * 0.5f);
        }

        // a soft glow behind the sun / moon, then the disc itself
        sun = Disc("Sun", new Color(1f, 0.86f, 0.3f), 0.16f, out sunImg);
        moon = Disc("Moon", new Color(0.95f, 0.96f, 1f), 0.11f, out moonImg);

        for (int i = 0; i < clouds.Length; i++)
        {
            var c = new GameObject("Cloud", typeof(RectTransform)).GetComponent<RectTransform>();
            c.SetParent(root, false);
            float sc = 0.7f + 0.15f * i;
            Place(c, -0.2f + i * 0.33f, 0.62f - (i % 2) * 0.16f, 0.2f * sc, 0.12f * sc);
            foreach (var (x, y, r) in new[] { (0.3f, 0.4f, 0.5f), (0.55f, 0.55f, 0.6f), (0.75f, 0.4f, 0.45f), (0.5f, 0.3f, 0.5f) })
            {
                var p = Make<Image>("Puff", c); p.sprite = TVServerScreen.DotSprite();
                var rt = p.rectTransform; rt.anchorMin = new Vector2(x - r * 0.5f, y - r * 0.5f * 1.6f); rt.anchorMax = new Vector2(x + r * 0.5f, y + r * 0.5f * 1.6f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            clouds[i] = c;
        }

        var font = TVServerScreen.SettingsFont();
        float h = 720f;
        title = TVServerScreen.Text(root, "Forecast Channel", font, h * 0.12f, Color.white);
        Place(title.rectTransform, 0.5f, 0.42f, 0.9f, 0.18f);
        title.fontStyle = FontStyles.Bold;
        title.outlineWidth = 0.18f; title.outlineColor = new Color32(10, 40, 90, 200);
        sub = TVServerScreen.Text(root, "", font, h * 0.055f, Color.white);
        Place(sub.rectTransform, 0.5f, 0.3f, 0.9f, 0.1f);
        sub.outlineWidth = 0.15f; sub.outlineColor = new Color32(10, 40, 90, 180);

        PaintSky();
        PlayChime();
    }

    private RectTransform Disc(string name, Color c, float size, out Image disc)
    {
        var g = Make<Image>(name, root); g.sprite = TVServerScreen.DotSprite();
        Place(g.rectTransform, 0.5f, 0.5f, size, size * 16f / 9f);
        var glow = Make<Image>("Glow", g.rectTransform); glow.sprite = TVServerScreen.DotSprite();
        glow.color = new Color(c.r, c.g, c.b, 0.16f);
        var grt = glow.rectTransform; grt.anchorMin = new Vector2(-0.3f, -0.3f); grt.anchorMax = new Vector2(1.3f, 1.3f); grt.offsetMin = grt.offsetMax = Vector2.zero;
        glow.transform.SetAsFirstSibling();
        // the disc on top of its own glow
        var d = Make<Image>("Disc", g.rectTransform); d.sprite = TVServerScreen.DotSprite(); Stretch(d.rectTransform); d.color = c;
        g.color = Color.clear;
        disc = d;
        return g.rectTransform;
    }

    private void PaintSky()
    {
        if (skyTex == null) return;
        float hr = Hour();
        Color top = Lerp4(hr, new Color(0.03f, 0.05f, 0.18f), new Color(0.32f, 0.36f, 0.72f), new Color(0.16f, 0.5f, 0.95f), new Color(0.28f, 0.22f, 0.55f));
        Color bottom = Lerp4(hr, new Color(0.1f, 0.12f, 0.3f), new Color(1f, 0.7f, 0.5f), new Color(0.72f, 0.9f, 1f), new Color(1f, 0.55f, 0.3f));
        for (int y = 0; y < 64; y++) skyTex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
        skyTex.Apply();
        if (sub != null)
        {
            string town = TVWeather.TownForPreview;
            string greet = hr < 5f ? "Good night" : hr < 12f ? "Good morning" : hr < 17f ? "Good afternoon" : hr < 22f ? "Good evening" : "Good night";
            sub.text = greet + (string.IsNullOrEmpty(town) ? "" : "  ·  " + town) + "  ·  " + DateTime.Now.ToString("dddd, MMMM d");
        }
    }

    private void Animate()
    {
        float hr = Hour(), t = Time.unscaledTime - builtAt;
        bool day = hr >= 6f && hr < 19.5f;
        // the sun crosses from left to right between 6:00 and 19:30, the moon through the night
        float k = day ? Mathf.InverseLerp(6f, 19.5f, hr) : Mathf.InverseLerp(0f, 10.5f, hr >= 19.5f ? hr - 19.5f : hr + 4.5f);
        var body = day ? sun : moon;
        sun.gameObject.SetActive(day); moon.gameObject.SetActive(!day);
        float x = Mathf.Lerp(0.12f, 0.88f, k), y = 0.42f + Mathf.Sin(k * Mathf.PI) * 0.42f;
        var a = new Vector2(x, y);
        float hs = (body.anchorMax.x - body.anchorMin.x) * 0.5f, vs = (body.anchorMax.y - body.anchorMin.y) * 0.5f;
        body.anchorMin = a - new Vector2(hs, vs); body.anchorMax = a + new Vector2(hs, vs);
        if (day) sunImg.transform.parent.localRotation = Quaternion.Euler(0f, 0f, -t * 6f);
        float starA = day ? 0f : 1f;
        stars.gameObject.SetActive(starA > 0f);
        if (!day) for (int i = 0; i < stars.childCount; i++) { var g = stars.GetChild(i).GetComponent<Image>(); var c = g.color; c.a = 0.45f + 0.45f * Mathf.Sin(t * 2.2f + i * 1.7f); g.color = c; }
        Color cloudC = day ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.55f, 0.6f, 0.75f, 0.55f);
        if (hr >= 17f && hr < 20f) cloudC = new Color(1f, 0.82f, 0.7f, 0.9f);
        for (int i = 0; i < clouds.Length; i++)
        {
            var c = clouds[i]; if (c == null) continue;
            float w = c.anchorMax.x - c.anchorMin.x;
            float cx = Mathf.Repeat(-0.2f + i * 0.33f + t * (0.012f + 0.004f * i), 1.4f) - 0.2f;
            c.anchorMin = new Vector2(cx, c.anchorMin.y); c.anchorMax = new Vector2(cx + w, c.anchorMax.y);
            for (int j = 0; j < c.childCount; j++) c.GetChild(j).GetComponent<Image>().color = cloudC;
        }
        // the title floats gently, like the Wii's own channel previews
        if (title != null) title.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 1.4f) * 6f);
    }

    // a short, soft chime: a rising bell arpeggio and a held chord, in the style of an old console menu
    private void PlayChime()
    {
        // RevoTV's own smooth-jazz intro (electric piano, bass, vibraphone; made for RevoTV, nothing sampled); the made-in-code
        // chime below is only the fallback
        if (chimeClip == null) chimeClip = Resources.Load<AudioClip>("ForecastChime");
        if (chimeClip == null)
        {
            const int rate = 32000; float len = 2.6f;
            int n = (int) (rate * len);
            var data = new float[n];
            // C major 9: C5 E5 G5 B5 D6, then the chord rings
            float[] f = { 523.25f, 659.25f, 783.99f, 987.77f, 1174.66f };
            float[] at = { 0f, 0.12f, 0.24f, 0.36f, 0.5f };
            for (int v = 0; v < f.Length; v++)
            {
                int s0 = (int) (at[v] * rate);
                for (int i = s0; i < n; i++)
                {
                    float tt = (i - s0) / (float) rate;
                    float env = Mathf.Min(1f, tt * 200f) * Mathf.Exp(-tt * 2.2f);
                    float w = Mathf.Sin(2f * Mathf.PI * f[v] * tt) + 0.35f * Mathf.Sin(2f * Mathf.PI * f[v] * 2f * tt) * Mathf.Exp(-tt * 6f)
                              + 0.12f * Mathf.Sin(2f * Mathf.PI * f[v] * 3.01f * tt) * Mathf.Exp(-tt * 9f);
                    data[i] += w * env * 0.16f;
                }
            }
            // a soft low note under it
            for (int i = 0; i < n; i++) { float tt = i / (float) rate; data[i] += Mathf.Sin(2f * Mathf.PI * 130.81f * tt) * Mathf.Min(1f, tt * 20f) * Mathf.Exp(-tt * 1.3f) * 0.12f; }
            for (int i = n - rate / 5; i < n; i++) data[i] *= (n - i) / (rate / 5f); // fade out
            chimeClip = AudioClip.Create("ForecastChime", n, 1, rate, false);
            chimeClip.SetData(data, 0);
        }
        if (chime == null) { chime = gameObject.AddComponent<AudioSource>(); chime.playOnAwake = false; chime.spatialBlend = 0f; }
        chime.volume = 0.7f;
        chime.PlayOneShot(chimeClip);
    }

    private static T Make<T>(string name, Transform parent) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(T));
        go.transform.SetParent(parent, false);
        var g = go.GetComponent<T>(); g.raycastTarget = false;
        return g;
    }

    private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    private static void Place(RectTransform rt, float cx, float cy, float w, float h)
    {
        rt.anchorMin = new Vector2(cx - w * 0.5f, cy - h * 0.5f); rt.anchorMax = new Vector2(cx + w * 0.5f, cy + h * 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
