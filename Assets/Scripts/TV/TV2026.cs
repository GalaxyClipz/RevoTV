using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Glass 2026, the parts that live inside the menu itself (not the overlay):
// a lit backdrop behind the grid, a dark glass bottom bar, and a white digital clock drawn from segments.
public static class TV2026
{
    private static Transform mainMenu, bar, date;
    private static RawImage backdrop, barTone;
    private static Image barImg, shadowLine;
    private static Color barOrig, shadowOrig, dateOrig;
    private static TMPro.FontStyles dateStyle;
    private static float dateSpacing, dateOutline;
    private static bool barKnown, lastOn, first = true, dateLook;
    private static float scan;
    private static readonly List<SegClock> clocks = new List<SegClock>();
    private static SegClock seg;
    // The reference's bar buttons stand on a polished floor: each is mirrored in it below the floor line (the bright
    // ledge 889 px down on the 941 px reference), fading out as it goes down. A snapshot of the button's visible
    // pictures and text (never its scripts), clipped to the floor and flipped about that line.
    private const float FloorLine = 889f / 941f;
    private struct Mirror { public GameObject src, copy; }
    private static readonly List<Mirror> mirrors = new List<Mirror>();
    private static void MakeMirror(RectTransform src)
    {
        if (src == null || !src.gameObject.activeInHierarchy) return;
        var canvas = bar.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var cs = new Vector3[4];
        ((RectTransform) canvas.rootCanvas.transform).GetWorldCorners(cs);
        Vector3 s0 = cs[0], s1 = cs[2];
        var br = (RectTransform) bar;
        float floorY = br.InverseTransformPoint(new Vector3(s0.x, Mathf.Lerp(s1.y, s0.y, FloorLine), s0.z)).y;
        float bottomY = br.InverseTransformPoint(new Vector3(s0.x, s0.y, s0.z)).y - 4f;
        var root = new GameObject("Glass Mirror " + src.name, typeof(RectTransform), typeof(CanvasGroup), typeof(RectMask2D));
        var rr = (RectTransform) root.transform;
        rr.SetParent(bar, false);
        rr.SetSiblingIndex(0);
        rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 0.5f);
        rr.sizeDelta = new Vector2(br.rect.width, floorY - bottomY);
        rr.anchoredPosition = new Vector2(0f, (floorY + bottomY) * 0.5f) - new Vector2(0f, br.rect.center.y);
        var cg = root.GetComponent<CanvasGroup>(); cg.alpha = 0.42f; cg.blocksRaycasts = false; cg.interactable = false;
        float axis = floorY - (floorY + bottomY) * 0.5f; // the floor line in the clip's own units
        float fadeLen = (s1.y - s0.y) * 0.075f / Mathf.Max(0.0001f, rr.lossyScale.y);
        foreach (var g in src.GetComponentsInChildren<Graphic>(false))
        {
            if (!Shown(g)) continue;
            g.rectTransform.GetWorldCorners(cs);
            Vector2 a = rr.InverseTransformPoint(cs[0]), b = rr.InverseTransformPoint(cs[2]);
            float k = (b.y - a.y) / Mathf.Max(1f, g.rectTransform.rect.height); // on-screen scale of the source
            Graphic copy;
            if (g is Image im)
            {
                var c = new GameObject(g.name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                c.sprite = im.sprite; c.type = im.type; c.preserveAspect = im.preserveAspect; c.pixelsPerUnitMultiplier = im.pixelsPerUnitMultiplier / Mathf.Max(0.01f, k);
                c.fillCenter = im.fillCenter;
                copy = c;
            }
            else if (g is RawImage ri)
            {
                var c = new GameObject(g.name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                c.texture = ri.texture; c.uvRect = ri.uvRect;
                copy = c;
            }
            else if (g is TMPro.TMP_Text t)
            {
                var c = new GameObject(g.name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TextMeshProUGUI>();
                c.font = t.font; c.fontSharedMaterial = t.fontSharedMaterial; c.text = t.text; c.fontStyle = t.fontStyle; c.alignment = t.alignment;
                c.enableAutoSizing = false; c.fontSize = t.fontSize * k; c.enableWordWrapping = false; c.overflowMode = TMPro.TextOverflowModes.Overflow;
                copy = c;
            }
            else continue;
            var col = g.color; if (copy is TMPro.TMP_Text) col.a *= 0.5f; // text sits deeper in the button, its reflection is fainter
            copy.color = col; copy.raycastTarget = false;
            var rt = copy.rectTransform;
            rt.SetParent(rr, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = b - a;
            var center = (a + b) * 0.5f;
            rt.anchoredPosition = new Vector2(center.x, 2f * axis - center.y);
            rt.localScale = new Vector3(1f, -1f, 1f);
            if (!(copy is TMPro.TMP_Text))
            {
                var fade = copy.gameObject.AddComponent<FloorFade>();
                fade.axis = axis - rt.anchoredPosition.y; fade.length = fadeLen;
            }
        }
        mirrors.Add(new Mirror { src = src.gameObject, copy = root });
    }

    private static bool Shown(Graphic g) => g.enabled && g.color.a > 0.05f && g.canvasRenderer.GetAlpha() > 0.05f && g.rectTransform.rect.width > 2f;

    private static readonly List<Graphic> pockets = new List<Graphic>();
    private static readonly List<GameObject> halos = new List<GameObject>();

    private static Texture2D haloTex, mailTex;
    private static Texture2D HaloTex()
    {
        if (haloTex != null) return haloTex;
        // measured on the reference's Wii and mail buttons (in face radii): one crisp white ring on the face's edge, a
        // saturated cyan band just outside it, then a soft blue light fading out by ~1.3 (no dark bezel, no second ring)
        const int n = 256;
        haloTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, q = Mathf.Sqrt(dx * dx + dy * dy) * 1.55f;
                float top = 0.88f + 0.12f * Mathf.Clamp01(dy / Mathf.Max(0.001f, q)); // a touch brighter over the top
                float glow = Mathf.Exp(-Mathf.Pow((q - 1.14f) / 0.09f, 2f)) * 0.40f;
                float cyan = Mathf.Exp(-Mathf.Pow((q - 1.07f) / 0.055f, 2f)) * 0.92f; // stays bright out to ~1.13 like the reference's
                float core = Mathf.Exp(-Mathf.Pow((q - 0.995f) / 0.022f, 2f)) * top; // on the face's edge (the face covers the middle)
                // back to front: blue light, cyan band, white ring
                var c = new Color(0.10f, 0.45f, 0.85f, glow);
                c = Over(c, new Color(0.05f, 0.69f, 0.99f, Mathf.Clamp01(cyan)));
                c = Over(c, new Color(0.96f, 0.98f, 1f, Mathf.Clamp01(core)));
                c.a *= Mathf.Clamp01((1.5f - q) / 0.1f);
                px[y * n + x] = c;
            }
        haloTex.SetPixels(px);
        haloTex.Apply();
        return haloTex;
    }

    // the reference's button face (measured): deep navy glass, flat across the middle, with a band of light around
    // its edge (pale over the top, bluer low down) running into the white ring
    private static Sprite discSprite;
    private static Sprite DiscSprite()
    {
        if (discSprite != null) return discSprite;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        var navy = new Color(0.19f, 0.255f, 0.36f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float top = Mathf.Clamp01(0.5f + 0.5f * dy / Mathf.Max(0.001f, r));
                float band = Mathf.Clamp01((r - 0.80f) / 0.13f); band = band * band * (3f - 2f * band);
                var lit = Color.Lerp(new Color(0.42f, 0.51f, 0.70f), new Color(0.71f, 0.81f, 0.92f), top * top);
                var c = Color.Lerp(navy, lit, band * 0.9f);
                c.a = Mathf.Clamp01((0.985f - r) * n * 0.5f); // smooth round edge
                px[y * n + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        discSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return discSprite;
    }

    private static Color Over(Color under, Color over)
    {
        float a = over.a + under.a * (1f - over.a);
        if (a <= 0f) return new Color(0f, 0f, 0f, 0f);
        var rgb = ((Vector4) over * over.a + (Vector4) under * under.a * (1f - over.a)) / a;
        return new Color(rgb.x, rgb.y, rgb.z, a);
    }

    // envelope glyph for the mail button: the reference's is a filled pale-blue envelope with a white edge and a
    // darker flap
    private static Texture2D MailTex()
    {
        if (mailTex != null) return mailTex;
        const int w = 96, h = 68; const float t = 4f;
        mailTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float edge = Mathf.Min(Mathf.Min(px, w - px), Mathf.Min(py, h - py));
                float inside = Mathf.Clamp01(edge + 0.5f);
                float rim = Mathf.Clamp01(t + 0.5f - edge);
                // the flap: two lines from the top corners down to the middle
                float fy = h - py, k = (h * 0.55f) / (w * 0.5f);
                float d1 = Mathf.Abs(fy - px * k) / Mathf.Sqrt(1f + k * k), d2 = Mathf.Abs(fy - (w - px) * k) / Mathf.Sqrt(1f + k * k);
                float flap = fy < h * 0.58f ? Mathf.Clamp01(t * 0.5f - Mathf.Min(d1, d2) + 0.5f) : 0f;
                var body = Color.Lerp(new Color(0.70f, 0.80f, 0.95f), new Color(0.86f, 0.92f, 0.99f), py / h);
                var c = Color.Lerp(Color.Lerp(body, new Color(0.47f, 0.62f, 0.86f), flap), Color.white, rim);
                c.a = inside;
                mailTex.SetPixel(x, y, c);
            }
        mailTex.Apply();
        return mailTex;
    }

    // a light glyph drawn over the original (dark) one, which is hidden while the look is on
    private static void Glyph(Image face, bool mail)
    {
        GameObject g;
        if (mail)
        {
            g = new GameObject("Glass Glyph", typeof(RectTransform), typeof(RawImage));
            var ri = g.GetComponent<RawImage>(); ri.texture = MailTex(); ri.raycastTarget = false;
        }
        else
        {
            g = new GameObject("Glass Glyph", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            var t = g.GetComponent<TMPro.TextMeshProUGUI>();
            t.text = "Revo"; t.fontStyle = TMPro.FontStyles.Bold; t.alignment = TMPro.TextAlignmentOptions.Center;
            t.enableAutoSizing = true; t.fontSizeMin = 8f; t.fontSizeMax = 120f; t.color = Color.white; t.raycastTarget = false;
            t.enableWordWrapping = false;
        }
        var rt = (RectTransform) g.transform;
        rt.SetParent(face.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = face.rectTransform.rect.center;
        float d = Mathf.Min(face.rectTransform.rect.width, face.rectTransform.rect.height);
        rt.sizeDelta = mail ? new Vector2(d * 0.44f, d * 0.44f * 68f / 96f) : new Vector2(d * 0.66f, d * 0.34f);
        halos.Add(g);
    }

    // the reference's buttons are dark glass, not flat paint: a lit lower rim and a faint sheen across the top
    private static Texture2D glossTex;
    private static void FaceGloss(Image face)
    {
        if (glossTex == null)
        {
            const int n = 128;
            glossTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    // the face itself carries the lit edge: only a faint sheen near the top (the reference's face is flat)
                    float a = Mathf.Exp(-(dx * dx * 1.6f + (dy - 0.50f) * (dy - 0.50f) * 7f)) * 0.12f;
                    a *= Mathf.Clamp01((1f - r) * 40f); // crisp round edge
                    glossTex.SetPixel(x, y, new Color(0.70f, 0.88f, 1f, a));
                }
            glossTex.Apply();
        }
        var g = new GameObject("Glass Gloss", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform) g.transform;
        rt.SetParent(face.transform, false);
        rt.SetSiblingIndex(0);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = face.rectTransform.rect.center;
        float d = Mathf.Min(face.rectTransform.rect.width, face.rectTransform.rect.height) * 0.94f;
        rt.sizeDelta = new Vector2(d, d);
        var ri = g.GetComponent<RawImage>(); ri.texture = glossTex; ri.raycastTarget = false;
        halos.Add(g);
    }

    // a soft cyan light behind a round bar button
    private static void Halo(Image face)
    {
        if (face == null) return;
        var h = new GameObject("Glass Halo", typeof(RectTransform), typeof(RawImage));
        var hr = (RectTransform) h.transform;
        hr.SetParent(face.transform.parent, false);
        hr.SetSiblingIndex(face.transform.GetSiblingIndex());
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f);
        hr.position = face.rectTransform.TransformPoint(face.rectTransform.rect.center);
        hr.sizeDelta = face.rectTransform.rect.size * 1.55f;
        var ri = h.GetComponent<RawImage>(); ri.texture = HaloTex(); ri.raycastTarget = false;
        halos.Add(h);
    }

    // the Revo and mail buttons are one baked image each (dark glyph included), so in Glass 2026 the face is
    // swapped for a dark glass button with a cyan ring and a white glyph on top; the clip pocket around it is hidden
    private struct Face { public Image img; public Sprite orig; public Color origCol; }
    private static readonly List<Face> faces = new List<Face>();
    // colours held while the look is on (the theme re-applies its own)
    private struct Tint { public Graphic g; public Color orig, on; }
    private static readonly List<Tint> tints = new List<Tint>();

    private static void CollectButtons()
    {
        foreach (var n in new[] { "WiiClip", "MailClip" })
        {
            var root = bar.Find(n); if (root == null) continue;
            // the grey pocket (clip + its outline) and the light drop-shadow disc: the 2026 buttons stand free
            var clip = root.GetComponent<Image>(); if (clip != null) pockets.Add(clip);
            foreach (var p in new[] { "Accent", "ButtonHolder/Shadow" }) { var t = root.Find(p); if (t != null && t.GetComponent<Graphic>() != null) pockets.Add(t.GetComponent<Graphic>()); }
            bool mail = n == "MailClip";
            var faceT = root.Find(mail ? "ButtonHolder/MailButton" : "ButtonHolder/WiiButton");
            var face = faceT != null ? faceT.GetComponent<Image>() : null;
            if (face == null) continue;
            // the light face goes dark glass (its cyan Accent ring stays), the dark logo is swapped for a white glyph
            faces.Add(new Face { img = face, orig = face.sprite, origCol = face.color });
            var icon = faceT.Find(mail ? "MailIcon" : "Icon");
            if (icon != null && icon.GetComponent<Graphic>() != null) pockets.Add(icon.GetComponent<Graphic>());
            Halo(face);
            FaceGloss(face);
            Glyph(face, mail);
        }
        // the reference's SD card sits in a small glowing cyan glass frame, a little away from it: a pale card face with a
        // blue SD logo and a blue edge (the cover is the card's edge, strip and logo; the tag is the face behind its window)
        var sd = bar.Find("SD") as RectTransform;
        if (sd != null)
        {
            foreach (var (part, col) in new[] { ("SD_Tag", new Color(0.90f, 0.95f, 1f, 1f)), ("SD_Cover", new Color(0.15f, 0.55f, 0.98f, 1f)) })
            {
                var g = sd.Find(part) != null ? sd.Find(part).GetComponent<Graphic>() : null;
                if (g != null) tints.Add(new Tint { g = g, orig = g.color, on = col });
            }
            var f = new GameObject("Glass SD Frame", typeof(RectTransform), typeof(Image));
            var fr = (RectTransform) f.transform;
            fr.SetParent(sd, false);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.anchoredPosition = sd.rect.center;
            const float pad = 8f;
            var inner = new Vector2(sd.rect.height * 1.05f, sd.rect.height * 1.22f); // measured: 68 x 85 px around a 44 x 69 px card
            fr.sizeDelta = inner + Vector2.one * pad * 2f;
            var im = f.GetComponent<Image>();
            im.sprite = TVFx.Frame(TVFx.ScreenAspect(fr, inner), pad / inner.x, pad / inner.y, true);
            im.color = new Color(0.30f, 0.82f, 1f, 1f); im.raycastTarget = false;
            halos.Add(f);
        }
    }

    // reference layout for the bar's controls (shares of the bar width; the reference: Wii button ~180 px across
    // centred 170 px in, SD card ~80x110 at 365 px, mail ~165 px centred 180 px from the right)
    private struct Placed { public RectTransform rt; public Vector2 pos; public Vector3 scale; public float dx, dy, k; }
    private static readonly List<Placed> placed = new List<Placed>();
    private static bool placedOn;
    private static void PlaceControls(bool on)
    {
        if (bar == null) return;
        if (placed.Count == 0)
        {
            // the date sits 5 px lower on the reference (as a share of the bar width, like the others)
            // sizes from the measured ring radius (reference 70 px of 941) and the SD card's ~69 px height
            foreach (var (n, dx, dy, k) in new[] { ("WiiClip", 0.0077f, -0.0084f, 0.99f), ("SD", 0.025f, 0.002f, 1.08f), ("MailClip", -0.0093f, -0.0072f, 0.99f), ("Date", 0f, -0.0032f, 1f) })
            {
                var t = bar.Find(n) as RectTransform;
                if (t != null) placed.Add(new Placed { rt = t, pos = t.anchoredPosition, scale = t.localScale, dx = dx, dy = dy, k = k });
            }
            placedOn = !on;
        }
        // while on, the placement is held: coming back from Settings replays the bar's own intro, which puts the
        // classic sizes back (only re-applied once that tween is over)
        if (placedOn == on && !on) return;
        bool changed = placedOn != on;
        placedOn = on;
        float w = ((RectTransform) bar).rect.width;
        foreach (var p in placed)
        {
            if (p.rt == null) continue;
            var pos = on ? p.pos + new Vector2(w * p.dx, w * p.dy) : p.pos;
            var sc = on ? p.scale * p.k : p.scale;
            if (!changed && (DOTween.IsTweening(p.rt) || ((p.rt.anchoredPosition - pos).sqrMagnitude < 0.25f && (p.rt.localScale - sc).sqrMagnitude < 1e-6f))) continue;
            p.rt.anchoredPosition = pos;
            p.rt.localScale = sc;
        }
    }

    // The classic bar was authored for a phone held upright: on a wall-sized picture it towers. Off the 2026 look it is
    // scaled down about its bottom edge (buttons, clock and date with it) and widened by the same factor, so it still
    // spans the screen. The 2026 bar is laid out on the reference and keeps its size.
    public const float ClassicBarScale = 0.80f;
    // ... but the bar itself is squeezed lower than that (the curve and the band under it), so no empty band is left under
    // the curve: the buttons, clock and date are scaled back up on that axis and keep the uniform ClassicBarScale look
    public const float ClassicBarYScale = 0.72f;

    // layout log (one time, ~20 s after start): the bar's parts and the grid, as screen pixel rects
    private static bool dumped;
    private static void DumpLayout()
    {
        try
        {
            var sb = new System.Text.StringBuilder("[Layout] screen " + Screen.width + "x" + Screen.height + "\n");
            void Walk(Transform t, int depth)
            {
                if (depth > 4) return;
                var rt = t as RectTransform;
                if (rt != null)
                {
                    var c = new Vector3[4]; rt.GetWorldCorners(c);
                    var canvas = rt.GetComponentInParent<Canvas>();
                    Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                    var img = t.GetComponent<Image>();
                    var txt = t.GetComponent<TMPro.TMP_Text>();
                    sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeInHierarchy ? "" : " (off)")
                      .Append(" x=").Append((int) a.x).Append("..").Append((int) b.x)
                      .Append(" y(top)=").Append((int) (Screen.height - b.y)).Append("..").Append((int) (Screen.height - a.y))
                      .Append(" scale=").Append(rt.localScale.x.ToString("0.00"))
                      .Append(img != null && img.sprite != null ? " sprite=" + img.sprite.name + " type=" + img.type : "")
                      .Append(txt != null ? " text='" + txt.text + "' size=" + txt.fontSize.ToString("0") : "")
                      .Append("\n");
                }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
            }
            Walk(bar, 0);
            { var mm = bar.parent != null && bar.parent.parent != null ? bar.parent.parent : null; if (mm != null) foreach (Transform ch in mm) if (ch != bar.parent) Walk(ch, 0); }
            var gc = GridController._instance;
            if (gc != null && gc.currentGrid != null) Walk(gc.currentGrid.transform, 0);
            foreach (var line in sb.ToString().Split((char) 10)) if (line.Length > 0) Debug.Log("[Layout] " + line);
        }
        catch (System.Exception e) { Debug.Log("[Layout] failed " + e.Message); }
    }
    private static float barWidthOrig = float.NaN, barYOrig = float.NaN;
    private static void ClassicBarSize(bool on)
    {
        if (!(bar is RectTransform br) || !(br.parent is RectTransform parent)) return;
        if (float.IsNaN(barWidthOrig)) barWidthOrig = br.sizeDelta.x;
        float k = on ? 1f : ClassicBarScale;
        var scale = new Vector3(k, on ? 1f : ClassicBarYScale, 1f);
        float width = on ? barWidthOrig : barWidthOrig + parent.rect.width * (1f / k - 1f);
        if (float.IsNaN(barYOrig)) barYOrig = br.anchoredPosition.y;
        float y = on ? barYOrig : barYOrig - parent.rect.height * -ClassicLift;
        if (Mathf.Abs(br.anchoredPosition.y - y) > 0.5f) br.anchoredPosition = new Vector2(br.anchoredPosition.x, y);
        if (br.localScale != scale) br.localScale = scale;
        if (Mathf.Abs(br.sizeDelta.x - width) > 0.5f) br.sizeDelta = new Vector2(width, br.sizeDelta.y);
        ClassicBarParts(br, on);
    }

    // Classic look only: the bar's parts sit as the Wii has them, measured against the reference (fractions of the screen):
    // the round buttons a little bigger and 3% higher, the SD card bigger and further right, the date higher and bigger.
    public const float ClassicLift = -0.012f; // classic bar only: the whole bar sits this fraction of the screen height lower (negative = down), nearer the bottom edge

    private struct Home { public Vector2 pos; public Vector3 scale; }
    // Where a part sits in the normal look. If the app starts with a custom theme already on, the part is found where that theme put it,
    // so its home is worked out from the built-in theme instead (the classic bar is laid out for the normal look whatever the theme).
    private static Home MakeHome(RectTransform rt)
    {
        var h = new Home { pos = rt.anchoredPosition, scale = rt.localScale };
        var tc = ThemeController._instance;
        var tt = rt.GetComponent<ThemedTransform>();
        if (tc != null && tt != null && !tc.IsDefaultLook())
        {
            var d = tc.GetDefaultThemeTransform(tt.category);
            if (d == null || d.position == ThemePositions.Neutral)
            {
                h.pos = (Vector2) tt.originalPositionLocal + (d == null ? Vector2.zero : (Vector2) d.offset);
                if (d != null) h.scale = d.scale;
            }
        }
        return h;
    }
    private static readonly Dictionary<Transform, Home> partHome = new Dictionary<Transform, Home>();
    private static void ClassicBarParts(RectTransform br, bool on)
    {
        ClassicCurve(on);
        // the date sits a little lower in the dip too (the round buttons stay where they are)
        Nudge(br.Find("Date"), 0f, on ? 0f : -0.026f, 1f);
        foreach (var name in new[] { "Date", "SD", "WiiClip", "MailClip" })
        {
            var t = br.Find(name);
            if (t == null) continue;
            if (!partHome.TryGetValue(t, out var home)) partHome[t] = home = MakeHome((RectTransform) t);
            var want = on ? home.scale : new Vector3(home.scale.x, home.scale.y * ClassicBarScale / ClassicBarYScale, home.scale.z);
            if ((t.localScale - want).sqrMagnitude > 1e-6f) t.localScale = want;
        }
    }

    // The curve with the clock above the bar belongs to the grid's own clip (BottomClip) with its two lines: drawn at the bar's
    // squeezed height too, so the white band under the curve doesn't look empty. Its bottom edge stays where it is.
    private static Transform Deep(Transform root, string name)
    {
        var q = new Queue<Transform>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (t.name == name) return t;
            foreach (Transform c in t) q.Enqueue(c);
        }
        return null;
    }

    // classic look: the curve with its two lines and the clock sit this fraction of the screen height lower (down is minus)
    public const float ClassicCurveDrop = -0.0415f; // the flat lines at the curve's sides and its top edge
    public const float ClassicCurveDipExtra = 0.03f; // the dip itself reaches this much lower still (fraction of the screen height)
    private static readonly Dictionary<Transform, Vector2> dateBase = new Dictionary<Transform, Vector2>();
    private static readonly Dictionary<Transform, Vector2> clockPin = new Dictionary<Transform, Vector2>();
    private static readonly Dictionary<Transform, float> curveHomeOff = new Dictionary<Transform, float>();
    private static readonly Dictionary<Transform, float> curveDxApplied = new Dictionary<Transform, float>();
    private static float ScreenRight(RectTransform space, float px)
    {
        if (space == null) return 0f;
        var canvas = space.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, Vector2.zero, cam, out Vector3 w0);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, new Vector2(px, 0f), cam, out Vector3 w1);
        return space.InverseTransformVector(w1 - w0).x;
    }
    private static float ScreenDrop(RectTransform space, float frac)
    {
        if (space == null) return 0f;
        var canvas = space.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, Vector2.zero, cam, out Vector3 w0);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, new Vector2(0f, Screen.height * frac), cam, out Vector3 w1);
        return space.InverseTransformVector(w1 - w0).y;
    }

    // the page change re-applies the theme's clock transform (Grid.FixPositions): put ours straight back so the time never pops big
    private static readonly Dictionary<Transform, (Vector2 pos, Vector3 scale)> clockWant = new Dictionary<Transform, (Vector2 pos, Vector3 scale)>();
    public static void ReapplyClock(Transform clock)
    {
        if (clock == null || !clockWant.TryGetValue(clock, out var w)) return;
        clock.localScale = w.scale; ((RectTransform) clock).anchoredPosition = w.pos;
    }

    private static void ClassicCurve(bool on)
    {
        var holder = bar != null && bar.parent != null ? bar.parent.Find("Grid ScrollView") : null;
        if (holder == null) return;
        // not while a channel is open or the camera zooms in / out of one: the curve's spot is worked out in screen pixels, and
        // with the camera zoomed those turned into other sizes, so the curve and the time were re-placed a few px off and put
        // back after the zoom (the time hopping as a preview closed, seen 2026-11-12)
        var ccz = ChannelController._instance;
        if (AppController.IsTransitioning() || (ccz != null && ccz.currentChannel != null)) return;
        // every page of the grid carries its own copy of the curve. Finding them walked every object on every page by name each
        // frame (14% of the main thread and most of its garbage, measured 2026-11-07): the list is kept and looked up again once
        // a second or when the number of pages changes. The per-frame placement below is unchanged.
        int pages = GridController._instance != null ? GridController._instance.GetNumberOfGrids() : 0;
        if (clipCache.Count == 0 || pages != clipPages || Time.unscaledTime >= clipScanAt || clipCache.Exists(c => c == null))
        {
            if (pages != clipPages) syncedFor = null;
            clipCache.Clear(); clipPages = pages; clipScanAt = Time.unscaledTime + 1f;
            foreach (var rtf in holder.GetComponentsInChildren<RectTransform>(true))
                if (rtf.name == "BottomClip") clipCache.Add(rtf);
        }
        for (int i = 0; i < clipCache.Count; i++) ClassicCurveOne(holder, clipCache[i], on);
    }
    private static readonly List<RectTransform> clipCache = new List<RectTransform>();
    private static int clipPages = -1; private static float clipScanAt;
    // a clip's clock and date, found once (Deep() walks the clip comparing names)
    private static readonly Dictionary<Transform, (Transform clock, Transform date)> clipParts = new Dictionary<Transform, (Transform, Transform)>();
    private static (Transform clock, Transform date) ClipParts(Transform clip)
    {
        if (!clipParts.TryGetValue(clip, out var p) || p.clock == null) clipParts[clip] = p = (Deep(clip, "ClockHolder"), Deep(clip, "Date"));
        return p;
    }

    private static void ClassicCurveOne(Transform holder, RectTransform clip, bool on)
    {
        var page = clip.parent;
        if (!partHome.TryGetValue(clip, out var home)) partHome[clip] = home = MakeHome(clip);
        float ky = on ? 1f : ClassicBarYScale;
        float stretch = 1f;
        if (!on && clip.parent is RectTransform sp0)
        {
            float extra = Mathf.Abs(ScreenDrop(sp0, ClassicCurveDipExtra));
            float hLocal = clip.rect.height * home.scale.y * ky;
            if (hLocal > 1f) stretch = 1f + extra / hLocal;
        }
        var want = new Vector3(home.scale.x, home.scale.y * ky * stretch, home.scale.z);
        // classic: the curve (and the time in it) is moved so its middle is the screen's middle; the page itself sits left of centre
        bool shift = !on && ThemeController._instance != null && ThemeController._instance.IsDefaultLookReady(); // a custom theme places its own curve
        float dxPxWant = !shift ? 0f : (0.5f - (0.018f + GridController._instance.GetAppsPerX() * 0.1095f)) * Screen.width;
        float dxLocal = 0f;
        if (shift && clip.parent is RectTransform dsp0) { curveHomeOff.TryGetValue(clip, out float ho0); dxLocal = ScreenRight(dsp0, dxPxWant) - ho0; }
        curveDxApplied.TryGetValue(clip, out float dxHad);
        var clock = ClipParts(clip).clock;
        Vector2 clockBase = Vector2.zero;
        if (clock != null)
        {
            if (!partHome.TryGetValue(clock, out var ch)) partHome[clock] = ch = MakeHome((RectTransform) clock);
            var cs = on ? ch.scale : new Vector3(ch.scale.x * ClassicBarScale, ch.scale.y * ClassicBarScale / (ky * stretch), ch.scale.z);
            if ((clock.localScale - cs).sqrMagnitude > 1e-6f) clock.localScale = cs;
            // the time stands in the middle of the curve, not on its upper edge
            var crt = (RectTransform) clock;
            var cp = on ? ch.pos : ch.pos + new Vector2(0f, ScreenDrop(crt.parent as RectTransform, -0.011f));
            clockBase = cp;
            if (shift && clockPin.TryGetValue(clock, out Vector2 cpin)) cp += cpin; // the curve moved, the time stays where it was
            clockWant[clock] = (cp, cs);
            if ((crt.anchoredPosition - cp).sqrMagnitude > 0.01f) crt.anchoredPosition = cp;
        }
        if ((clip.localScale - want).sqrMagnitude < 1e-6f && (on || (clip.anchoredPosition - home.pos).sqrMagnitude > 0.01f) && Mathf.Abs(dxHad - dxLocal) < 0.5f) return;
        // always worked out from the untouched state, so a theme reload that resets the scale can't push it down twice
        clip.localScale = home.scale; clip.anchoredPosition = home.pos;
        var c0 = new Vector3[4]; clip.GetWorldCorners(c0);
        clip.localScale = want;
        var c1 = new Vector3[4]; clip.GetWorldCorners(c1);
        var space = clip.parent as RectTransform;
        // the time and the date are held where they are while the curve around them moves
        Transform dateT = ClipParts(clip).date;
        if (shift)
        {
            if (dateT != null && !dateBase.ContainsKey(dateT)) dateBase[dateT] = ((RectTransform) dateT).anchoredPosition;
            if (clock != null) ((RectTransform) clock).anchoredPosition = clockBase;
            if (dateT != null) ((RectTransform) dateT).anchoredPosition = dateBase[dateT];
        }
        else
        {
            // off the normal look (a custom theme, or Glass 2026): the date and time go back to where the theme put them
            if (dateT != null && dateBase.TryGetValue(dateT, out Vector2 db)) ((RectTransform) dateT).anchoredPosition = db;
            if (clock != null) clockPin.Remove(clock);
        }
        float clockX = clock != null ? clock.position.x : 0f, dateX = dateT != null ? dateT.position.x : 0f;
        if (shift && space != null)
        {
            Vector3 cw = (c0[0] + c0[2]) * 0.5f, pw = space.TransformPoint(space.rect.center);
            float ho = space.InverseTransformVector(cw - pw).x;
            curveHomeOff[clip] = ho;
            dxLocal = ScreenRight(space, dxPxWant) - ho;
        }
        curveDxApplied[clip] = dxLocal;
        if (space != null)
        {
            Vector3 d = space.InverseTransformVector(c0[0] - c1[0]);
            clip.anchoredPosition += new Vector2(dxLocal, d.y + (on ? 0f : ScreenDrop(space, ClassicCurveDrop - ClassicCurveDipExtra + ClassicLift)));
        }
        MoveBowl(page, c0, clip, on, dxLocal);
        if (shift)
        {
            if (clock != null)
            {
                var crt2 = (RectTransform) clock;
                // the whole time (digits and AM/PM) is centred over the date: the normal theme has it a little left
                var wp = clock.position; clock.position = new Vector3(clockX, wp.y, wp.z);
                clockPin[clock] = crt2.anchoredPosition - clockBase;
                if (clockWant.TryGetValue(clock, out var cw0)) clockWant[clock] = (crt2.anchoredPosition, cw0.scale);
            }
            if (dateT != null) { var wd = dateT.position; dateT.position = new Vector3(dateX, wd.y, wd.z); }
        }
        // the two lines at the curve's top follow its new top edge
        float drop = (1f - ky) * Mathf.Abs(c0[1].y - c0[0].y);
        foreach (var tt in page.GetComponentsInChildren<RectTransform>(true))
        {
            if ((tt.name != "TopLine" && tt.name != "Shadow") || tt.IsChildOf(clip)) continue;
            if (!partHome.TryGetValue(tt, out var th)) partHome[tt] = th = MakeHome(tt);
            var sp = tt.parent as RectTransform;
            Vector3 dd = sp != null ? sp.InverseTransformVector(new Vector3(0f, on ? 0f : -drop, 0f)) : Vector3.zero;
            tt.anchoredPosition = th.pos + new Vector2(0f, dd.y + (on ? 0f : ScreenDrop(sp, ClassicCurveDrop + ClassicLift)));
        }
    }

    // The white bowl (BackMask: the page picture seen through the curve's shape) is stretched and lowered with the curve,
    // the same way, so no grey shows between the bowl and the blue line. Its picture child keeps its place on screen.
    private static void MoveBowl(Transform page, Vector3[] clipHome, RectTransform clip, bool on, float dxLocal)
    {
        var bm = page.Find("BackMask") as RectTransform;
        if (bm == null || bm.childCount == 0 || !(bm.GetChild(0) is RectTransform pic)) return;
        if (!partHome.TryGetValue(bm, out var home)) partHome[bm] = home = MakeHome(bm);
        if (!partHome.TryGetValue(pic, out var ph)) partHome[pic] = ph = MakeHome(pic);
        bm.localScale = home.scale; bm.anchoredPosition = home.pos; pic.localScale = ph.scale; pic.anchoredPosition = ph.pos;
        if (on) return;
        var cf = new Vector3[4]; clip.GetWorldCorners(cf);
        float oldH = clipHome[1].y - clipHome[0].y, newH = cf[1].y - cf[0].y;
        if (Mathf.Abs(oldH) < 1e-4f) return;
        float k = newH / oldH;
        var b0 = new Vector3[4]; bm.GetWorldCorners(b0);
        var p0 = new Vector3[4]; pic.GetWorldCorners(p0);
        bm.localScale = new Vector3(home.scale.x, home.scale.y * k, home.scale.z);
        var b1 = new Vector3[4]; bm.GetWorldCorners(b1);
        float want = cf[0].y + (b0[0].y - clipHome[0].y) * k;
        if (bm.parent is RectTransform bp) bm.anchoredPosition += new Vector2(dxLocal, bp.InverseTransformVector(new Vector3(0f, want - b1[0].y, 0f)).y);
        pic.localScale = new Vector3(ph.scale.x, ph.scale.y / k, ph.scale.z);
        var p1 = new Vector3[4]; pic.GetWorldCorners(p1);
        var pd = bm.InverseTransformVector(new Vector3(p0[0].x - p1[0].x, p0[0].y - p1[0].y, 0f));
        pic.anchoredPosition += new Vector2(pd.x, pd.y);
    }

    private static void Nudge(Transform t, float fx, float fy, float k)
    {
        if (!(t is RectTransform rt) || !(rt.parent is RectTransform space)) return;
        if (!partHome.TryGetValue(t, out var home)) partHome[t] = home = MakeHome(rt);
        var canvas = space.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector3 w0, w1;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, new Vector2(0f, 0f), cam, out w0);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(space, new Vector2(Screen.width * fx, Screen.height * fy), cam, out w1);
        Vector3 d = space.InverseTransformVector(w1 - w0);
        var want = home.pos + new Vector2(d.x, d.y);
        if ((rt.anchoredPosition - want).sqrMagnitude > 0.01f) rt.anchoredPosition = want;
        var s = home.scale * k;
        if ((rt.localScale - s).sqrMagnitude > 0.00001f) rt.localScale = s;
    }

    // Normal look: the date and the time (digits and AM/PM together) are centred on the screen, whatever the theme reload or a page change did to them.
    private static float centerTimer;
    private static readonly List<UnityEngine.UI.Graphic> clockGraphics = new List<UnityEngine.UI.Graphic>();
    private static readonly Vector3[] cornersTmp = new Vector3[4];
    private static Vector3 lastGridPos, lastGridScale; private static int stillFrames;
    private static bool centerWasHome; private static Grid centerGrid;
    // a page change or coming back to the menu: centred at once (it was checked only every 0.3 s, so a page's own copy of the
    // time showed off-centre for a moment and then hopped sideways: the "clock moves after switching pages / leaving a preview")
    public static void CenterTimeNow() { centerTimer = 0f; CenterTime(); }
    private static void CenterTime()
    {
        var gc0 = GridController._instance; var app0 = AppController._instance;
        bool home0 = app0 != null && app0.mainView != null && app0.mainView.activeInHierarchy;
        if ((home0 && !centerWasHome) || (gc0 != null && gc0.currentGrid != centerGrid)) centerTimer = 0f;
        centerWasHome = home0; if (gc0 != null && !gc0.isChangingGrid) centerGrid = gc0.currentGrid;
        // every frame on the menu: other code puts a page's time back to its old spot now and then (seen: 26 px, several times
        // after leaving a channel preview), and a check every 0.3 s let that show before it was centred again
        var tc = ThemeController._instance;
        var gc = GridController._instance;
        var app = AppController._instance;
        if (tc == null || !tc.IsDefaultLookReady() || gc == null || gc.currentGrid == null || gc.isChangingGrid || app == null || app.gameCanvas == null) return;
        if (DOTween.IsTweening(gc.currentGrid.transform.parent) || !app.mainView.activeInHierarchy) return;
        // not while a channel is open or the menu zooms in / out of one: it chased the moving clock (up to 463 px) and then had
        // to put it back when the zoom stopped = the time hopping sideways after leaving any channel preview
        var cc = ChannelController._instance;
        if (AppController.IsTransitioning() || (cc != null && cc.currentChannel != null)) { stillFrames = 0; return; }
        var gt = gc.currentGrid.transform; Vector3 gp = gt.position, gs = gt.lossyScale;
        if ((gp - lastGridPos).sqrMagnitude > 1e-6f || (gs - lastGridScale).sqrMagnitude > 1e-8f) { lastGridPos = gp; lastGridScale = gs; stillFrames = 0; return; }
        if (++stillFrames < 2) return;
        var canvas = app.gameCanvas.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(app.gameCanvas, new Vector2(Screen.width * 0.5f + Screen.width * TVTune.Get("timex", 0f), Screen.height * 0.5f), cam, out Vector3 mid);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(app.gameCanvas, new Vector2(Screen.width * (0.5f + TVTune.Get("clockx", -0.012f)), Screen.height * 0.5f), cam, out Vector3 midClock);
        var corners4 = cornersTmp;
        // the date
        var dtext = TextController._instance != null && TextController._instance.date != null ? TextController._instance.date.dateText : null;
        if (dtext != null && dtext.gameObject.activeInHierarchy && dtext.rectTransform is RectTransform drt)
        {
            Vector3 dc = drt.TransformPoint(drt.rect.center);
            if (Mathf.Abs(RectTransformUtility.WorldToScreenPoint(cam, dc).x - RectTransformUtility.WorldToScreenPoint(cam, new Vector3(mid.x, dc.y, dc.z)).x) > 2f)
                drt.position += new Vector3(mid.x - dc.x, 0f, 0f);
        }
        // the time: all of the clock's texts together
        var holder = gc.currentGrid.clockTransform != null ? gc.currentGrid.clockTransform.transform : null;
        if (holder != null && holder.gameObject.activeInHierarchy)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            // the clock is drawn from digit pictures: its width is the span of the visible ones
            holder.GetComponentsInChildren(false, clockGraphics);
            foreach (var g in clockGraphics)
            {
                if (!g.gameObject.activeInHierarchy || !g.enabled || g.color.a < 0.1f || g == (UnityEngine.UI.Graphic) dtext) continue;
                g.rectTransform.GetWorldCorners(corners4);
                for (int i = 0; i < 4; i++) { lo = Mathf.Min(lo, corners4[i].x); hi = Mathf.Max(hi, corners4[i].x); }
            }
            if (hi > lo)
            {
                float cx = (lo + hi) * 0.5f;
                var a = RectTransformUtility.WorldToScreenPoint(cam, new Vector3(cx, holder.position.y, holder.position.z)).x;
                var b = RectTransformUtility.WorldToScreenPoint(cam, new Vector3(midClock.x, holder.position.y, holder.position.z)).x;
                if (Mathf.Abs(a - b) > 2f)
                {
                    Debug.Log("[CLOCK] centred by " + Mathf.RoundToInt(b - a) + " px");
                    var hrt = (RectTransform) holder; Vector2 a0 = hrt.anchoredPosition;
                    holder.position += new Vector3(midClock.x - cx, 0f, 0f);
                    // the layout pass puts the clock back to its remembered spot every tick: remember the new one
                    Vector2 dA = hrt.anchoredPosition - a0;
                    clockPin[holder] = (clockPin.TryGetValue(holder, out var pin0) ? pin0 : Vector2.zero) + dA;
                    if (clockWant.TryGetValue(holder, out var cw1)) clockWant[holder] = (cw1.pos + dA, cw1.scale);
                    syncedFor = null;
                }
                if (syncedFor != holder) { syncedFor = holder; SyncPageClocks(holder); }
            }
        }
    }

    // every page carries its own copy of the time: only the shown page's was centred, so the next page's slid in off-centre
    // and hopped ~26 px a moment after the page change. All copies get the same spot on their page as the centred one.
    private static Transform syncedFor;
    private static void SyncPageClocks(Transform holder)
    {
        Transform refPage = null;
        for (int i = 0; i < clipCache.Count; i++) if (clipCache[i] != null && ClipParts(clipCache[i]).clock == holder) { refPage = clipCache[i].parent; break; }
        if (refPage == null) return;
        float rel = holder.position.x - refPage.position.x;
        for (int i = 0; i < clipCache.Count; i++)
        {
            var clip = clipCache[i]; if (clip == null) continue;
            var ck = ClipParts(clip).clock; if (ck == null || ck == holder) continue;
            float want = clip.parent.position.x + rel;
            if (Mathf.Abs(ck.position.x - want) < 0.01f) continue;
            var rt = (RectTransform) ck; Vector2 a0 = rt.anchoredPosition;
            ck.position = new Vector3(want, ck.position.y, ck.position.z);
            Vector2 dA = rt.anchoredPosition - a0;
            clockPin[ck] = (clockPin.TryGetValue(ck, out var p0) ? p0 : Vector2.zero) + dA;
            if (clockWant.TryGetValue(ck, out var w0)) clockWant[ck] = (w0.pos + dA, w0.scale);
        }
    }

    private static float clockLogX = float.NaN;
    // the time never moves when a channel opens or closes: some placing code puts it ~3 units off while a channel is up and
    // back afterwards (seen as a hop when leaving any preview). Its spot on the menu is held from just before every frame is
    // drawn, from the channel opening until a moment after it closed.
    private static bool holdHooked; private static float holdX = float.NaN, holdUntil;
    private static void HoldClockSpot()
    {
        var gcl = GridController._instance; var app = AppController._instance;
        var hc = gcl != null && gcl.currentGrid != null && gcl.currentGrid.clockTransform != null ? gcl.currentGrid.clockTransform.transform : null;
        if (hc == null || app == null || TVGlass.On) { holdX = float.NaN; return; }
        var cc = ChannelController._instance;
        bool inChannel = AppController.IsTransitioning() || (cc != null && cc.currentChannel != null);
        if (inChannel) holdUntil = Time.unscaledTime + 1f;
        bool hold = inChannel || Time.unscaledTime < holdUntil;
        if (!hold) { if (!gcl.isChangingGrid && app.mainView.activeInHierarchy) holdX = hc.position.x; return; }
        if (float.IsNaN(holdX)) return;
        var p = hc.position; if (Mathf.Abs(p.x - holdX) > 0.01f) hc.position = new Vector3(holdX, p.y, p.z);
    }
    public static void Tick(bool on)
    {
        if (!holdHooked) { holdHooked = true; Canvas.willRenderCanvases += HoldClockSpot; }
        // check: every time the menu's clock moves sideways on screen, how far (to find what moves it)
        var gcl = GridController._instance;
        var hc = gcl != null && gcl.currentGrid != null && gcl.currentGrid.clockTransform != null ? gcl.currentGrid.clockTransform.transform : null;
        if (hc != null && hc.gameObject.activeInHierarchy)
        {
            float sx = hc.position.x;
            if (!float.IsNaN(clockLogX) && Mathf.Abs(sx - clockLogX) > 0.5f) Debug.Log("[CLOCKPOS] " + clockLogX.ToString("0.0") + " -> " + sx.ToString("0.0") + " f" + Time.frameCount);
            clockLogX = sx;
        }
        scan -= Time.unscaledDeltaTime;
        if (scan <= 0f) { scan = 1f; Find(); }
        if (backdrop != null && backdrop.gameObject.activeSelf != on) backdrop.gameObject.SetActive(on);
        if (barTone != null && barTone.gameObject.activeSelf != on) barTone.gameObject.SetActive(on);
        if (barImg != null && (on != lastOn || first))
        {
            // the room artwork already holds the reference's glass bar and cyan curve. Its light is brought to the
            // reference's by the measured tone layer, so the bar's own sprite isn't drawn (it stays clickable); without
            // that layer, a plain smoke makes the glass greyer and darker
            barImg.color = on ? (barTone != null ? new Color(1f, 1f, 1f, 0f) : new Color(0.17f, 0.16f, 0.17f, 0.45f)) : barOrig;
            if (shadowLine != null) shadowLine.color = on ? new Color(1f, 1f, 1f, 0f) : shadowOrig;
        }
        if (date != null)
        {
            var t = date.GetComponent<TMPro.TMP_Text>();
            if (t != null)
            {
                var want = on ? new Color(0.88f, 0.92f, 0.98f, 1f) : dateOrig;
                if (t.color != want) t.color = want; // the theme can re-apply its own colour, so hold ours
                // the reference's date is a regular weight, a little spaced: here a medium weight (regular, thickened)
                if (dateLook != on)
                {
                    dateLook = on;
                    t.fontStyle = on ? TMPro.FontStyles.Normal : dateStyle;
                    t.characterSpacing = on ? dateSpacing + 3f : dateSpacing;
                    t.outlineColor = want;
                    t.outlineWidth = on ? 0.14f : dateOutline;
                }
            }
        }
        foreach (var c in clocks) c.Hide(on);
        if (!on) CenterTime();
        if (on && seg == null && bar != null)
        {
            var gc = GridController._instance;
            var src = gc != null && gc.currentGrid != null ? gc.currentGrid.GetComponentInChildren<Clock>(true) : null;
            if (src != null && !DOTween.IsTweening(gc.currentGrid.transform.parent) && src.isActiveAndEnabled)
            {
                seg = new SegClock(src, bar);
            }
        }
        if (seg != null) seg.Tick(on);
        // the grey pocket around each round button: the 2026 bar has free-standing buttons
        foreach (var g in pockets) if (g != null) g.canvasRenderer.SetAlpha(on ? 0f : 1f);
        foreach (var h in halos) if (h != null && h.activeSelf != on) h.SetActive(on);
        if (faces.Count == 0 && bar != null) CollectButtons();
        PlaceControls(on);
        ClassicBarSize(on);
        // held every frame while on: a theme reload resets them (and set before the floor reflections copy them);
        // put back once when the look goes off, so the classic theme's own pictures and colours stay in charge
        bool off = !on && (lastOn || first);
        foreach (var f in faces)
        {
            if (f.img == null) continue;
            var col = on ? new Color(1f, 1f, 1f, f.origCol.a) : f.origCol;
            if (f.img.color != col) f.img.color = col;
            if (on && f.img.sprite != DiscSprite()) f.img.sprite = DiscSprite();
            else if (off) f.img.sprite = f.orig;
        }
        foreach (var t in tints)
        {
            if (t.g == null) continue;
            if (on && t.g.color != t.on) t.g.color = t.on;
            else if (off) t.g.color = t.orig;
        }
        if (on && mirrors.Count == 0 && faces.Count > 0 && seg != null)
        {
            foreach (var n in new[] { "WiiClip", "SD", "MailClip" }) MakeMirror(bar.Find(n) as RectTransform);
            var hb = bar.GetComponentInChildren<HdmiButton>(false);
            if (hb != null) MakeMirror((RectTransform) hb.transform);
        }
        foreach (var m in mirrors)
        {
            bool show = on && m.src != null && m.src.activeInHierarchy;
            if (m.copy != null && m.copy.activeSelf != show) m.copy.SetActive(show);
        }
        lastOn = on; first = false;
    }

    private static void Find()
    {
        if (mainMenu == null)
        {
            var go = GameObject.Find("Main Canvas/Menus/MainMenu");
            if (go == null) return;
            mainMenu = go.transform;
        }
        if (backdrop == null)
        {
            var bg = mainMenu.Find("Background");
            var bgo = new GameObject("Glass Backdrop", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform) bgo.transform;
            rt.SetParent(mainMenu, false);
            rt.SetSiblingIndex(bg != null ? bg.GetSiblingIndex() + 1 : 0);
            // the reference's own room (supplied artwork, with the bar glass and cyan curve built in), full screen. The asset
            // is already laid out on the reference, unscaled: its ceiling part raised 18 px so the ring sits 70 px down, its
            // bar part lowered 51 px so the curve (714 / 805 px) and floor ledge (889 px) fall where the reference has them,
            // the two joined behind the top row of channels
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            backdrop = bgo.GetComponent<RawImage>();
            backdrop.texture = BackdropTex(); backdrop.raycastTarget = false;
            backdrop.color = Color.white; // the artwork as painted: no tint
            bgo.SetActive(false);
        }
        if (barTone == null && backdrop != null && BarToneTex() != null)
        {
            // the measured bar light (bartone.py): over the room artwork's bar, under the bar's controls, from 690 px
            // of 941 down
            var go = new GameObject("Glass Bar Tone", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform) go.transform;
            rt.SetParent(mainMenu, false);
            rt.SetSiblingIndex(backdrop.transform.GetSiblingIndex() + 1);
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, (941f - 690f) / 941f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            barTone = go.GetComponent<RawImage>();
            barTone.texture = BarToneTex(); barTone.raycastTarget = false;
            go.SetActive(false);
            first = true;
        }
        if (bar == null)
        {
            bar = mainMenu.Find("Content/BottomBar");
            if (bar != null)
            {
                barImg = bar.GetComponent<Image>();
                if (barImg != null) barOrig = barImg.color;
                var sl = bar.Find("ShadowLine");
                if (sl != null) { shadowLine = sl.GetComponent<Image>(); if (shadowLine != null) shadowOrig = shadowLine.color; }
                date = bar.Find("Date");
                var dt = date != null ? date.GetComponent<TMPro.TMP_Text>() : null;
                if (dt != null) { dateOrig = dt.color; dateStyle = dt.fontStyle; dateSpacing = dt.characterSpacing; dateOutline = dt.outlineWidth; }
                first = true;
            }
        }
        // the clocks are only taken over (hidden behind the 2026 segment clock) while Glass 2026 is on: looking through every
        // object for them once a second cost 30-45 ms on the projector with any other look, a hitch every second
        if (TVGlass.On)
        foreach (var c in UnityEngine.Object.FindObjectsByType<Clock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool known = false;
            foreach (var s in clocks) if (s.clock == c) { known = true; break; }
            if (!known) clocks.Add(new SegClock(c, null));
        }
        clocks.RemoveAll(s => s.clock == null);
    }

    // The 2026 backdrop: the Revo 2026 room artwork (white rotunda, blue ceiling ring, side windows,
    // reflective floor), loaded from Resources at its own 16:9 aspect. A plain cool gradient stands in if it is ever missing.
    private static Texture2D backdropTex;
    public static Texture2D BackdropTex()
    {
        if (backdropTex != null) return backdropTex;
        // shipped as raw PNG bytes and decoded here: no mipmaps, no GPU compression, so it stays as sharp as the artwork
        var bytes = Resources.Load<TextAsset>("Glass2026/glass2026_bg");
        if (bytes != null)
        {
            backdropTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (backdropTex.LoadImage(bytes.bytes, true)) return backdropTex;
        }
        backdropTex = new Texture2D(1, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        backdropTex.SetPixel(0, 0, new Color(0.72f, 0.78f, 0.86f)); backdropTex.SetPixel(0, 1, new Color(0.90f, 0.93f, 0.97f));
        backdropTex.Apply();
        return backdropTex;
    }

    private static Texture2D barToneTex;
    private static bool barToneTried;
    private static Texture2D BarToneTex()
    {
        if (barToneTried) return barToneTex;
        barToneTried = true;
        var bytes = Resources.Load<TextAsset>("Glass2026/glass2026_bar");
        if (bytes == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (t.LoadImage(bytes.bytes, true)) barToneTex = t;
        return barToneTex;
    }

    // white seven-segment digits drawn over the original clock images (those are grey sprites that can't turn white)
    private class SegClock
    {
        public readonly Clock clock;
        private readonly Image[] originals;
        private readonly Image[][] digits = new Image[4][];
        private Transform colon;
        private TMPro.TextMeshProUGUI period;
        private bool built;
        private int lastStamp = -1;

        private static readonly int[] Map = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F }; // bits a..g

        private readonly Transform fixedParent;
        private RectTransform fixedRoot;

        public SegClock(Clock c, Transform fixedParent)
        {
            this.fixedParent = fixedParent;
            clock = c;
            originals = new[] { c.imageHour1, c.imageHour2, c.imageSeparator, c.imagePeriod, c.imageMinute1, c.imageMinute2 };
        }

        private void Build()
        {
            built = true;
            fixedRoot = Layer(fixedParent, "Glass Clock");
            var holders = new[] { Slot(clock.imageHour1), Slot(clock.imageHour2), Slot(clock.imageMinute1), Slot(clock.imageMinute2) };
            for (int i = 0; i < 4; i++)
            {
                if (holders[i] == null) continue;
                var root = Layer(holders[i], "Seg");
                var fit = root.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 0.64f; // the reference's digits are broad
                digits[i] = new Image[7];
                const float tx = 0.17f, ty = 0.15f, gap = 0.03f; // measured on the reference's clock: ~5 px uprights, ~8 px bars on 55 px digits
                Vector2[,] r =
                {
                    { new Vector2(tx * 0.6f, 1f - ty), new Vector2(1f - tx * 0.6f, 1f) },                          // a
                    { new Vector2(1f - tx, 0.5f + gap), new Vector2(1f, 1f - ty * 0.5f) },                          // b
                    { new Vector2(1f - tx, ty * 0.5f), new Vector2(1f, 0.5f - gap) },                               // c
                    { new Vector2(tx * 0.6f, 0f), new Vector2(1f - tx * 0.6f, ty) },                                // d
                    { new Vector2(0f, ty * 0.5f), new Vector2(tx, 0.5f - gap) },                                    // e
                    { new Vector2(0f, 0.5f + gap), new Vector2(tx, 1f - ty * 0.5f) },                               // f
                    { new Vector2(tx * 0.6f, 0.5f - ty * 0.5f), new Vector2(1f - tx * 0.6f, 0.5f + ty * 0.5f) },    // g
                };
                for (int s = 0; s < 7; s++)
                {
                    var seg = Layer(root, "s" + s);
                    seg.anchorMin = r[s, 0]; seg.anchorMax = r[s, 1];
                    var gl = Layer(seg, "g"); gl.offsetMin = -Vector2.one * 11f; gl.offsetMax = Vector2.one * 11f;
                    var gi = gl.gameObject.AddComponent<Image>(); gi.sprite = TVGlass.Capsule(true); gi.type = Image.Type.Sliced; gi.pixelsPerUnitMultiplier = 1f;
                    gi.color = new Color(0.45f, 0.85f, 1f, 0.85f); gi.raycastTarget = false; // the reference's digits glow cyan
                    var core = Layer(seg, "c");
                    var im = core.gameObject.AddComponent<Image>(); im.sprite = TVGlass.Capsule(false); im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 3f;
                    im.color = Color.white; im.raycastTarget = false;
                    gl.gameObject.AddComponent<Slant>().root = root; core.gameObject.AddComponent<Slant>().root = root;
                    digits[i][s] = im;
                }
            }
            if (clock.imageSeparator != null)
            {
                colon = Layer(Slot(clock.imageSeparator), "Colon");
                var cf = colon.gameObject.AddComponent<AspectRatioFitter>();
                cf.aspectMode = AspectRatioFitter.AspectMode.FitInParent; cf.aspectRatio = 0.3f;
                foreach (float y in new[] { 0.28f, 0.68f })
                {
                    var dot = Layer(colon, "dot");
                    dot.anchorMin = new Vector2(0.20f, y - 0.085f); dot.anchorMax = new Vector2(0.80f, y + 0.085f); // the reference's are ~10 px
                    var im = dot.gameObject.AddComponent<Image>(); im.sprite = TVGlass.Capsule(false); im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 3f;
                    im.color = Color.white; im.raycastTarget = false;
                    dot.gameObject.AddComponent<Slant>().root = (RectTransform) colon;
                }
            }
            if (clock.imagePeriod != null)
            {
                var p = Layer(Slot(clock.imagePeriod), "Period");
                period = p.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
                period.fontStyle = TMPro.FontStyles.Bold; period.alignment = TMPro.TextAlignmentOptions.BottomLeft;
                period.enableAutoSizing = true; period.fontSizeMin = 8f; period.fontSizeMax = 200f;
                period.color = Color.white; period.raycastTarget = false; period.margin = new Vector4(34f, 0f, 0f, 0f);
                p.localScale = Vector3.one * 1.6f; p.pivot = new Vector2(0f, 0f); // the reference's AM/PM: 21 px capitals, 35 px after the digits
            }
            // the reference sets its digits further apart (49 px from one to the next, the original clock's images 42):
            // the slots spread out from the middle of the time, their sizes unchanged
            foreach (Transform t in fixedRoot)
            {
                var r = (RectTransform) t;
                r.anchoredPosition = new Vector2(r.anchoredPosition.x * 1.12f, r.anchoredPosition.y);
            }
            // sized and placed from the reference (941 px tall): digits 55 px high, the "2:07" block centred 762 px down
            // in the middle of the screen
            var canvas = fixedParent.GetComponentInParent<Canvas>();
            var hour2 = holders[1];
            if (canvas == null || hour2 == null) return;
            var screen = (RectTransform) canvas.rootCanvas.transform;
            var cs = new Vector3[4];
            screen.GetWorldCorners(cs);
            Vector3 s0 = cs[0], s1 = cs[2];
            hour2.GetWorldCorners(cs);
            float digitH = Mathf.Min(cs[2].y - cs[0].y, (cs[2].x - cs[0].x) / 0.64f); // the digit's aspect fitter
            fixedRoot.localScale *= (55f / 941f) * (s1.y - s0.y) / Mathf.Max(0.001f, digitH);
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var t in new[] { holders[1], colon != null ? (RectTransform) colon.parent : null, holders[2], holders[3] })
            {
                if (t == null) continue;
                t.GetWorldCorners(cs);
                mn = Vector3.Min(mn, cs[0]); mx = Vector3.Max(mx, cs[2]);
            }
            if (float.IsInfinity(mn.x)) return;
            var want = new Vector3(Mathf.Lerp(s0.x, s1.x, 0.4994f), Mathf.Lerp(s1.y, s0.y, 762f / 941f), 0f);
            var have = (mn + mx) * 0.5f;
            fixedRoot.position += new Vector3(want.x - have.x, want.y - have.y, 0f);
            // the reference's AM/PM stands on the digits' baseline (measured: 9 px lower than this one sat)
            if (period != null) period.transform.position += new Vector3(0f, -(9f / 941f) * (s1.y - s0.y), 0f);
        }

        // a rect in the fixed layer covering the same screen area as one of the page clock's images
        private RectTransform Slot(Image src)
        {
            if (src == null) return null;
            var rt = Layer(fixedRoot, src.name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var c = new Vector3[4];
            src.rectTransform.GetWorldCorners(c);
            Vector2 a = fixedRoot.InverseTransformPoint(c[0]), b = fixedRoot.InverseTransformPoint(c[2]);
            rt.sizeDelta = b - a;
            rt.anchoredPosition = (a + b) * 0.5f - fixedRoot.rect.center;
            return rt;
        }

        // the page clocks stay running underneath, just not drawn while the 2026 clock shows
        public void Hide(bool on)
        {
            foreach (var o in originals) if (o != null) o.canvasRenderer.SetAlpha(on ? 0f : 1f);
        }

        private static RectTransform Layer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform) go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public void Tick(bool on)
        {
            if (on && !built) Build();
            if (!built) return;
            fixedRoot.gameObject.SetActive(on);
            if (period != null) period.gameObject.SetActive(PREFS.Is12Format.GetBool());
            if (!on) return;

            var now = DateTime.Now;
            int stamp = now.Hour * 60 + now.Minute;
            if (stamp == lastStamp) return;
            lastStamp = stamp;
            bool h12 = PREFS.Is12Format.GetBool();
            int h = now.Hour;
            if (h12) { h %= 12; if (h == 0) h = 12; }
            int[] val = { h / 10, h % 10, now.Minute / 10, now.Minute % 10 };
            for (int i = 0; i < 4; i++)
            {
                if (digits[i] == null) continue;
                bool blank = i == 0 && val[0] == 0 && h12;
                for (int s = 0; s < 7; s++) digits[i][s].transform.parent.gameObject.SetActive(!blank && (Map[val[i]] & (1 << s)) != 0);
                bool one = val[i] == 1;
                foreach (int s in new[] { 1, 2 })
                {
                    var sr = (RectTransform) digits[i][s].transform.parent;
                    float w = sr.anchorMax.x - sr.anchorMin.x;
                    float x0 = one ? 0.5f - w * 0.5f : 1f - w;
                    sr.anchorMin = new Vector2(x0, sr.anchorMin.y); sr.anchorMax = new Vector2(x0 + w, sr.anchorMax.y);
                }
            }
            if (period != null) period.text = now.Hour < 12 ? "AM" : "PM";
        }
    }
}
