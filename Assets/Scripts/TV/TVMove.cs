using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Moving a channel with the remote: hold OK on a tile, pick Move, walk the light to any slot (on any page) and press
/// OK. A slot that already has a channel swaps places with it; Back cancels. The prefab's own Move button (unused on
/// phones, which drag tiles instead) is the one shown, between Edit and Delete.
/// </summary>
public static class TVMove
{
    public static Channel Moving { get; private set; }
    public static bool Active => Moving != null;
    private static int endedFrame = -1;
    private static GameObject hint;

    // Back pressed to cancel a move must not also do what Back does on the home menu (jump to the first page)
    public static bool ConsumesBack => Active || endedFrame == Time.frameCount;

    public static void AddButton(AppHandler h)
    {
        if (!TVMode.IsTV || h.options == null) return;
        var buttons = h.options.transform.Find("Buttons");
        var edit = buttons != null ? buttons.Find("Edit") as RectTransform : null;
        var delete = buttons != null ? buttons.Find("Delete") as RectTransform : null;
        if (edit == null || delete == null || buttons.Find("TV Move") != null) return;

        // a copy of Edit, so it looks and lights up like its neighbours
        var move = Object.Instantiate(edit.gameObject, buttons, false);
        move.name = "TV Move";
        var b = move.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent(); // the copy's own listener is Edit's
        b.onClick.AddListener(() => { h.ToggleOptions(); Begin(h.channel); });
        foreach (var tt in move.GetComponentsInChildren<TranslatedText>(true)) Object.DestroyImmediate(tt);
        var label = move.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Move";
        var icon = move.transform.Find("Elements/Image");
        if (icon != null) icon.gameObject.SetActive(false);

        // three in a column where two were: same size, same middle, closer together
        var mrt = (RectTransform) move.transform;
        float top = edit.anchoredPosition.y, bottom = delete.anchoredPosition.y, mid = (top + bottom) * 0.5f;
        float step = edit.sizeDelta.y * 1.5f;
        // the menu's own panel (and its shadow) grows with the third button, so nothing is squeezed against its edge
        float extra = 2f * step - Mathf.Abs(top - bottom);
        if (extra > 0f)
        {
            var root = (RectTransform) h.options.transform;
            var panels = new System.Collections.Generic.List<RectTransform>();
            if (root.GetComponent<Image>() != null) panels.Add(root);
            foreach (RectTransform c in root) if (c != buttons && c.GetComponent<Image>() != null) panels.Add(c);
            foreach (var p in panels)
            {
                if (Mathf.Approximately(p.anchorMin.y, p.anchorMax.y)) p.sizeDelta += new Vector2(0f, extra);
                else { p.offsetMin -= new Vector2(0f, extra * 0.5f); p.offsetMax += new Vector2(0f, extra * 0.5f); }
            }
        }
        edit.anchoredPosition = new Vector2(edit.anchoredPosition.x, mid + step);
        mrt.anchoredPosition = new Vector2(edit.anchoredPosition.x, mid);
        delete.anchoredPosition = new Vector2(delete.anchoredPosition.x, mid - step);
        move.transform.SetSiblingIndex(edit.GetSiblingIndex() + 1);
        // slimmer buttons, so the three sit apart inside the tile instead of touching
        edit.localScale = mrt.localScale = delete.localScale = new Vector3(0.9f, 0.9f, 1f);
    }

    public static void Begin(Channel c)
    {
        if (c == null || c.appHandler == null) return;
        Moving = c;
        c.Cover(0.55f, 0.15f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
        ShowHint("Moving \"" + Trim(c.GetTag()) + "\": pick a slot and press OK.  Back cancels.");
        // the light was on the small Move button near the tile's top: from there the first arrow press could jump up a row
        TVNav.FocusTile(c.appHandler.transform);
        Debug.Log("[Move] picked page " + c.gridNumber + " slot " + c.position);
    }

    /// <returns>true when a move was waiting for this slot (the press is used up)</returns>
    public static bool TryDrop(int grid, int pos)
    {
        if (!Active) return false;
        var c = Moving;
        End();
        if (c.gridNumber == grid && c.position == pos) { if (c.appHandler != null) c.Uncover(0.15f); return true; }
        string error = TVTiles.Move(c.gridNumber, c.position, grid, pos);
        if (error != null)
        {
            Debug.Log("[Move] " + error);
            if (c.appHandler != null) c.Uncover(0.15f);
            return true;
        }
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
        if (c.appHandler != null) TVNav.ReturnTo(c.appHandler.transform); // the light stays on the channel just placed
        return true;
    }

    public static void Cancel()
    {
        if (!Active) return;
        var c = Moving;
        End();
        if (c.appHandler != null) c.Uncover(0.15f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
    }

    private static void End()
    {
        Moving = null;
        endedFrame = Time.frameCount;
        if (hint != null) hint.SetActive(false);
    }

    private static string Trim(string s) => string.IsNullOrEmpty(s) ? "Channel" : s.Length > 28 ? s.Substring(0, 28) + "..." : s;

    // a small dark note at the top of the screen while a channel is being moved
    private static void ShowHint(string text)
    {
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        if (hint == null)
        {
            hint = new GameObject("TV Move Hint", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform) hint.transform;
            rt.SetParent(app.overlayCanvas, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -18f);
            var bg = hint.GetComponent<Image>(); bg.color = new Color(0.04f, 0.08f, 0.14f, 0.82f); bg.raycastTarget = false;
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform) tgo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(24f, 8f); trt.offsetMax = new Vector2(-24f, -8f);
            var t = tgo.GetComponent<TextMeshProUGUI>();
            t.fontSize = 30f; t.color = Color.white; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.enableWordWrapping = false;
        }
        var label = hint.GetComponentInChildren<TextMeshProUGUI>();
        label.text = text;
        label.ForceMeshUpdate();
        ((RectTransform) hint.transform).sizeDelta = new Vector2(label.preferredWidth + 48f, label.preferredHeight + 16f);
        hint.transform.SetAsLastSibling();
        hint.SetActive(true);
    }
}
