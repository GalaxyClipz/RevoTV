using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TV: a power button in the top-left corner of the home menu (every look and theme, 2026 included). It opens a panel with
/// Sleep, Restart and Power off. Sleep turns the screen off at once; Android lets an app restart or power off the TV only
/// through the TV's own power menu, so those two open it (one more OK there). Both go through RevoTV's accessibility service.
/// </summary>
public class TVPower : MonoBehaviour
{
    public static GameObject ButtonGo => me != null && me.button != null && me.button.gameObject.activeInHierarchy ? me.button.gameObject : null;
    public static Transform PanelRoot => panel != null && panel.activeInHierarchy ? panel.transform : null;
    private static GameObject panel;
    private static TVPower me;
    private RectTransform button;
    private CanvasGroup fade; private float homeSince = -1f, own;
    private static Sprite round, ring, powerIcon;

    public static void Ensure(MonoBehaviour host)
    {
        if (!TVMode.IsTV || me != null) return;
        me = host.gameObject.AddComponent<TVPower>();
    }

    // a Yes/No popup that would undo something (log out, delete, remove, reset) starts with the light on No: an OK pressed a moment
    // too late used to answer Yes (that logged the Content Hub account out)
    private GameObject lastPopup;
    private void SafePopupFocus()
    {
        var pop = PopupController.CurrentPopup;
        if (pop == null || !pop.activeInHierarchy) { lastPopup = null; return; }
        if (pop == lastPopup) return;
        lastPopup = pop;
        string txt = "";
        foreach (var t in pop.GetComponentsInChildren<TMP_Text>(false)) txt += " " + t.text.ToLowerInvariant();
        if (!(txt.Contains("logout") || txt.Contains("log out") || txt.Contains("delete") || txt.Contains("remove") || txt.Contains("reset") || txt.Contains("erase"))) return;
        foreach (var b in pop.GetComponentsInChildren<Button>(false))
            if (b.name == "Button_No" && b.interactable) { StartCoroutine(FocusSoon(b.gameObject)); Debug.Log("[NAV] risky popup: light starts on No"); break; }
    }
    private System.Collections.IEnumerator FocusSoon(GameObject go) { yield return null; yield return null; TVNav.FocusButton(go); }

    private void Update()
    {
        SafePopupFocus();
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        if (button == null) Build(app.overlayCanvas);
        var cc = ChannelController._instance; var sc = SettingsController._instance; var shop = ShopController._instance;
        bool home = app.mainView != null && app.mainView.activeInHierarchy && !app.warningView.activeInHierarchy && !AppController.IsTransitioning()
                    && (cc == null || cc.currentChannel == null) && (sc == null || sc.settingsView == null || !sc.settingsView.activeInHierarchy)
                    && (shop == null || shop.shopMenu == null || !shop.shopMenu.activeInHierarchy)
                    && !TVWeather.Open && !TVServerScreen.Open && !TVControllers.Open && !TVGameScreen.Open && !TVMove.Active;
        bool show = home && (panel == null || !panel.activeSelf);
        // it comes in with the menu: a soft fade once the menu has been up a moment (it used to pop up on its own first)
        if (!home) homeSince = -1f; else if (homeSince < 0f) homeSince = Time.unscaledTime;
        if (button.gameObject.activeSelf != show) { button.gameObject.SetActive(show); if (show && Time.unscaledTime - homeSince < 0.1f) own = 0f; }
        // it comes in with the menu: it follows the menu's own fade (the black cover after the warning screen; it sat on top of that at
        // full strength = popped up) and after the start-up loading cover it fades in as the menu shows
        var fc = FadeController._instance;
        float cover = fc != null && fc.cover != null && fc.cover.gameObject.activeInHierarchy ? fc.cover.color.a : 0f;
        if (TVPerf.SplashOffAt >= 0f && cover > 0.02f) own = 1f - cover;
        else if (show && own < 1f && Time.unscaledTime > Mathf.Max(homeSince + 0.15f, TVPerf.SplashOffAt + 0.1f)) own = Mathf.Min(1f, own + Time.unscaledDeltaTime / 0.6f);
        if (TVPerf.SplashOffAt < 0f) own = 0f;
        if (fade != null) fade.alpha = Mathf.Min(own, 1f - cover);
        if (panel != null && panel.activeSelf && !home) panel.SetActive(false);
        // the remote's light sits under this panel: the focused button lights up itself (bigger, brighter blue, soft halo)
        if (panel != null && panel.activeSelf)
        {
            var f = TVNav.Focused;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            foreach (var b in panel.GetComponentsInChildren<Button>())
            {
                bool on = f != null && (f == b.gameObject || f.transform.IsChildOf(b.transform));
                b.transform.localScale = Vector3.one * (on ? 1.08f : 1f);
                var rim = b.transform.Find("Rim")?.GetComponent<Image>();
                if (rim != null) rim.color = on ? Color.Lerp(new Color(0.15f, 0.6f, 1f, 1f), new Color(0.45f, 0.8f, 1f, 1f), pulse) : new Color(0.35f, 0.68f, 0.95f, 0.6f);
                var bg = b.targetGraphic as Image;
                if (bg != null) bg.color = on ? (TVGlass.On ? new Color(0.12f, 0.35f, 0.6f, 0.95f) : new Color(0.82f, 0.92f, 1f, 1f)) : Idle();
            }
        }
        // the corner button lights the same way when it holds the light
        if (button != null && button.gameObject.activeSelf)
        {
            var f = TVNav.Focused; bool on = f != null && (f == button.gameObject || f.transform.IsChildOf(button));
            button.localScale = Vector3.one * (on ? 1.12f : 1f);
            var rim = button.Find("Ring")?.GetComponent<Image>();
            if (rim != null) rim.color = on ? new Color(0.2f, 0.75f, 1f, 1f) : TVGlass.On ? new Color(0.45f, 0.85f, 1f, 0.8f) : new Color(0.25f, 0.62f, 0.9f, 0.9f);
        }
    }

    // ---------------------------------------------------------------- look

    private static Sprite Circle(int s, float thick)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s]; float r = s / 2f - 1f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Mathf.Sqrt((x - s / 2f + 0.5f) * (x - s / 2f + 0.5f) + (y - s / 2f + 0.5f) * (y - s / 2f + 0.5f));
            float a = Mathf.Clamp01(r - d + 0.5f);
            if (thick > 0f) a *= Mathf.Clamp01(d - (r - thick) + 0.5f);
            px[y * s + x] = new Color32(255, 255, 255, (byte) (a * 255));
        }
        t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(s / 2f - 1, s / 2f - 1, s / 2f - 1, s / 2f - 1));
    }

    // the power symbol: an open ring with a bar through its gap
    private static Sprite PowerIcon(int s)
    {
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        float c = s / 2f, r = s * 0.34f, w = s * 0.075f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - c + 0.5f, dy = y - c + 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // 0 = straight up
            float ringA = Mathf.Clamp01(w - Mathf.Abs(d - r) + 0.5f) * (Mathf.Abs(ang) > 38f ? 1f : 0f);
            float barA = Mathf.Abs(dx) < w ? Mathf.Clamp01(Mathf.Min(dy - s * 0.02f, s * 0.47f - dy) + 0.5f) * Mathf.Clamp01(w - Mathf.Abs(dx) + 0.5f) : 0f;
            float a = Mathf.Max(ringA, barA);
            px[y * s + x] = new Color32(255, 255, 255, (byte) (a * 255));
        }
        t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
    }

    private static Image Img(Transform parent, string name, Sprite sp, Color col)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var im = go.GetComponent<Image>();
        im.sprite = sp; im.color = col; im.type = sp != null && sp.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        return im;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    private void Build(RectTransform overlay)
    {
        round = round ?? Circle(64, 0f);
        ring = ring ?? Circle(64, 3f);
        powerIcon = powerIcon ?? PowerIcon(64);
        float h = overlay.rect.height > 1f ? overlay.rect.height : 720f;
        float s = h * 0.06f;

        // the corner button: a frosted round button with a blue ring, like the bar's own round buttons
        // 2026 has its "Revo 2026" title in that corner: there the button sits top-right beside the Wi-Fi and gear icons, in glass
        bool glass = TVGlass.On;
        var bg = Img(overlay, "Power Button", round, glass ? new Color(0.05f, 0.12f, 0.25f, 0.55f) : new Color(1f, 1f, 1f, 0.82f));
        button = bg.rectTransform;
        button.pivot = new Vector2(0.5f, 0.5f);
        if (glass)
        {
            s = h * 0.05f;
            button.anchorMin = button.anchorMax = new Vector2(1f, 1f);
            button.anchoredPosition = new Vector2(-h * 0.19f, -h * 0.042f);
        }
        else
        {
            button.anchorMin = button.anchorMax = new Vector2(0f, 1f);
            button.anchoredPosition = new Vector2(s * 0.85f, -s * 0.62f);
        }
        button.sizeDelta = new Vector2(s, s);
        var rim = Img(button, "Ring", ring, glass ? new Color(0.45f, 0.85f, 1f, 0.8f) : new Color(0.25f, 0.62f, 0.9f, 0.9f)); TVServerScreen.Stretch(rim.rectTransform); rim.raycastTarget = false;
        var ic = Img(button, "Icon", powerIcon, glass ? Color.white : new Color(0.33f, 0.4f, 0.5f, 1f)); ic.raycastTarget = false;
        Place(ic.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s * 0.58f, s * 0.58f));
        var b = button.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.transition = Selectable.Transition.None;
        b.onClick.AddListener(Open);
        button.SetAsLastSibling();
        fade = button.gameObject.AddComponent<CanvasGroup>(); fade.alpha = 0f;
    }

    private static void Open()
    {
        var app = AppController._instance;
        if (app == null) return;
        if (panel == null) BuildPanel(app.overlayCanvas);
        panel.transform.SetAsLastSibling();
        panel.SetActive(true);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    // closing goes back to the power button (the light used to jump to whatever tile was nearest)
    private static void Close()
    {
        if (panel != null) panel.SetActive(false);
        if (me != null && me.button != null) { me.button.gameObject.SetActive(true); TVNav.FocusDirect(me.button.gameObject); }
    }

    private static void BuildPanel(RectTransform overlay)
    {
        float h = overlay.rect.height > 1f ? overlay.rect.height : 720f;
        var font = TVServerScreen.SettingsFont();
        var dim = Img(overlay, "Power Menu", null, new Color(0.02f, 0.05f, 0.1f, 0.55f));
        panel = dim.gameObject;
        TVServerScreen.Stretch(dim.rectTransform);

        bool g26 = TVGlass.On;
        var card = Img(dim.transform, "Card", Circle(96, 0f), g26 ? new Color(0.05f, 0.11f, 0.22f, 0.92f) : new Color(0.97f, 0.98f, 1f, 0.94f));
        card.pixelsPerUnitMultiplier = 0.55f;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(h * 1.05f, h * 0.56f));
        var edge = Img(card.transform, "Edge", Circle(96, 4f), new Color(0.35f, 0.68f, 0.95f, 0.85f));
        edge.pixelsPerUnitMultiplier = 0.55f; edge.raycastTarget = false; TVServerScreen.Stretch(edge.rectTransform);

        // Cancel first in the hierarchy: Back presses it
        Btn(card.transform, "Cancel Button", "Cancel", font, h, new Vector2(0f, -h * 0.19f), new Vector2(h * 0.3f, h * 0.075f), false, Close);

        var title = TVServerScreen.Text(card.transform, "Power", font, h * 0.06f, g26 ? Color.white : new Color(0.25f, 0.3f, 0.38f));
        title.alignment = TextAlignmentOptions.Center; title.raycastTarget = false;
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, h * 0.19f), new Vector2(h * 0.8f, h * 0.09f));

        float bw = h * 0.28f, bh = h * 0.16f, gap = h * 0.04f;
        Btn(card.transform, "Sleep Button", "Sleep", font, h, new Vector2(-(bw + gap), h * 0.01f), new Vector2(bw, bh), true, () => Do("sleep"));
        Btn(card.transform, "Restart Button", "Restart", font, h, new Vector2(0f, h * 0.01f), new Vector2(bw, bh), true, () => Do("restart"));
        Btn(card.transform, "Power Off Button", "Power off", font, h, new Vector2(bw + gap, h * 0.01f), new Vector2(bw, bh), true, () => Do("off"));
        panel.SetActive(false);
    }

    private static Color Idle() => TVGlass.On ? new Color(0.1f, 0.2f, 0.35f, 0.85f) : new Color(0.93f, 0.95f, 0.98f, 1f);

    private static void Btn(Transform parent, string name, string label, TMP_FontAsset font, float h, Vector2 pos, Vector2 size, bool big, UnityEngine.Events.UnityAction act)
    {
        var bg = Img(parent, name, Circle(64, 0f), Idle());
        bg.pixelsPerUnitMultiplier = big ? 1.6f : 2.2f;
        Place(bg.rectTransform, new Vector2(0.5f, 0.5f), pos, size);
        var rim = Img(bg.transform, "Rim", Circle(64, 3f), new Color(0.35f, 0.68f, 0.95f, big ? 0.9f : 0.6f));
        rim.pixelsPerUnitMultiplier = bg.pixelsPerUnitMultiplier; rim.raycastTarget = false; TVServerScreen.Stretch(rim.rectTransform);
        var t = TVServerScreen.Text(bg.transform, label, font, h * (big ? 0.042f : 0.036f), TVGlass.On ? Color.white : new Color(0.22f, 0.3f, 0.4f));
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; TVServerScreen.Stretch(t.rectTransform);
        var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.transition = Selectable.Transition.None;
        b.onClick.AddListener(act);
    }

    private static void Do(string what)
    {
        Close();
        string res = "off";
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (what == "restart")
            {
                // Android TV's own Settings app restarts the TV when asked (the power menu only offered power off here);
                // a TV without that: the TV's power menu after a moment
                using var rv = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV");
                rv.CallStatic<bool>("restart", AndroidLinker.currentActivity);
                me.StartCoroutine(RestartFallback());
                Debug.Log("[Power] restart asked of the TV's Settings");
                return;
            }
            using var hs = new AndroidJavaClass("com.karuzohikari.revotv.HomeService");
            res = hs.CallStatic<bool>("power", what == "sleep" ? "sleep" : "menu") ? "ok" : "off";
        }
        catch (System.Exception e) { Debug.LogWarning("[Power] " + e.Message); }
#endif
        Debug.Log("[Power] " + what + ": " + res);
        if (res != "ok") PopupController.ShowPopup("tv.power.noservice", PopupController.ClosePopup);
    }

    private static System.Collections.IEnumerator RestartFallback()
    {
        yield return new WaitForSecondsRealtime(3f);
        // still here: this TV doesn't take the request
        Debug.Log("[Power] restart not taken by this TV");
        PopupController.ShowPopup("tv.power.norestart", PopupController.ClosePopup);
    }
}
