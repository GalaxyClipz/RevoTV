using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// "Glass 2026": frosted-glass finish on top of the classic menu (blue ambient wash, glowing glass rims, soft reflections).
// Everything is a static overlay or a tint, so it costs next to nothing; the toggle in Preferences switches it live.
public static class TVGlass
{
    public static bool On => TVMode.IsTV && PREFS.Glass2026.GetBool();

    private static RectTransform group;
    private static float textCheck;
    private static readonly Dictionary<TMPro.TMP_Text, Color> origText = new Dictionary<TMPro.TMP_Text, Color>();

    private static readonly Dictionary<Image, Color> origShadow = new Dictionary<Image, Color>();
    private static readonly HashSet<GameObject> hiddenMasks = new HashSet<GameObject>();
    private static Texture2D wash, radial, band;
    private static Sprite shadowSoft, rimSoft;

    // tiny procedural glyphs: wi-fi arcs or a gear
    private static Texture2D Icon(bool wifi)
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float a;
                if (wifi)
                {
                    float dx = x + 0.5f - 32f, dy = y + 0.5f - 10f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Abs(Mathf.Atan2(dx, dy)) * Mathf.Rad2Deg;
                    a = r < 5.5f ? 1f : 0f;
                    foreach (float rr in new[] { 20f, 33f, 46f }) if (Mathf.Abs(r - rr) < 3f && ang < 46f) a = 1f;
                }
                else
                {
                    float dx = x + 0.5f - 32f, dy = y + 0.5f - 32f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float tooth = Mathf.Cos(Mathf.Atan2(dy, dx) * 8f) > 0.25f ? 28f : 21f;
                    a = r < tooth && r > 11f ? 1f : 0f;
                }
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }

    public static Texture2D Band()
    {
        if (band != null) return band;
        band = new Texture2D(64, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 64; x++) { float a = Mathf.Sin(x / 63f * Mathf.PI); band.SetPixel(x, 0, new Color(1f, 1f, 1f, a * a)); }
        band.Apply();
        return band;
    }

    // a soft contact shadow sitting just under each tile
    public static RectTransform MakeShadow(Transform tile, Vector2 size)
    {
        if (shadowSoft == null)
        {
            const int n = 64; const float r = 16f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - 14f - r);
                    float qy = Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - 14f - r);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float a = Mathf.Clamp01(1f - d / 13f); a *= a;
                    tex.SetPixel(x, y, new Color(0.06f, 0.14f, 0.34f, a * 0.48f)); // the tiles float a little off the glass wall
                }
            tex.Apply();
            shadowSoft = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(30, 30, 30, 30));
        }
        var go = new GameObject("TV Shadow", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform) go.transform;
        rt.SetParent(tile, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size + ShadowPad;
        rt.anchoredPosition = new Vector2(0f, -12f);
        var im = go.GetComponent<Image>();
        im.sprite = shadowSoft; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 1f; im.raycastTarget = false;
        return rt;
    }

    public const float RimPad = 14f; // room outside the art for the edge's cyan bloom
    public static readonly Vector2 ShadowPad = new Vector2(16f, 16f);

    // a rounded capsule for clock segments (sharp) or its soft glow (blurred)
    private static Sprite capsule, capsuleGlow;
    public static Sprite Capsule(bool glow)
    {
        if (glow ? capsuleGlow != null : capsule != null) return glow ? capsuleGlow : capsule;
        const int n = 48; float r = glow ? 8f : 9f, pad = glow ? 14f : 4f, soft = glow ? 12f : 1.2f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - pad - r);
                float qy = Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - pad - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a = Mathf.Clamp01(0.5f - d / soft);
                if (glow) a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        var b = n * 0.5f - 1f;
        var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        if (glow) capsuleGlow = sp; else capsule = sp;
        return sp;
    }

    public static Sprite RimSoft()
    {
        if (rimSoft != null) return rimSoft;
        // the shape sits RimPad px inside the sprite so a soft blue glow can spill outside the tile's edge
        const int n = 80; const float r = 16f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - RimPad - r);
                float qy = Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - RimPad - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                // a thick bright glass bevel, a soft light just inside it and a blue glow outside
                float line = Mathf.Clamp01(1f - Mathf.Abs(d + 3f) / 3.6f);
                float inner = d < 0f ? Mathf.Clamp01(1f + d / 9f) : 0f;
                float outer = d > 0f ? Mathf.Clamp01(1f - d / RimPad) : 0f;
                float a = Mathf.Clamp01(line + inner * inner * 0.30f + outer * outer * 0.55f);
                var c = Color.Lerp(new Color(0.42f, 0.74f, 1f), new Color(0.95f, 0.99f, 1f), Mathf.Clamp01(line * 1.4f));
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
            }
        tex.Apply();
        rimSoft = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(36, 36, 36, 36));
        return rimSoft;
    }

    private static Texture2D Wash()
    {
        if (wash != null) return wash;
        wash = new Texture2D(1, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float t = y / 63f; // 0 bottom .. 1 top
            Color c;
            if (t < 0.36f) c = new Color(0.28f, 0.40f, 0.62f, 0.10f * Mathf.Pow(1f - t / 0.36f, 1.2f)); // deep glass blue under the bar
            else if (t > 0.8f) c = new Color(0.28f, 0.42f, 0.64f, 0.45f * Mathf.Pow((t - 0.8f) / 0.2f, 1.2f)); // deeper blue ceiling, the light ring sits on it
            else c = new Color(0.5f, 0.55f, 0.65f, 0.0f);                                              // faint cool tint everywhere
            wash.SetPixel(0, y, c);
        }
        wash.Apply();
        return wash;
    }

    public static Texture2D Radial()
    {
        if (radial != null) return radial;
        radial = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x / 63f - 0.5f) * 2f, dy = (y / 63f - 0.5f) * 2f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                radial.SetPixel(x, y, new Color(0.62f, 0.84f, 1f, a * a * 0.22f));
            }
        radial.Apply();
        return radial;
    }

    private static RawImage Raw(Transform parent, string name, Texture tex, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var ri = go.GetComponent<RawImage>();
        ri.texture = tex; ri.raycastTarget = false;
        return ri;
    }

    // called by the screen finish when it builds its overlay
    public static void Build(RectTransform root)
    {
        var go = new GameObject("Glass 2026", typeof(RectTransform));
        group = (RectTransform) go.transform;
        group.SetParent(root, false);
        group.anchorMin = Vector2.zero; group.anchorMax = Vector2.one;
        group.offsetMin = group.offsetMax = Vector2.zero;
        group.SetAsFirstSibling();
        var top = Raw(group, "Light", Band(), new Vector2(0.08f, 0.955f), new Vector2(0.92f, 1.01f));
        top.gameObject.SetActive(false); // the artwork has its own ceiling light

        var tgo = new GameObject("Title", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        var trt = (RectTransform) tgo.transform;
        trt.SetParent(group, false);
        trt.anchorMin = new Vector2(0.02f, 0.94f); trt.anchorMax = new Vector2(0.25f, 0.995f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var tmp = tgo.GetComponent<TMPro.TextMeshProUGUI>();
        tmp.text = "<b>Revo</b> <size=60%><alpha=#B0><cspace=0.08em>2026"; // the year light and spaced, as in the reference
        tmp.fontStyle = TMPro.FontStyles.Normal;
        tmp.alignment = TMPro.TextAlignmentOptions.Left;
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 10f; tmp.fontSizeMax = 48f;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        var mat = tmp.fontMaterial; // soft dark underlay so the white title reads on the light ceiling
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0.03f, 0.10f, 0.26f, 0.95f));
        mat.SetFloat("_UnderlayOffsetX", 0.5f); mat.SetFloat("_UnderlayOffsetY", -0.5f); mat.SetFloat("_UnderlayDilate", 0.35f); mat.SetFloat("_UnderlaySoftness", 0.7f);

        // system indicators, top right: wi-fi and settings, white like the title
        var iconCol = Color.white;
        float icon = root.rect.height * 0.034f; // the reference's size and spot
        var wifi = Raw(group, "Wifi", Icon(true), new Vector2(0.930f, 0.957f), new Vector2(0.930f, 0.957f)); wifi.color = iconCol;
        var gear = Raw(group, "Gear", Icon(false), new Vector2(0.966f, 0.957f), new Vector2(0.966f, 0.957f)); gear.color = iconCol;
        foreach (var r in new[] { wifi, gear })
        {
            r.rectTransform.sizeDelta = new Vector2(icon, icon);
            var sh = r.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.03f, 0.10f, 0.26f, 0.85f); sh.effectDistance = new Vector2(2f, -2f);
            // pressable: the screen finish around them lets clicks through, these two take them
            r.raycastTarget = true;
            var cg = r.gameObject.AddComponent<CanvasGroup>(); cg.ignoreParentGroups = true;
            var b = r.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None;
            bool isWifi = r == wifi;
            b.onClick.AddListener(() =>
            {
                if (AndroidLinker._instance == null) return;
                AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
                if (isWifi) AndroidLinker._instance.OpenWifiSettings(); else AndroidLinker._instance.OpenPhoneGeneralSettings();
            });
        }

    }

    public static void Tick(float dt)
    {
        if (group == null) return;
        bool on = On;
        // title, indicators and room light belong to the home menu only (not over settings, channels or popups)
        var app = AppController._instance;
        bool home = on && app != null && app.mainView.activeInHierarchy && !app.warningView.activeInHierarchy
                    && ChannelController._instance != null && ChannelController._instance.currentChannel == null
                    && !PopupController.IsPopupOpen() && !TVWeather.Open && !TVGameScreen.Open;
        if (group.gameObject.activeSelf != home) group.gameObject.SetActive(home);
        TV2026.Tick(on);
        textCheck -= dt; // page backgrounds are stopped at the source (Grid.EnableBackground), so this can be throttled
        if (textCheck > 0f) return;
        textCheck = 0.2f;
        TintBackground(on);
    }

    // the silver background gets a cool icy tint while the look is on
    private static readonly List<Grid> grids = new List<Grid>();
    public static void KeepHidden(GameObject go)
    {
        go.SetActive(false);
        hiddenMasks.Add(go);
    }
    private static void TintBackground(bool on)
    {
        var gc = GridController._instance;
        if (gc == null || gc.currentGrid == null) return;
        var host = gc.currentGrid.transform.parent != null ? gc.currentGrid.transform.parent : gc.currentGrid.transform;
        host.GetComponentsInChildren(true, grids);
        foreach (var g in grids)
        {
            // each page's own copy of the curve is hidden: the bar holds one fixed copy
            var bc = g.transform.Find("BottomClip");
            var bcg = bc != null ? bc.GetComponent<Graphic>() : null;
            if (bcg != null) bcg.canvasRenderer.SetAlpha(on ? 0f : 1f);
            // the flat cyan line across the top of the bar: the 2026 bar only has the curve
            var tl = g.transform.Find("TopLine");
            if (tl != null)
            {
                if (on && tl.gameObject.activeSelf) { tl.gameObject.SetActive(false); hiddenMasks.Add(tl.gameObject); }
                else if (!on && hiddenMasks.Remove(tl.gameObject)) tl.gameObject.SetActive(true);
            }
            // each page has its own masked background (it also fills the dip around the clock): hide it so the 2026 backdrop shows
            if (g.backMask != null)
            {
                if (on && g.backMask.activeSelf) { g.backMask.SetActive(false); hiddenMasks.Add(g.backMask); }
                else if (!on && hiddenMasks.Remove(g.backMask)) g.backMask.SetActive(true);
            }
            // the soft white "Shadow" under the grid: the room artwork has its own floor there
            var sh = g.transform.Find("Shadow");
            var si = sh != null ? sh.GetComponent<Image>() : null;
            if (si != null)
            {
                if (!origShadow.ContainsKey(si)) origShadow[si] = si.color;
                var o = origShadow[si];
                var want = on ? new Color(1f, 1f, 1f, 0f) : o;
                if (si.color != want) si.color = want;
            }
        }
    }
}
