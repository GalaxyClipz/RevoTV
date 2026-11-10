using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Disc Channel's game list for TVs where the bundled emulator (a Dolphin fork for 32-bit TVs) has no screen that
/// works with a remote. Revo finds the disc images on the TV and on USB drives, lists them here (up / down / OK), and
/// OK starts the game straight in the emulator. Drawn in the Settings screens' style (see TVServerScreen).
/// </summary>
public static class TVGameScreen
{
    private const string Helper = "com.karuzohikari.revotv.RevoTV";

    private static GameObject root;
    private static string package;

    public static bool Open => root != null;

    /// <param name="emulatorPackage">the installed emulator</param>
    public static void Show(string emulatorPackage)
    {
        if (Open) return;
        package = emulatorPackage;
#if UNITY_ANDROID && !UNITY_EDITOR
        using var helper = new AndroidJavaClass(Helper);
        if (!helper.CallStatic<bool>("hasAllFilesAccess"))
        {
            // the one switch only the person can turn on; No skips the list and opens the emulator itself
            PopupController.ShowPopup("popup.games.access", () =>
            {
                PopupController.ClosePopup();
                using var h = new AndroidJavaClass(Helper);
                h.CallStatic<bool>("openAllFilesAccess", AndroidLinker.currentActivity);
            }, () =>
            {
                PopupController.ClosePopup();
                AndroidLinker.LaunchApp(package, false);
            });
            return;
        }
#endif
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        Build(app.overlayCanvas);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    // a game was added or removed from the Channel Server: look again
    public static void Refresh()
    {
        if (!Open) return;
        var r = root.GetComponent<Runner>();
        if (r != null) r.Restart();
    }

    // hidden (not closed) while the controller settings are on screen, so the list's buttons can't be reached under them
    public static void SetHidden(bool hidden) { if (root != null) root.SetActive(!hidden); }

    public static void Close()
    {
        if (!Open) return;
        Object.Destroy(root);
        root = null;
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    private static void Build(RectTransform parent)
    {
        root = new GameObject("TV Game Screen", typeof(RectTransform));
        var rt = (RectTransform) root.transform;
        rt.SetParent(parent, false);
        rt.SetAsFirstSibling();
        TVServerScreen.Stretch(rt);
        float h = parent.rect.height > 1f ? parent.rect.height : 1080f;
        var font = TVServerScreen.SettingsFont();

        var bg = TVServerScreen.Child(rt, "Background").gameObject.AddComponent<Image>();
        bg.color = new Color(0.035f, 0.035f, 0.04f, 1f);
        bg.raycastTarget = true;
        var band = TVServerScreen.Child(rt, "Band");
        band.anchorMin = new Vector2(0f, 0.22f); band.anchorMax = new Vector2(1f, 0.78f);
        var stripes = band.gameObject.AddComponent<RawImage>();
        stripes.texture = TVServerScreen.StripeTex();
        stripes.uvRect = new Rect(0f, 0f, 1f, h * 0.56f / 6f);
        stripes.raycastTarget = false;
        TVServerScreen.Line(rt, 0.78f, h); TVServerScreen.Line(rt, 0.22f, h);

        var tab = TVServerScreen.Child(rt, "Tab");
        tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f);
        tab.pivot = new Vector2(0f, 1f);
        tab.anchoredPosition = new Vector2(h * 0.045f, -h * 0.12f);
        tab.sizeDelta = new Vector2(h * 0.5f, h * 0.105f);
        var tabImg = tab.gameObject.AddComponent<Image>();
        tabImg.sprite = TVServerScreen.TabSprite(); tabImg.type = Image.Type.Sliced; tabImg.raycastTarget = false;
        var title = TVServerScreen.Text(tab, TextController.GetTranslation("games.title"), font, h * 0.05f, new Color(0.2f, 0.2f, 0.22f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        TVServerScreen.Inset((RectTransform) title.transform, h * 0.045f, 0f);

        // the list: a scroll view inside the band, entries stacked one under another
        var view = TVServerScreen.Child(rt, "List");
        view.anchorMin = new Vector2(0.12f, 0.235f); view.anchorMax = new Vector2(0.88f, 0.765f);
        view.offsetMin = view.offsetMax = Vector2.zero;
        view.gameObject.AddComponent<RectMask2D>();
        var content = TVServerScreen.Child(view, "Content");
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = h * 0.018f;
        layout.padding = new RectOffset((int) (h * 0.01f), (int) (h * 0.01f), (int) (h * 0.012f), (int) (h * 0.012f));
        layout.childControlWidth = true; layout.childControlHeight = false;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = h * 0.05f;

        // the status line in the middle of the band (searching / nothing found)
        var status = TVServerScreen.Text(rt, TextController.GetTranslation("games.scanning"), font, h * 0.04f, new Color(0.86f, 0.88f, 0.9f));
        var srt = (RectTransform) status.transform;
        srt.anchorMin = new Vector2(0.1f, 0.3f); srt.anchorMax = new Vector2(0.9f, 0.7f);
        srt.offsetMin = srt.offsetMax = Vector2.zero;

        // Back (bottom left) and the emulator's own screen, for anything this list doesn't do
        Pill(rt, "← Back", font, h, 0.065f, Close);
        Pill(rt, TextController.GetTranslation("games.emulator"), font, h, 0.065f + 0.44f, () => { Close(); AndroidLinker.LaunchApp(package, false); });
        Pill(rt, "Controllers", font, h, 0.065f + 0.88f, TVControllers.Show); // the remote and game controllers: set up for the games
        var hint = TVServerScreen.Text(rt, TextController.GetTranslation("games.hint"), font, h * 0.026f, new Color(0.55f, 0.57f, 0.6f));
        var hrt = (RectTransform) hint.transform;
        hrt.anchorMin = new Vector2(0.8f, 0f); hrt.anchorMax = new Vector2(0.95f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, h * 0.085f);
        hrt.sizeDelta = new Vector2(0f, h * 0.05f);
        hint.alignment = TextAlignmentOptions.MidlineRight;

        var runner = root.AddComponent<Runner>();
        runner.content = content; runner.status = status; runner.font = font; runner.h = h; runner.scroll = scroll;
    }

    private static void Pill(Transform parent, string label, TMP_FontAsset font, float h, float xFrac, UnityEngine.Events.UnityAction click)
    {
        var rt = TVServerScreen.Child(parent, "Pill " + label);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(h * xFrac, h * 0.06f);
        rt.sizeDelta = new Vector2(h * 0.4f, h * 0.11f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = TVServerScreen.PillSprite(); img.type = Image.Type.Sliced;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(click);
        var t = TVServerScreen.Text(rt, label, font, h * 0.05f, new Color(0.15f, 0.15f, 0.17f));
        t.enableAutoSizing = true; t.fontSizeMin = h * 0.03f; t.fontSizeMax = h * 0.05f;
    }

    // waits for the scan, then fills the list
    private class Runner : MonoBehaviour
    {
        public RectTransform content;
        public TextMeshProUGUI status;
        public TMP_FontAsset font;
        public float h;
        public ScrollRect scroll;
        private bool started, filled;

        public void Restart()
        {
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            status.text = TextController.GetTranslation("games.scanning"); status.gameObject.SetActive(true);
            started = filled = false;
        }

        private void Update()
        {
            if (filled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            using var helper = new AndroidJavaClass(Helper);
            if (!started) { started = true; helper.CallStatic("startGameScan"); return; }
            if (!helper.CallStatic<bool>("gameScanDone")) return;
            string[] paths = helper.CallStatic<string[]>("gameScanResult");
#else
            string[] paths = new string[0];
#endif
            filled = true;
            // the Channel Server's Games folder too, when it's one the scan can't see (the app's own folder)
            var all = new System.Collections.Generic.List<string>(paths ?? new string[0]);
            try { if (Directory.Exists(TVGames.Folder)) foreach (var f in Directory.GetFiles(TVGames.Folder)) if (TVGames.IsGame(f) && !all.Contains(f)) all.Add(f); } catch (System.Exception) { }
            paths = all.ToArray();
            if (paths == null || paths.Length == 0)
            {
                status.text = TextController.GetTranslation("games.none");
                return;
            }
            status.gameObject.SetActive(false);
            foreach (var p in paths) Row(p);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private void Row(string path)
        {
            var info = TVGames.Read(path);
            float rowH = h * 0.13f;
            var rt = TVServerScreen.Child(content, "Game");
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(0f, rowH);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = rowH;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = TVServerScreen.PillSprite(); img.type = Image.Type.Sliced;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            string chosen = path;
            b.onClick.AddListener(() =>
            {
                Debug.Log("[Games] starting " + chosen);
#if UNITY_ANDROID && !UNITY_EDITOR
                using var helper = new AndroidJavaClass(Helper);
                string result = helper.CallStatic<string>("launchGame", AndroidLinker.currentActivity, package, chosen);
                if (result != "ok") Debug.LogWarning("[Games] " + result);
#endif
            });
            // a round console badge on the left, like a disc in the drive
            bool gc = info.console == "GameCube", wii = info.console == "Wii";
            var badge = TVServerScreen.Child(rt, "Badge");
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f); badge.pivot = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(h * 0.025f, 0f); badge.sizeDelta = Vector2.one * rowH * 0.74f;
            var bi = badge.gameObject.AddComponent<Image>(); bi.sprite = TVServerScreen.DotSprite(); bi.raycastTarget = false;
            bi.color = gc ? new Color(0.42f, 0.33f, 0.75f) : wii ? new Color(0.1f, 0.62f, 0.88f) : new Color(0.45f, 0.47f, 0.52f);
            var hole = TVServerScreen.Child(badge, "Hole");
            hole.anchorMin = hole.anchorMax = new Vector2(0.5f, 0.5f); hole.sizeDelta = Vector2.one * rowH * 0.16f;
            var hi = hole.gameObject.AddComponent<Image>(); hi.sprite = TVServerScreen.DotSprite(); hi.raycastTarget = false; hi.color = new Color(1f, 1f, 1f, 0.85f);
            var bt = TVServerScreen.Text(badge, gc ? "GC" : wii ? "Wii" : "?", font, rowH * 0.2f, Color.white);
            bt.fontStyle = FontStyles.Bold; bt.alignment = TextAlignmentOptions.Bottom; TVServerScreen.Inset((RectTransform) bt.transform, 0f, rowH * 0.06f);
            // the title (from the disc itself when it could be read) and a line about it
            float left = h * 0.05f + rowH * 0.74f;
            var t = TVServerScreen.Text(rt, info.title, font, h * 0.042f, new Color(0.13f, 0.13f, 0.15f));
            t.alignment = TextAlignmentOptions.BottomLeft; t.fontStyle = FontStyles.Bold;
            t.overflowMode = TextOverflowModes.Ellipsis; t.enableWordWrapping = false;
            var trt = (RectTransform) t.transform; trt.anchorMin = new Vector2(0f, 0.48f); trt.anchorMax = new Vector2(1f, 0.95f); trt.offsetMin = new Vector2(left, 0f); trt.offsetMax = new Vector2(-h * 0.03f, 0f);
            string sub = info.console + (string.IsNullOrEmpty(info.id) ? "" : "  ·  " + info.id) + (string.IsNullOrEmpty(info.size) ? "" : "  ·  " + info.size) + "  ·  on " + info.where
                         + (info.recognized ? "" : "  ·  " + Path.GetFileName(path));
            var st = TVServerScreen.Text(rt, sub, font, h * 0.027f, new Color(0.38f, 0.4f, 0.44f));
            st.alignment = TextAlignmentOptions.TopLeft; st.overflowMode = TextOverflowModes.Ellipsis; st.enableWordWrapping = false;
            var srt = (RectTransform) st.transform; srt.anchorMin = new Vector2(0f, 0.08f); srt.anchorMax = new Vector2(1f, 0.46f); srt.offsetMin = new Vector2(left, 0f); srt.offsetMax = new Vector2(-h * 0.03f, 0f);
        }
    }
}
