using UnityEngine;
using UnityEngine.UI;

// Procedural textures and the retro "old CRT with a glossy front" finish for the home tiles.
public static class TVFx
{
    private static Sprite rounded;
    private static Texture2D scan, gloss;
    private static Texture2D[] noise, noiseGlass;

    private static Sprite roundedBig;
    // classic slots: corners as round as the original Wii menu's tiles
    public static Sprite RoundedBig() { return Rounded(true); }

    public static Sprite Rounded(bool big = false)
    {
        if (big && roundedBig != null) return roundedBig;
        if (!big && rounded != null) return rounded;
        int n = big ? 128 : 64; float r = big ? 44f : 18f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - r);
                float qy = Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
            }
        tex.Apply();
        float bd = big ? 48f : 20f;
        var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(bd, bd, bd, bd));
        if (big) roundedBig = sp; else rounded = sp;
        return sp;
    }

    // Glass 2026 tile frame drawn at the tile's on-screen aspect (tiles are scaled wider than tall, so a sliced
    // sprite would give oval corners): a filled shape for masks and panes, or the bright bevel + cyan spill for the rim.
    // pad is the transparent margin around the shape, as a fraction of the frame's own width / height.
    private static readonly System.Collections.Generic.Dictionary<string, Sprite> frames = new System.Collections.Generic.Dictionary<string, Sprite>();
    // the 2026 tile frame as a share of the tile rect: the channel art's full box (measured on the projector), so
    // nothing the art places at its edges is cut (the Photo Channel title sits right at the top); empty slots share it
    public static readonly Vector2 FrameScale = new Vector2(0.96f, 1.19f);
    public const float FrameRadius = 0.12f; // corner radius as a share of the tile height (measured on the reference: ~27 of 224 px)
    public static Sprite Frame(float aspect, float padX, float padY, bool rim)
    {
        aspect = Mathf.Round(Mathf.Clamp(aspect, 0.5f, 4f) * 20f) / 20f;
        padX = Mathf.Round(padX * 200f) / 200f; padY = Mathf.Round(padY * 200f) / 200f;
        string key = aspect + "/" + padX + "/" + padY + "/" + rim;
        if (frames.TryGetValue(key, out var sp)) return sp;
        const int Hi = 160;
        int Wi = Mathf.RoundToInt(Hi * aspect), pw = Mathf.RoundToInt(padX * Wi), ph = Mathf.RoundToInt(padY * Hi);
        int W = Wi + pw * 2, H = Hi + ph * 2;
        float r = Hi * FrameRadius, glow = Mathf.Max(1f, Mathf.Min(pw, ph));
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - W * 0.5f) - (Wi * 0.5f - r);
                float qy = Mathf.Abs(y + 0.5f - H * 0.5f) - (Hi * 0.5f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                if (!rim) { px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)); continue; }
                // the reference's tile edge (measured close up): one thin bright ice-cyan line right on the art's border
                // (it also hides the mask's hard step), a little light just inside it, and a soft cyan bloom outside
                // centred on the mask's own edge (its stencil cuts at about +0.5 px), wide enough to cover that hard step
                float line = Mathf.Clamp01(1.4f - Mathf.Abs(d) / 1.3f);
                float inner = d < 0f ? Mathf.Clamp01(1f + d / 6f) : 0f;
                float outer = d > 0f ? Mathf.Clamp01(1f - d / glow) : 0f;
                var c = Color.Lerp(d > 0f ? new Color(0.38f, 0.76f, 1f) : new Color(0.92f, 0.98f, 1f), new Color(0.80f, 0.96f, 1f), line);
                float a = Mathf.Clamp01(Mathf.Max(line * 0.95f, outer * outer * 0.50f + inner * inner * 0.20f));
                px[y * W + x] = new Color(c.r, c.g, c.b, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        sp = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        frames[key] = sp;
        return sp;
    }

    // on-screen width / height of a rect (tiles carry a non-uniform scale)
    public static float ScreenAspect(RectTransform rt, Vector2 localSize)
    {
        var s = rt.lossyScale;
        return localSize.x * Mathf.Abs(s.x) / Mathf.Max(0.001f, localSize.y * Mathf.Abs(s.y));
    }

    // a dozen frames of Wii-style TV static, shared by every empty slot: opaque grey grain (light-biased like the
    // Wii's), each line a little brighter or darker, with a short sideways smear like an analogue picture
    // soft horizontal light bands for the Glass 2026 empty pane (the reference's slot glass is banded like this)
    private static Texture2D bands;
    public static Texture2D ScanBands()
    {
        if (bands != null) return bands;
        bands = new Texture2D(1, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
        // kept faint: stronger reads as stripes, not glass
        for (int y = 0; y < 8; y++) bands.SetPixel(0, y, new Color(0.92f, 0.96f, 1f, 0.105f * (0.5f + 0.5f * Mathf.Cos(y / 8f * 2f * Mathf.PI))));
        bands.Apply();
        return bands;
    }

    public const int NoiseFrames = 12;
    // glass: the original slot texture's look (empty1-4.png: crisp, mostly near-black or near-white specks), set
    // a little softer so it reads grey under the glass; classic keeps the light grain it always had
    public static Texture2D[] Noise(bool glass = false)
    {
        if (glass ? noiseGlass != null : noise != null) return glass ? noiseGlass : noise;
        var set = new Texture2D[NoiseFrames];
        var rnd = new System.Random(7);
        const int W = 168, H = 94; // ~2.5 px grain on a 1080p tile, like the Wii
        for (int f = 0; f < set.Length; f++)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float rowGain = glass ? 0.97f + 0.06f * (float) rnd.NextDouble() : 0.94f + 0.12f * (float) rnd.NextDouble(); // line-to-line shimmer (glass: barely, or it reads as woven cloth)
                float prev = (float) rnd.NextDouble();
                for (int x = 0; x < W; x++)
                {
                    float v = (float) rnd.NextDouble();
                    float c;
                    if (glass)
                    {
                        // two-toned like the Wii's: dark specks and light specks, few in between
                        float s = v < 0.46f ? 0.22f + 0.40f * v : 0.66f + 0.34f * v;
                        c = Mathf.Clamp01(s * rowGain);
                    }
                    else
                    {
                        v = Mathf.Lerp(v, prev, 0.12f); // a hint of horizontal smear, like a CRT
                        prev = v;
                        c = Mathf.Clamp01((0.20f + 0.80f * v) * rowGain);
                    }
                    px[y * W + x] = new Color(c, c, c * 1.02f, 1f);
                }
            }
            t.SetPixels(px);
            t.Apply();
            set[f] = t;
        }
        if (glass) noiseGlass = set; else noise = set;
        return set;
    }

    // the Wii's tile: white pane, thin cool-grey rim, rounded
    private static void MakePlate(Transform tile)
    {
        var go = new GameObject("TV Plate", typeof(RectTransform));
        var rt = (RectTransform) go.transform;
        rt.SetParent(tile, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        PlateImage(rt, "Rim", 0f, new Color(0.64f, 0.67f, 0.73f, 1f));
        PlateImage(rt, "Fill", 2.5f, new Color(0.97f, 0.975f, 0.985f, 1f));
        // faint cool shade toward the bottom so the pane isn't flat
        var sh = PlateImage(rt, "Shade", 2.5f, new Color(0.80f, 0.85f, 0.92f, 0.28f));
        sh.anchorMax = new Vector2(1f, 0.5f);
    }

    private static RectTransform PlateImage(RectTransform parent, string name, float inset, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        var im = go.GetComponent<Image>();
        im.sprite = Rounded(); im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 1f;
        im.color = c; im.raycastTarget = false;
        return rt;
    }

    private static Texture2D Scan()
    {
        if (scan != null) return scan;
        scan = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        scan.SetPixel(0, 0, new Color(0, 0, 0, 0f));
        scan.SetPixel(0, 1, new Color(0, 0, 0, 0f));
        scan.SetPixel(0, 2, new Color(0, 0, 0, 1f));
        scan.SetPixel(0, 3, new Color(0, 0, 0, 0.3f));
        scan.Apply();
        return scan;
    }

    public static Texture2D GlossTex()
    {
        if (gloss != null) return gloss;
        gloss = new Texture2D(1, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float t = y / 63f; // 0 bottom .. 1 top
            float a = t > 0.45f ? Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.55f) : 0f;
            gloss.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        gloss.Apply();
        return gloss;
    }

    private const string FxName = "TV Fx";

    // scanlines + a soft reflection over the union of the tile's own artwork
    public static void FitTileFx(Transform tile)
    {
        var rtTile = tile as RectTransform;
        if (rtTile == null) return;
        Transform fx = tile.Find(FxName);
        if (fx == null)
        {
            var go = new GameObject(FxName, typeof(RectTransform));
            fx = go.transform;
            fx.SetParent(tile, false);
            fx.SetAsLastSibling();
            Layer(fx, "Scan", Scan(), new Color(0f, 0f, 0.05f, 0.05f));
        }
        var oldPlate = tile.Find("TV Plate"); if (oldPlate != null) Object.Destroy(oldPlate.gameObject);
        // glass rim + sheen live in their own pane, sized like the Wii tile frame (not the artwork union)
        var gl = tile.Find("TV Gl") as RectTransform;
        if (gl == null)
        {
            var ggo = new GameObject("TV Gl", typeof(RectTransform));
            gl = (RectTransform) ggo.transform;
            gl.SetParent(tile, false);
            gl.anchorMin = gl.anchorMax = new Vector2(0.5f, 0.5f);
            var rgo = new GameObject("Rim", typeof(RectTransform), typeof(Image));
            var rrt = (RectTransform) rgo.transform;
            rrt.SetParent(gl, false);
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            var rim = rgo.GetComponent<Image>();
            rim.sprite = TVGlass.RimSoft(); rim.type = Image.Type.Sliced; rim.pixelsPerUnitMultiplier = 1f; rim.raycastTarget = false;
            rim.color = new Color(1f, 1f, 1f, 0.95f);
            Layer(gl, "Sheen", GlossTex(), new Color(1f, 1f, 1f, 0.18f));
            var shn = (RectTransform) gl.Find("Sheen");
            shn.anchorMin = new Vector2(0f, 0.5f); shn.offsetMin = new Vector2(TVGlass.RimPad, 0f); shn.offsetMax = -Vector2.one * TVGlass.RimPad;
        }
        // the glass rim sits on the visible art's edge (measured on the projector), so its bevel covers the clip
        Vector2 frame = Vector2.Scale(rtTile.rect.size, FrameScale);
        gl.sizeDelta = frame + Vector2.one * (TVGlass.RimPad * 2f);
        gl.anchoredPosition = Vector2.zero;
        gl.SetAsLastSibling();
        gl.gameObject.SetActive(TVGlass.On);
        if (TVGlass.On)
        {
            var rimImg = gl.Find("Rim").GetComponent<Image>();
            rimImg.sprite = Frame(ScreenAspect(rtTile, frame), TVGlass.RimPad / frame.x, TVGlass.RimPad / frame.y, true);
            rimImg.type = Image.Type.Simple;
        }
        RoundArt(tile, rtTile, frame);
        var sh = tile.Find("TV Shadow") as RectTransform;
        if (sh == null) sh = TVGlass.MakeShadow(tile, frame);
        sh.sizeDelta = frame + TVGlass.ShadowPad;
        sh.gameObject.SetActive(TVGlass.On);
        Transform plate = fx;
        // the channel art ships its own scan-line layers (lines.png, channel_overlay.png): the 2026 tiles are clean glass
        foreach (var h in tile.GetComponentsInChildren<AnimationHolder>(true))
            if (h.rawImage != null && h.animationInfo != null && (h.animationInfo.imageName == "lines.png" || h.animationInfo.imageName == "channel_overlay.png"))
                h.rawImage.canvasRenderer.SetAlpha(TVGlass.On ? 0f : 1f);
        fx.gameObject.SetActive(!TVGlass.On); // no scanlines on glass tiles

        // size to the artwork
        var cs = new Vector3[4];
        bool any = false;
        Vector2 min = default, max = default;
        foreach (var g in tile.GetComponentsInChildren<Graphic>(false))
        {
            if (g.transform.IsChildOf(fx) || g.transform.IsChildOf(gl) || g.transform.name == "TV Shadow" || !g.enabled || g.color.a < 0.5f) continue;
            var r = g.rectTransform;
            if (r.rect.width < 20f || r.rect.height < 20f) continue;
            r.GetWorldCorners(cs);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = rtTile.InverseTransformPoint(cs[i]);
                if (!any) { min = max = p; any = true; }
                else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            }
        }
        if (!any) return;
        var frt = (RectTransform) fx;
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.pivot = new Vector2(0.5f, 0.5f);
        Vector2 size = max - min;
        // keep to a sane Wii-tile frame even if a glow or border graphic is in the union
        frt.sizeDelta = new Vector2(Mathf.Min(size.x, rtTile.rect.width * 1.0f) - 16f, Mathf.Min(size.y, rtTile.rect.height * 1.15f) - 16f);
        frt.anchoredPosition = (min + max) * 0.5f - rtTile.rect.center;
        var scanImg = fx.Find("Scan").GetComponent<RawImage>();
        scanImg.uvRect = new Rect(0, 0, 1, Mathf.Max(1f, frt.sizeDelta.y / 4f));
    }

    // Glass 2026: the channel art is clipped by the tile's Mask, which is taller than the art. While the look is on,
    // the Mask is fitted to the art frame (its children keep their place on screen) and clips with the rounded frame;
    // the classic grey Border is dropped (the glass rim takes its place). Everything comes back when the look is off.
    private class MaskLook
    {
        public Sprite sprite; public Image.Type type; public Texture tex; public Rect uv;
        public Vector2 aMin, aMax, pivot, size, pos;
        public RectTransform[] kids; public Vector2[] kMin, kMax, kSize, kPos;
        public bool fitted;
    }
    private static readonly System.Collections.Generic.Dictionary<Graphic, MaskLook> maskOrig = new System.Collections.Generic.Dictionary<Graphic, MaskLook>();
    private static void RoundArt(Transform tile, RectTransform rtTile, Vector2 frame)
    {
        bool on = TVGlass.On;
        var m = tile.Find("Mask") as RectTransform;
        // the channel tiles clip with a RawImage (the classic rounded texture), other prefabs with an Image
        var mi = m != null && m.GetComponent<Mask>() != null ? m.GetComponent<Graphic>() : null;
        if (mi != null)
        {
            if (!maskOrig.TryGetValue(mi, out var o))
            {
                o = new MaskLook { aMin = m.anchorMin, aMax = m.anchorMax, pivot = m.pivot, size = m.sizeDelta, pos = m.anchoredPosition };
                if (mi is Image oi) { o.sprite = oi.sprite; o.type = oi.type; }
                if (mi is RawImage ori) { o.tex = ori.texture; o.uv = ori.uvRect; }
                int n = m.childCount;
                o.kids = new RectTransform[n]; o.kMin = new Vector2[n]; o.kMax = new Vector2[n]; o.kSize = new Vector2[n]; o.kPos = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    var k = m.GetChild(i) as RectTransform; o.kids[i] = k; if (k == null) continue;
                    o.kMin[i] = k.anchorMin; o.kMax[i] = k.anchorMax; o.kSize[i] = k.sizeDelta; o.kPos[i] = k.anchoredPosition;
                }
                maskOrig[mi] = o;
            }
            if (on && !o.fitted)
            {
                // pin each child to its current screen rect, then move the Mask onto the art frame
                var pins = new (Vector3 pos, Vector2 size)[o.kids.Length];
                for (int i = 0; i < o.kids.Length; i++) if (o.kids[i] != null) pins[i] = (o.kids[i].position, o.kids[i].rect.size);
                var ls = m.localScale;
                m.anchorMin = m.anchorMax = m.pivot = new Vector2(0.5f, 0.5f);
                m.sizeDelta = new Vector2(frame.x / Mathf.Max(0.001f, Mathf.Abs(ls.x)), frame.y / Mathf.Max(0.001f, Mathf.Abs(ls.y)));
                m.position = rtTile.TransformPoint(rtTile.rect.center);
                for (int i = 0; i < o.kids.Length; i++)
                {
                    var k = o.kids[i]; if (k == null) continue;
                    var pv = k.pivot;
                    k.anchorMin = k.anchorMax = new Vector2(0.5f, 0.5f);
                    k.sizeDelta = pins[i].size; k.position = pins[i].pos; k.pivot = pv;
                }
                var shape = Frame(ScreenAspect(rtTile, frame), 0f, 0f, false);
                if (mi is Image fi) { fi.sprite = shape; fi.type = Image.Type.Simple; }
                if (mi is RawImage fr) { fr.texture = shape.texture; fr.uvRect = new Rect(0f, 0f, 1f, 1f); }
                o.fitted = true;
            }
            else if (!on && o.fitted)
            {
                m.anchorMin = o.aMin; m.anchorMax = o.aMax; m.pivot = o.pivot; m.sizeDelta = o.size; m.anchoredPosition = o.pos;
                for (int i = 0; i < o.kids.Length; i++)
                {
                    var k = o.kids[i]; if (k == null) continue;
                    k.anchorMin = o.kMin[i]; k.anchorMax = o.kMax[i]; k.sizeDelta = o.kSize[i]; k.anchoredPosition = o.kPos[i];
                }
                if (mi is Image ri) { ri.sprite = o.sprite; ri.type = o.type; }
                if (mi is RawImage rr) { rr.texture = o.tex; rr.uvRect = o.uv; }
                o.fitted = false;
            }
        }
        var b = tile.Find("Border");
        var bg = b != null ? b.GetComponent<Graphic>() : null;
        if (bg != null) bg.canvasRenderer.SetAlpha(on ? 0f : 1f);
    }

    private static void Layer(Transform parent, string name, Texture2D tex, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var ri = go.GetComponent<RawImage>();
        ri.texture = tex; ri.color = c; ri.raycastTarget = false;
    }
}

// whole-screen finish: very fine scanlines, soft vignette and a slow drifting light sweep (one cheap overlay, no per-frame texture work)
public static class TVScreenFx
{
    private static RectTransform root, sweep;
    private static RawImage sweepImg, scanImg, vignette;
    private static float t;
    public static Transform Root => root; // the page-change bar picture goes just above this (it already contains it)

    // the page-change bar picture must not hold a frozen piece of the light sweep: the sweep is left out of that one frame,
    // and while the picture shows, a copy of the sweep (clipped to the picture's band) keeps moving over it
    public static void HideSweepForCapture(bool hide) { if (sweepImg != null) sweepImg.enabled = !hide; }
    private static RectTransform overClip, overSweep;
    private static RawImage overImg;
    public static void SweepOver(RectTransform band)
    {
        if (band == null || sweep == null) { if (overClip != null) overClip.gameObject.SetActive(false); return; }
        if (overClip == null)
        {
            var cgo = new GameObject("Still Bar Sweep", typeof(RectTransform), typeof(RectMask2D));
            overClip = (RectTransform) cgo.transform;
            var sgo = new GameObject("Sweep", typeof(RectTransform), typeof(RawImage));
            overSweep = (RectTransform) sgo.transform;
            overSweep.SetParent(overClip, false);
            overImg = sgo.GetComponent<RawImage>();
            overImg.texture = sweepImg.texture; overImg.raycastTarget = false;
        }
        overClip.SetParent(band.parent, false);
        overClip.anchorMin = band.anchorMin; overClip.anchorMax = band.anchorMax;
        overClip.offsetMin = band.offsetMin; overClip.offsetMax = band.offsetMax;
        overClip.SetSiblingIndex(band.GetSiblingIndex() + 1);
        overClip.gameObject.SetActive(true);
        FollowSweep();
    }
    private static void FollowSweep()
    {
        if (overClip == null || !overClip.gameObject.activeSelf || sweep == null) return;
        overSweep.anchorMin = overSweep.anchorMax = new Vector2(0.5f, 0.5f);
        overSweep.sizeDelta = sweep.sizeDelta;
        overSweep.rotation = sweep.rotation;
        overSweep.position = sweep.position;
        overImg.color = sweep.gameObject.activeInHierarchy ? sweepImg.color : Color.clear;
    }

    public static void Tick(float dt)
    {
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        if (root == null) Build(app.overlayCanvas);
        if (root == null) return;
        t += dt;
        float cycle = 16f, run = 4.5f;
        float p = Mathf.Clamp01((t % cycle) / run);
        float w = ((RectTransform) root.parent).rect.width;
        sweep.anchoredPosition = new Vector2(Mathf.Lerp(-0.35f, 1.35f, Mathf.SmoothStep(0f, 1f, p)) * w - w * 0.5f, 0f);
        sweepImg.color = new Color(1f, 1f, 1f, 0.08f * Mathf.Sin(p * Mathf.PI));
        long pg = TVFrameProbe.Begin();
        TVGlass.Tick(dt);
        TVFrameProbe.End("fx.glass", pg); pg = TVFrameProbe.Begin();
        TVHomeArrows.Tick(dt, root);
        TVFrameProbe.End("fx.arrows", pg);
        // the full-screen scanlines and light sweep sit over the tiles: Glass 2026 keeps the picture clean
        bool clean = !TVGlass.On;
        if (scanImg != null && scanImg.gameObject.activeSelf != clean) scanImg.gameObject.SetActive(clean);
        bool glint = clean && PREFS.TvScreenGlint.GetBool(); // Settings: "Screen light sweep" (the tiles' own shine stays)
        if (sweep != null && sweep.gameObject.activeSelf != glint) sweep.gameObject.SetActive(glint);
        if (vignette != null && vignette.gameObject.activeSelf != clean) vignette.gameObject.SetActive(clean);
        FollowSweep();
    }

    private static Texture2D Vignette()
    {
        var tex = new Texture2D(64, 36, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x / 63f - 0.5f) * 2f, dy = (y / 35f - 0.5f) * 2f;
                float d = Mathf.Clamp01((Mathf.Sqrt(dx * dx * 0.7f + dy * dy) - 0.55f) / 0.75f);
                tex.SetPixel(x, y, new Color(0.05f, 0.08f, 0.14f, d * d * 0.22f));
            }
        tex.Apply();
        return tex;
    }

    private static Texture2D Band()
    {
        var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 64; x++)
        {
            float a = Mathf.Sin(x / 63f * Mathf.PI);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        return tex;
    }

    private static RawImage Layer(Transform parent, string name, Texture tex, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var ri = go.GetComponent<RawImage>();
        ri.texture = tex; ri.color = c; ri.raycastTarget = false;
        return ri;
    }

    private static void Build(RectTransform canvas)
    {
        var go = new GameObject("TV Screen Fx", typeof(RectTransform), typeof(CanvasGroup));
        root = (RectTransform) go.transform;
        root.SetParent(canvas, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        root.SetAsFirstSibling();
        var cg = go.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false; cg.interactable = false;
        TVGlass.Build(root);

        var scan = new Texture2D(1, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        scan.SetPixel(0, 0, new Color(0, 0, 0, 0f)); scan.SetPixel(0, 1, new Color(0, 0, 0, 0f)); scan.SetPixel(0, 2, new Color(0, 0, 0.05f, 1f));
        scan.Apply();
        var s = scanImg = Layer(root, "Scanlines", scan, new Color(1f, 1f, 1f, 0.018f));
        s.uvRect = new Rect(0, 0, 1, Screen.height / 3f);
        vignette = Layer(root, "Vignette", Vignette(), Color.white);

        var sgo = new GameObject("Sweep", typeof(RectTransform), typeof(RawImage));
        sweep = (RectTransform) sgo.transform;
        sweep.SetParent(root, false);
        sweep.anchorMin = new Vector2(0.5f, 0.5f); sweep.anchorMax = new Vector2(0.5f, 0.5f);
        float h = ((RectTransform) canvas).rect.height, w = ((RectTransform) canvas).rect.width;
        sweep.sizeDelta = new Vector2(w * 0.22f, h * 1.6f);
        sweep.localRotation = Quaternion.Euler(0, 0, 14f);
        sweepImg = sgo.GetComponent<RawImage>();
        sweepImg.texture = Band(); sweepImg.color = new Color(1f, 1f, 1f, 0f); sweepImg.raycastTarget = false;
    }
}

// the Wii's page markers: a small tab on the screen edge with a blue arrow. Not a button, it only shows where more pages are.
public static class TVHomeArrows
{
    private static RectTransform left, right;
    private static RawImage leftImg, rightImg;
    private static Texture2D tex;
    private static float t;

    private static float TriDist(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
        return Mathf.Max(d1, Mathf.Max(d2, d3));
    }

    // signed distance to the edge's line, positive outside (triangle given counter-clockwise)
    private static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 e = (b - a).normalized;
        return Vector2.Dot(p - a, new Vector2(e.y, -e.x));
    }

    private static Texture2D Tex()
    {
        if (tex != null) return tex;
        const int W = 96, H = 128;
        tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        // the Wii's arrow is just a glossy cyan-blue triangle with a pale rim: no button behind it
        Vector2 a = new Vector2(14f, 12f), b = new Vector2(84f, 64f), c = new Vector2(14f, 116f); // counter-clockwise, pointing right
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = TriDist(p, a, b, c) - 4f;
                float alpha = Mathf.Clamp01(0.5f - d);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 2.5f) / 2.5f);
                float v = Mathf.Clamp01((p.y - 12f) / 104f);
                Color col = Color.Lerp(new Color(0.10f, 0.50f, 0.88f), new Color(0.38f, 0.82f, 0.97f), v); // deeper blue below, light on top
                col = Color.Lerp(col, new Color(0.80f, 0.95f, 1f), rim * 0.8f);
                px[y * W + x] = new Color(col.r, col.g, col.b, alpha);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static RectTransform Make(Transform parent, string name, bool isRight, out RawImage img)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        float x = isRight ? 1f : 0f;
        rt.anchorMin = rt.anchorMax = new Vector2(x, 0.62f);
        rt.pivot = new Vector2(1f, 0.5f);
        float h = ((RectTransform) parent).rect.height * 0.095f;
        rt.sizeDelta = new Vector2(h * 0.75f, h);
        if (!isRight) rt.localScale = new Vector3(-1f, 1f, 1f);
        img = go.GetComponent<RawImage>();
        img.texture = Tex(); img.raycastTarget = false; img.color = new Color(1f, 1f, 1f, 0f);
        return rt;
    }

    public static void Tick(float dt, RectTransform root)
    {
        var gc = GridController._instance; var cc = ChannelController._instance;
        if (root == null || gc == null || gc.currentGrid == null || cc == null) return;
        if (left == null)
        {
            left = Make(root, "Arrow Left", false, out leftImg);
            right = Make(root, "Arrow Right", true, out rightImg);
        }
        t += dt;
        var sc = SettingsController._instance;
        bool home = cc.currentChannel == null && !AppController.IsTransitioning() && !PopupController.IsPopupOpen()
                    && !(sc != null && sc.settingsView != null && sc.settingsView.activeInHierarchy)
                    && !(ShopController._instance != null && ShopController._instance.backButton != null && ShopController._instance.backButton.activeInHierarchy)
                    && !TVServerScreen.Open && !TVGameScreen.Open && !TVWeather.Open; // not over the Content Hub or the Channel Server / game screens
        int n = gc.GetNumberOfGrids(), cur = gc.currentGrid.number;
        Fade(leftImg, home && cur > 0, dt);
        Fade(rightImg, home && cur < n - 1, dt);
        // a slow nudge toward the edge
        float nudge = 5f * (0.5f + 0.5f * Mathf.Sin(t * 2.2f));
        // sits just inside the screen edge, like the Wii's
        float inset = ((RectTransform) root).rect.width * 0.012f;
        right.anchoredPosition = new Vector2(-inset + nudge * 0.5f, 0f);
        left.anchoredPosition = new Vector2(inset - nudge * 0.5f, 0f);
        float pulse = 1f + 0.05f * Mathf.Sin(t * 2.2f);
        right.localScale = new Vector3(pulse, pulse, 1f);
        left.localScale = new Vector3(-pulse, pulse, 1f);
    }

    private static void Fade(RawImage im, bool on, float dt)
    {
        var c = im.color;
        c.a = Mathf.MoveTowards(c.a, on ? 0.95f : 0f, dt * 4f);
        im.color = c;
    }
}
