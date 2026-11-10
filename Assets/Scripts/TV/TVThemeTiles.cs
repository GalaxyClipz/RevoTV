using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// A custom theme can give the home tiles another shape (rounded squares, circles) through its tile mask picture. The channel art is
/// shrunk to the biggest picture that fits inside that shape, so it isn't cropped or stretched, and the hover light follows the shape.
/// The normal look (and Glass 2026) is never touched: art is only moved while a custom shape is on, and only put back if it is still
/// where this class left it.
/// </summary>
public static class TVThemeTiles
{
    private struct Shape
    {
        public Vector4 box;   // visible width, height, centre x, centre y of the drawn shape (fractions of the mask box)
        public float fit;     // biggest scale of that box that stays inside the shape (1 = the art already fits)
        public float radius;  // corner radius as a fraction of the shorter side (0.5 = circle / pill)
    }

    private struct Saved { public Vector3 scale; public Vector2 pos; public Vector3 wantScale; public Vector2 wantPos; }

    private static readonly Dictionary<string, Shape> shapes = new Dictionary<string, Shape>();
    private static readonly Dictionary<RectTransform, Saved> saved = new Dictionary<RectTransform, Saved>();
    private static readonly Shape Full = new Shape { box = new Vector4(1f, 1f, 0f, 0f), fit = 1f, radius = 0f };
    private static readonly Vector3[] corners = new Vector3[4];

    // the radius the hover light should use for the tile just measured (0 = its usual one)
    public static float GlowRadius { get; private set; }

    private static Shape Current()
    {
        if (!TVMode.IsTV || TVGlass.On) return Full;
        var tc = ThemeController._instance;
        var cur = tc != null ? tc.GetCurrentTheme() : null;
        if (cur == null || !cur.FinishedLoading() || tc.IsDefaultLook()) return Full;
        var tt = cur.GetThemeTexture("channel_mask");
        if (tt == null || tt.isGif || tt.isVideo || string.IsNullOrEmpty(tt.filePath) || !File.Exists(tt.filePath)) return Full;
        if (shapes.TryGetValue(tt.filePath, out var known)) return known;
        var v = Full;
        Texture2D t = null;
        try
        {
            t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (t.LoadImage(File.ReadAllBytes(tt.filePath), false)) v = Measure(t);
        }
        catch (System.Exception e) { Debug.Log("[Tiles] mask shape: " + e.Message); }
        if (t != null) Object.Destroy(t);
        shapes[tt.filePath] = v;
        return v;
    }

    private static Shape Measure(Texture2D t)
    {
        var px = t.GetPixels32();
        int W = t.width, H = t.height;
        System.Func<int, int, bool> solid = (x, y) => x >= 0 && y >= 0 && x < W && y < H && px[y * W + x].a > 128;
        int x0 = W, y0 = H, x1 = -1, y1 = -1, st = Mathf.Max(1, Mathf.Min(W, H) / 256);
        for (int y = 0; y < H; y += st)
            for (int x = 0; x < W; x += st)
                if (px[y * W + x].a > 128)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        if (x1 < x0 || y1 < y0) return Full;
        float bw = x1 - x0 + 1, bh = y1 - y0 + 1, cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        // biggest copy of the box (same middle, same proportions) whose corners and edge middles all sit on the drawn shape
        float lo = 0.2f, hi = 1f;
        for (int i = 0; i < 12; i++)
        {
            float s = (lo + hi) * 0.5f, hx = bw * 0.5f * s - 1f, hy = bh * 0.5f * s - 1f;
            bool ok = solid(Mathf.RoundToInt(cx - hx), Mathf.RoundToInt(cy - hy)) && solid(Mathf.RoundToInt(cx + hx), Mathf.RoundToInt(cy - hy))
                   && solid(Mathf.RoundToInt(cx - hx), Mathf.RoundToInt(cy + hy)) && solid(Mathf.RoundToInt(cx + hx), Mathf.RoundToInt(cy + hy))
                   && solid(Mathf.RoundToInt(cx), Mathf.RoundToInt(cy - hy)) && solid(Mathf.RoundToInt(cx), Mathf.RoundToInt(cy + hy))
                   && solid(Mathf.RoundToInt(cx - hx), Mathf.RoundToInt(cy)) && solid(Mathf.RoundToInt(cx + hx), Mathf.RoundToInt(cy));
            if (ok) lo = s; else hi = s;
        }
        // corner roundness: how far in along the diagonal the shape starts (a circle of radius R leaves 0.29 R at each axis)
        int gap = 0;
        while (gap < bw * 0.5f && !solid(x0 + gap, y0 + gap)) gap++;
        float side = Mathf.Min(bw, bh), rad = Mathf.Clamp(gap * 3.414f / Mathf.Max(1f, side), 0f, 0.5f);
        return new Shape
        {
            box = new Vector4(Mathf.Clamp01(bw / W), Mathf.Clamp01(bh / H), (cx + 0.5f) / W - 0.5f, (cy + 0.5f) / H - 0.5f),
            fit = Mathf.Min(1f, lo),
            radius = rad,
        };
    }

    // the art on a tile is scaled to fit inside the custom shape; put back only if nobody else has moved it since
    public static void Fit(Transform tile)
    {
        var m = tile.Find("Mask") as RectTransform;
        if (m == null) return;
        var sh = Current();
        // the drawn shape's visible area (a box inside the mask picture, then the biggest copy of it that stays inside the shape): the art is
        // stretched to exactly that, so nothing is cropped away and nothing is left bare; a mask that shows the whole tile changes nothing
        float cover = Mathf.Clamp(Mathf.Max(sh.box.x, sh.box.y), 0.2f, 1f); // covers the shape's box: filled edge to edge, the corners cut by the shape
        Vector2 sv2 = new Vector2(cover, cover); // uniform, and only for shapes the art really sticks out of (circles, pills): a rectangle-like mask is left exactly as the theme draws it
        // round shapes, and masks that show only part of the tile (a Wii U theme's mask shows the middle 65%: the art was cut
        // off on every side, "hoto Channe"); a mask that shows (nearly) the whole tile is left exactly as the theme draws it
        bool on = sh.fit < 0.9f || Mathf.Max(sh.box.x, sh.box.y) < 0.95f;
        for (int i = 0; i < m.childCount; i++)
        {
            var k = m.GetChild(i) as RectTransform;
            if (k == null || k.GetComponentInChildren<UnityEngine.UI.Button>(true) != null) continue; // never the Edit / Move / Delete menu
            bool have = saved.TryGetValue(k, out var sv);
            bool untouched = have && (k.localScale - sv.wantScale).sqrMagnitude < 1e-6f && (k.anchoredPosition - sv.wantPos).sqrMagnitude < 0.04f;
            if (!on)
            {
                if (have)
                {
                    if (untouched) { k.localScale = sv.scale; k.anchoredPosition = sv.pos; }
                    saved.Remove(k);
                }
                continue;
            }
            Vector3 baseScale = untouched ? sv.scale : k.localScale;
            Vector2 basePos = untouched ? sv.pos : k.anchoredPosition;
            Vector3 want = new Vector3(baseScale.x * sv2.x, baseScale.y * sv2.y, baseScale.z);
            // scaled about its pivot, so the picture's middle is held where it was (then moved to the middle of the visible shape)
            Vector2 mid = new Vector2((0.5f - k.pivot.x) * k.rect.width * baseScale.x, (0.5f - k.pivot.y) * k.rect.height * baseScale.y);
            Vector2 pos = basePos + new Vector2(mid.x * (1f - sv2.x), mid.y * (1f - sv2.y)) + new Vector2(sh.box.z * m.rect.width, sh.box.w * m.rect.height);
            if (!untouched || (k.localScale - want).sqrMagnitude > 1e-6f) k.localScale = want;
            if (!untouched || (k.anchoredPosition - pos).sqrMagnitude > 0.01f) k.anchoredPosition = pos;
            saved[k] = new Saved { scale = baseScale, pos = basePos, wantScale = k.localScale, wantPos = k.anchoredPosition };
        }
    }

    // the hover light hugs the drawn tile shape, not the bigger box around it (screen pixels)
    public static bool GlowRect(Transform focus, out Rect rect)
    {
        rect = default;
        GlowRadius = 0f;
        var sh = Current();
        if (sh.fit >= 0.9f && sh.radius < 0.25f && Mathf.Max(sh.box.x, sh.box.y) >= 0.95f) return false; // ordinary tile shape: the usual light
        if (focus.name != "Click") return false; // only the tile itself, never the buttons of its Edit / Move / Delete menu
        var ah = focus.GetComponentInParent<AppHandler>();
        var m = ah != null ? ah.transform.Find("Mask") as RectTransform : null;
        if (m == null) return false;
        m.GetWorldCorners(corners);
        var canvas = m.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        Vector2 size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        Vector2 center = (a + b) * 0.5f + new Vector2(sh.box.z * size.x, sh.box.w * size.y);
        size = new Vector2(size.x * sh.box.x, size.y * sh.box.y);
        rect = new Rect(center - size * 0.5f, size);
        GlowRadius = sh.radius;
        return true;
    }
}
