using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Round Wii-menu style "HDMI 1" button that lives in the home menu's bottom bar (TV only).
/// Built at runtime next to the mail button, so it hides/shows with the rest of the bar.
/// </summary>
public class HdmiButton : MonoBehaviour, IPointerClickHandler
{
    // where the button sits, as fractions of the screen (from the left / from the top) and its diameter (of screen height)
    private const float CenterX = 0.805f;
    private const float CenterY = 0.885f;
    private const float Diameter = 0.115f;

    private static readonly Color LabelColor = new Color(0.45f, 0.46f, 0.50f);

    private TextMeshProUGUI label;
    private bool showingError;
    // when Revo has just come back to the front (from Photos, a game, the TV's settings...), a press still on its way from
    // what was open before must not switch the TV to HDMI
    private static float backAt = -10f;
    private void OnApplicationFocus(bool on) { if (on) backAt = Time.unscaledTime; }
    private int shownPort;
    private CanvasGroup group;
    private Image face;
    private Sprite lightSprite, darkSprite;
    private bool? glassShown, sizedFor;
    private Vector2 basePos, baseSize;

    // only on the plain home menu: not over the warning screen, popups or an open channel
    private void Update()
    {
        var app = AppController._instance;
        bool visible = app != null && app.finishedLoading && app.mainView.activeInHierarchy && !app.warningView.activeInHierarchy
                       && !PopupController.IsPopupOpen() && !PopupController.IsBlockerOpen()
                       && PREFS.HdmiButton.GetBool() && TVHdmi.HasHdmiInput(); // a TV without HDMI inputs never shows it
        bool clickable = visible && ChannelController._instance != null && ChannelController._instance.currentChannel == null;
        if (label != null && !showingError && shownPort != PREFS.HdmiPort.GetInt()) SetLabel();
        bool glass = TVGlass.On;
        // the classic bar is drawn smaller (TV2026.ClassicBarSize): this button follows it, about the screen's bottom middle
        var rt = (RectTransform) transform;
        if (baseSize == Vector2.zero) { basePos = rt.anchoredPosition; baseSize = rt.sizeDelta; }
        if (sizedFor != glass)
        {
            sizedFor = glass;
            float k = glass ? 1f : TV2026.ClassicBarScale;
            // the screen's bottom middle in the button's own position space (the menu panel is taller than the screen)
            var parent = (RectTransform) rt.parent;
            var canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(Screen.width * 0.5f, 0f), cam, out Vector2 local);
            var bottom = local - parent.rect.center;
            // the classic bar's round buttons sit higher (TV2026.ClassicLift): this one with them
            Vector3 lw0, lw1;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(0f, 0f), cam, out lw0);
            RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(0f, glass ? 0f : Screen.height * TV2026.ClassicLift), cam, out lw1);
            Vector3 lift = parent.InverseTransformVector(lw1 - lw0);
            var off = basePos - bottom;
            rt.anchoredPosition = bottom + new Vector2(off.x * (glass ? 1f : 1.08f), off.y * (glass ? 1f : TV2026.ClassicBarYScale)) + new Vector2(0f, lift.y);
            rt.sizeDelta = baseSize * k;
        }
        if (face != null && glassShown != glass)
        {
            glassShown = glass;
            if (glass && darkSprite == null) darkSprite = MakeButtonSprite(true);
            face.sprite = glass ? darkSprite : lightSprite;
            if (label != null) label.color = glass ? new Color(0.92f, 0.97f, 1f) : LabelColor;
        }
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = clickable;
            group.interactable = clickable;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        new GameObject("HdmiButtonSpawner").AddComponent<Spawner>();
    }

    private class Spawner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // wait for the home menu to exist
            while (AppController._instance == null || AppController._instance.mainView == null) yield return null;
            yield return null;
            try { Build((RectTransform) AppController._instance.mainView.transform); }
            catch (System.Exception e) { Debug.LogError("[HDMI] button build failed: " + e); }
            Destroy(gameObject);
        }
    }

    private static void Build(RectTransform parent)
    {
        var canvas = parent.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        var go = new GameObject("HDMI 1 Button", typeof(RectTransform), typeof(Image), typeof(WiiButton), typeof(HdmiButton), typeof(CanvasGroup));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        // place by screen position so it lines up with the rest of the bar at any resolution
        float diameter = Diameter * Screen.height;
        Vector2 center = new Vector2(CenterX * Screen.width, (1f - CenterY) * Screen.height);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, center, cam, out Vector3 world);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, center + new Vector2(diameter, 0), cam, out Vector3 worldEdge);
        rt.position = world;
        float size = Vector3.Distance(world, worldEdge) / Mathf.Max(0.0001f, rt.lossyScale.x);
        rt.sizeDelta = new Vector2(size, size);

        var image = go.GetComponent<Image>();
        image.sprite = MakeButtonSprite(false);
        image.raycastTarget = true;

        var wii = go.GetComponent<WiiButton>();
        wii.multiplier = 1.12f;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var trt = (RectTransform) textGo.transform;
        trt.SetParent(rt, false);
        trt.localScale = Vector3.one;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.anchorMin = new Vector2(0.17f, 0.17f); // keep the text inside the cyan ring
        trt.anchorMax = new Vector2(0.83f, 0.83f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var btn = go.GetComponent<HdmiButton>();
        btn.label = textGo.GetComponent<TextMeshProUGUI>();
        btn.label.alignment = TextAlignmentOptions.Center;
        btn.label.fontStyle = FontStyles.Bold;
        btn.label.enableAutoSizing = true;
        btn.label.fontSizeMin = 4;
        btn.label.fontSizeMax = size * 0.24f;
        btn.label.color = LabelColor;
        btn.label.raycastTarget = false;
        btn.label.enableWordWrapping = false;
        btn.SetLabel();
        btn.group = go.GetComponent<CanvasGroup>();
        btn.group.alpha = 0f; btn.group.blocksRaycasts = false; // hidden until its first check (it showed for a moment at start-up)
        btn.face = image; btn.lightSprite = image.sprite;
        rt.SetAsLastSibling();
        Debug.Log("[HDMI] button built under " + parent.name + " size=" + size + " at " + rt.position);
    }

    private void SetLabel()
    {
        shownPort = PREFS.HdmiPort.GetInt();
        label.text = "HDMI\n<size=70%>" + shownPort + "</size>";
    }

    // Glossy round button: cyan ring, soft grey face, white highlight. Mirrors the other bottom-bar buttons.
    // dark: the Glass 2026 look, a dark glass face with a brighter ring
    private static Sprite MakeButtonSprite(bool dark)
    {
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var ring = dark ? new Color(0.32f, 0.82f, 1f) : new Color(0.10f, 0.62f, 0.86f);
        var shadow = dark ? new Color(0.16f, 0.23f, 0.35f) : new Color(0.70f, 0.72f, 0.77f);
        float c = (N - 1) / 2f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = x - c, dy = y - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float v = y / (float) N; // 0 bottom .. 1 top

            // outer soft shadow disc, cyan ring, face
            float aShadow = Mathf.Clamp01(c - r + 0.5f);
            float aRing = Mathf.Clamp01((c - 14f) - r + 0.5f);
            float aFace = Mathf.Clamp01((c - 22f) - r + 0.5f);

            Color px = shadow;
            px = Color.Lerp(px, ring, aRing);
            Color face = dark
                ? Color.Lerp(new Color(0.09f, 0.14f, 0.24f), new Color(0.24f, 0.33f, 0.48f), Mathf.SmoothStep(0.1f, 0.95f, v))
                : Color.Lerp(new Color(0.72f, 0.74f, 0.79f), new Color(0.97f, 0.97f, 0.98f), Mathf.SmoothStep(0.1f, 0.95f, v));
            // glossy highlight across the upper half
            float gloss = Mathf.Clamp01(1f - Mathf.Abs(dx) / (c * 0.78f)) * Mathf.Clamp01((dy - c * 0.08f) / (c * 0.62f)) * Mathf.Clamp01((c - 28f - r) / 12f + 0.5f);
            face = Color.Lerp(face, Color.white, gloss * (dark ? 0.18f : 0.45f));
            px = Color.Lerp(px, face, aFace);
            px.a = aShadow;
            tex.SetPixel(x, y, px);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (Time.unscaledTime - backAt < 1.5f) { Debug.Log("[HDMI] press ignored: Revo only just came back"); return; }
        string error = TVHdmi.LaunchHdmi();
        if (error != null && !showingError) StartCoroutine(ShowError(error));
    }

    private IEnumerator ShowError(string msg)
    {
        showingError = true;
        label.text = "<size=60%>" + msg.Replace(" ", "\n") + "</size>";
        yield return new WaitForSecondsRealtime(2.5f);
        SetLabel();
        showingError = false;
    }
}
