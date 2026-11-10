using UnityEngine;
using UnityEngine.UI;

// The Wii shows the edge of the neighbouring channel pages. These frosted slivers hint at them on the home screen.
public static class TVEdgePeek
{
    private static RectTransform root;
    private static CanvasGroup group;
    private static GameObject left, right;
    private static float alpha;
    public static bool Disabled; // the real next page peeks in, so no stand-in is needed

    public static void Tick(float dt)
    {
        if (Disabled) { if (root != null) root.gameObject.SetActive(false); return; }
        var app = AppController._instance;
        var gc = GridController._instance;
        var cc = ChannelController._instance;
        if (app == null || app.overlayCanvas == null || gc == null || gc.currentGrid == null) return;
        if (root == null) Build(app.overlayCanvas);
        if (root == null) return;

        var sc = SettingsController._instance;
        bool home = cc != null && cc.currentChannel == null && !AppController.IsTransitioning()
                    && (sc == null || sc.settingsView == null || !sc.settingsView.activeInHierarchy);
        int n = gc.GetNumberOfGrids();
        int cur = gc.currentGrid.number;
        left.SetActive(cur > 0);
        right.SetActive(cur < n - 1);
        alpha = Mathf.MoveTowards(alpha, home ? 1f : 0f, dt * 4f);
        group.alpha = alpha;
    }

    private static void Build(RectTransform canvas)
    {
        var go = new GameObject("TV Edge Peek", typeof(RectTransform), typeof(CanvasGroup));
        root = (RectTransform) go.transform;
        root.SetParent(canvas, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        root.SetAsFirstSibling();
        group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false; group.alpha = 0f;
        left = Side("Left", true);
        right = Side("Right", false);
    }

    private static GameObject Side(string name, bool isLeft)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform) go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        // three ghost slots, the same rows as the home grid
        float[,] rows = { { 0.60f, 0.80f }, { 0.38f, 0.58f }, { 0.16f, 0.36f } };
        for (int i = 0; i < 3; i++)
        {
            float x0 = isLeft ? -0.05f : 0.978f;
            float x1 = isLeft ? 0.022f : 1.05f;
            Ghost(rt, x0, x1, rows[i, 0], rows[i, 1], new Color(0.62f, 0.76f, 0.9f, 0.16f));
            // bright rim on the inner edge
            float rx = isLeft ? 0.022f : 0.978f;
            Ghost(rt, rx - 0.0012f, rx + 0.0012f, rows[i, 0], rows[i, 1], new Color(0.9f, 0.96f, 1f, 0.45f));
        }
        return go;
    }

    private static void Ghost(RectTransform parent, float x0, float x1, float y0, float y1, Color c)
    {
        var go = new GameObject("Ghost", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var im = go.GetComponent<Image>();
        im.color = c; im.raycastTarget = false;
    }
}
