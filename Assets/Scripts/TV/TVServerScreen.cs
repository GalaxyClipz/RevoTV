using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Channel Server's own screen (Settings > Revo Launcher > Channel Server). Opening it turns the server on and
/// shows the address to type on a phone or computer; Back (the button or the remote's Back) closes it and turns the
/// server off, so nothing keeps running in the background. Drawn in the Settings screens' style: black with the
/// striped band, a white title tab and a white Back pill.
/// </summary>
public static class TVServerScreen
{
    private static GameObject root;
    private static TextMeshProUGUI status;
    private static Image dot;
    private static Runner runner;

    public static bool Open => root != null;

    // set before a restart the phone asked for (a theme): after the restart, once the menu is up, this screen opens again
    public const string ReopenKey = "tv.server.reopen";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV || PlayerPrefs.GetInt(ReopenKey, 0) == 0) return;
        PlayerPrefs.SetInt(ReopenKey, 0);
        new GameObject("TVServerReopen").AddComponent<Reopener>();
    }
    private class Reopener : MonoBehaviour
    {
        private System.Collections.IEnumerator Start()
        {
            float until = Time.unscaledTime + 600f;
            while (Time.unscaledTime < until)
            {
                var app = AppController._instance;
                if (app != null && app.finishedLoading && !app.warningView.activeInHierarchy && !AppController.IsTransitioning()) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(1f);
            if (Time.unscaledTime < until && !Open) { Debug.Log("[Server] back on after the restart"); Show(); }
            Destroy(gameObject);
        }
    }
    // hidden for the one frame the theme preview picture is taken (TVHubPost)
    public static void SetHidden(bool hidden) { if (root != null) root.SetActive(!hidden); }

    public static void Show()
    {
        if (Open) return;
        if (!TVChannelServer.Running && !TVChannelServer.Start())
        {
            PopupController.ShowPopup("channelserver.nonet");
            return;
        }
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        Build(app.overlayCanvas);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    public static void Close()
    {
        if (!Open) return;
        TVChannelServer.Stop();
        Object.Destroy(root);
        root = null;
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    // ---------------------------------------------------------------- building

    private static void Build(RectTransform parent)
    {
        root = new GameObject("TV Channel Server Screen", typeof(RectTransform));
        var rt = (RectTransform) root.transform;
        rt.SetParent(parent, false);
        rt.SetAsFirstSibling(); // under popups and the remote's light
        Stretch(rt);
        float h = parent.rect.height > 1f ? parent.rect.height : 1080f;
        var font = SettingsFont();

        // black, with the Settings screens' striped band across the middle
        var bg = Child(rt, "Background").gameObject.AddComponent<Image>();
        bg.color = new Color(0.035f, 0.035f, 0.04f, 1f);
        bg.raycastTarget = true; // covers the screen below: the light only finds this screen's Back
        var band = Child(rt, "Band");
        band.anchorMin = new Vector2(0f, 0.22f); band.anchorMax = new Vector2(1f, 0.78f);
        var stripes = band.gameObject.AddComponent<RawImage>();
        stripes.texture = StripeTex();
        stripes.uvRect = new Rect(0f, 0f, 1f, h * 0.56f / 6f);
        stripes.raycastTarget = false;
        Line(rt, 0.78f, h); Line(rt, 0.22f, h);

        // the white title tab, top left, like "Settings" / "Preferences"
        var tab = Child(rt, "Tab");
        tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f);
        tab.pivot = new Vector2(0f, 1f);
        tab.anchoredPosition = new Vector2(h * 0.045f, -h * 0.12f);
        tab.sizeDelta = new Vector2(h * 0.66f, h * 0.105f);
        var tabImg = tab.gameObject.AddComponent<Image>();
        tabImg.sprite = TabSprite(); tabImg.type = Image.Type.Sliced; tabImg.raycastTarget = false;
        var title = Text(tab, TextController.GetTranslation("channelserver.title"), font, h * 0.05f, new Color(0.2f, 0.2f, 0.22f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        Inset((RectTransform) title.transform, h * 0.045f, 0f);

        // the middle: what to do, the address, what you can do there, and whether a phone is connected
        var card = Child(rt, "Card");
        card.anchorMin = new Vector2(0.08f, 0.24f); card.anchorMax = new Vector2(0.92f, 0.76f);
        var steps = Text(card, TextController.GetTranslation("channelserver.steps"), font, h * 0.036f, new Color(0.86f, 0.88f, 0.9f));
        Band((RectTransform) steps.transform, 0.80f, 0.98f);
        var addr = Text(card, TVChannelServer.Url ?? "", font, h * 0.068f, new Color(0.36f, 0.82f, 1f));
        addr.fontStyle = FontStyles.Bold;
        addr.enableAutoSizing = true; addr.fontSizeMin = h * 0.03f; addr.fontSizeMax = h * 0.068f;
        Band((RectTransform) addr.transform, 0.56f, 0.78f);
        var what = Text(card, TextController.GetTranslation("channelserver.what"), font, h * 0.032f, new Color(0.7f, 0.72f, 0.76f));
        Band((RectTransform) what.transform, 0.26f, 0.52f);

        var statusRow = Child(card, "Status");
        statusRow.anchorMin = new Vector2(0f, 0.04f); statusRow.anchorMax = new Vector2(1f, 0.2f);
        status = Text(statusRow, "", font, h * 0.032f, new Color(0.86f, 0.88f, 0.9f));
        dot = Child(statusRow, "Dot").gameObject.AddComponent<Image>();
        dot.sprite = DotSprite(); dot.raycastTarget = false;
        var drt = dot.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(h * 0.022f, h * 0.022f);

        // the Back pill, bottom left, where every Settings screen has it
        var back = Child(rt, "Back");
        back.anchorMin = back.anchorMax = new Vector2(0f, 0f);
        back.pivot = new Vector2(0f, 0f);
        back.anchoredPosition = new Vector2(h * 0.065f, h * 0.06f);
        back.sizeDelta = new Vector2(h * 0.4f, h * 0.11f);
        var backImg = back.gameObject.AddComponent<Image>();
        backImg.sprite = PillSprite(); backImg.type = Image.Type.Sliced;
        var btn = back.gameObject.AddComponent<Button>();
        btn.targetGraphic = backImg;
        btn.onClick.AddListener(Close);
        Text(back, "← Back", font, h * 0.055f, new Color(0.15f, 0.15f, 0.17f));
        var hint = Text(rt, TextController.GetTranslation("channelserver.back"), font, h * 0.026f, new Color(0.55f, 0.57f, 0.6f));
        var hrt = (RectTransform) hint.transform;
        hrt.anchorMin = new Vector2(0.3f, 0f); hrt.anchorMax = new Vector2(0.95f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, h * 0.085f);
        hrt.sizeDelta = new Vector2(0f, h * 0.05f);
        hint.alignment = TextAlignmentOptions.MidlineRight;

        runner = root.AddComponent<Runner>();
    }

    // keeps the status line current and closes the screen if the server stops on its own
    private class Runner : MonoBehaviour
    {
        private float t;
        private void Update()
        {
            t += Time.unscaledDeltaTime;
            if (!TVChannelServer.Running) { Close(); return; }
            bool connected = TVChannelServer.SecondsSinceRequest < 20f;
            status.text = TextController.GetTranslation(connected ? "channelserver.connected" : "channelserver.waiting");
            // the dot sits just left of the centred line
            float w = status.preferredWidth;
            dot.rectTransform.anchoredPosition = new Vector2(-w * 0.5f - dot.rectTransform.sizeDelta.x * 1.2f, 0f);
            float pulse = connected ? 1f : 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 2.2f));
            dot.color = connected ? new Color(0.35f, 0.9f, 0.5f, 1f) : new Color(0.36f, 0.82f, 1f, pulse);
        }
    }

    // ---------------------------------------------------------------- pieces

    internal static TMP_FontAsset SettingsFont()
    {
        var sc = SettingsController._instance;
        if (sc != null && sc.settingsView != null)
        {
            var t = sc.settingsView.GetComponentInChildren<TMP_Text>(true);
            if (t != null && t.font != null) return t.font;
        }
        return TMP_Settings.defaultFontAsset;
    }

    internal static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        Stretch(rt);
        return rt;
    }

    internal static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    internal static void Band(RectTransform rt, float y0, float y1)
    {
        rt.anchorMin = new Vector2(0f, y0); rt.anchorMax = new Vector2(1f, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    internal static void Inset(RectTransform rt, float x, float y)
    {
        rt.offsetMin = new Vector2(x, y); rt.offsetMax = new Vector2(-x, -y);
    }

    internal static TextMeshProUGUI Text(Transform parent, string s, TMP_FontAsset font, float size, Color c)
    {
        var rt = Child(parent, "Text");
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = s;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = true;
        t.raycastTarget = false;
        return t;
    }

    internal static void Line(RectTransform parent, float y, float h)
    {
        var l = Child(parent, "Line");
        l.anchorMin = new Vector2(0f, y); l.anchorMax = new Vector2(1f, y);
        l.sizeDelta = new Vector2(0f, Mathf.Max(2f, h * 0.007f));
        var img = l.gameObject.AddComponent<Image>();
        img.color = new Color(0.92f, 0.93f, 0.95f, 1f);
        img.raycastTarget = false;
    }

    private static Texture2D stripeTex;
    internal static Texture2D StripeTex()
    {
        if (stripeTex != null) return stripeTex;
        stripeTex = new Texture2D(1, 6, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (int y = 0; y < 6; y++) stripeTex.SetPixel(0, y, y < 3 ? new Color(0.2f, 0.2f, 0.21f) : new Color(0.1f, 0.1f, 0.11f));
        stripeTex.Apply();
        return stripeTex;
    }

    // rounded shapes, drawn once: a white tab (round top corners), the Back pill (white, cyan ring) and the status dot
    private static Sprite tab, pill, dotSprite;

    internal static Sprite TabSprite() => tab != null ? tab : tab = Rounded(96, 40, new Color(0.97f, 0.97f, 0.98f), Color.clear, 0, true);
    internal static Sprite PillSprite() => pill != null ? pill : pill = Rounded(128, 56, new Color(0.96f, 0.97f, 0.98f), new Color(0.12f, 0.65f, 0.9f), 6, false);
    internal static Sprite DotSprite() => dotSprite != null ? dotSprite : dotSprite = Rounded(32, 16, Color.white, Color.clear, 0, false, false);

    internal static Sprite Rounded(int n, int radius, Color fill, Color ring, int ringWidth, bool topOnly, bool sliced = true)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
            if (topOnly && y + 0.5f < n - radius) cy = y + 0.5f; // square bottom corners
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            float a = Mathf.Clamp01(radius - d + 0.5f);
            if (topOnly && y + 0.5f < n - radius) a = 1f;
            float inner = Mathf.Clamp01(radius - ringWidth - d + 0.5f);
            // a soft top-lit face, like the Wii's buttons
            Color face = Color.Lerp(fill * 0.9f, fill, Mathf.SmoothStep(0f, 1f, y / (float) n));
            face.a = 1f;
            Color px = ringWidth > 0 ? Color.Lerp(ring, face, inner) : face;
            px.a = a;
            tex.SetPixel(x, y, px);
        }
        tex.Apply();
        float b = radius + 2;
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            sliced ? new Vector4(b, b, b, b) : Vector4.zero);
    }
}
