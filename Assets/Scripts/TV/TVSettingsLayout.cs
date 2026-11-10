using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Settings menus were authored for a tall portrait screen: tiles stacked in two rows that, on a landscape TV, run over the
/// title tab and the Back button. When one of those panels opens we put its tiles in one centred row between the top and bottom bars.
/// </summary>
[DefaultExecutionOrder(32000)]
public class TVSettingsLayout : MonoBehaviour
{
    private static readonly string[] Panels = { "MainSettings", "ThemeSettings", "WallpaperSelection", "BannerIconSelection" };

    private float timer, freshUntil;
    private readonly bool[] wasOpen = new bool[Panels.Length];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVSettingsLayout");
        DontDestroyOnLoad(go);
        go.AddComponent<TVSettingsLayout>();
    }

    // LateUpdate, every frame for a moment after a panel opens: a panel must never be drawn with its portrait layout
    // (it used to show stacked for up to 0.12 s, "the phone thing"), then every 0.12 s while it stays open
    private void LateUpdate()
    {
        var sc = SettingsController._instance;
        if (sc == null || sc.settingsView == null || !sc.settingsView.activeInHierarchy)
        {
            for (int i = 0; i < wasOpen.Length; i++) wasOpen[i] = false;
            return;
        }
        var root = sc.settingsView.transform.Find("Content");
        if (root != null)
        {
            for (int i = 0; i < Panels.Length; i++)
            {
                var p = root.Find(Panels[i]);
                bool open = p != null && p.gameObject.activeInHierarchy;
                if (open && !wasOpen[i]) freshUntil = Time.unscaledTime + 0.6f;
                wasOpen[i] = open;
            }
        }
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f && Time.unscaledTime > freshUntil) return;
        timer = 0.12f;

        var content = sc.settingsView.transform.Find("Content") as RectTransform;
        if (content == null) return;
        float top = 250f, bottom = 250f;
        var bars = content.Find("Borders");
        if (bars != null)
        {
            var t = bars.Find("TopBar") as RectTransform;
            var b = bars.Find("BottomBar") as RectTransform;
            if (t != null) top = t.rect.height;
            if (b != null) bottom = b.rect.height;
        }

        foreach (var name in Panels)
        {
            var panel = content.Find(name) as RectTransform;
            if (panel == null)
            {
                continue;
            }
            if (!panel.gameObject.activeInHierarchy) continue;
            // keep applying while the panel is open: the settings code repositions things itself when it opens
            Apply(panel, top, bottom);
        }
    }

    private static bool IsTile(RectTransform t)
    {
        return t.GetComponent<Button>() != null || t.GetComponent<WiiButton>() != null;
    }

    private static void Apply(RectTransform panel, float top, float bottom)
    {
        // these containers may be driven by layout groups that would undo our positions
        foreach (var lg in panel.GetComponentsInChildren<LayoutGroup>(true)) lg.enabled = false;
        foreach (var f in panel.GetComponentsInChildren<ContentSizeFitter>(true)) f.enabled = false;

        // fill the space between the bars
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = new Vector2(0f, bottom);
        panel.offsetMax = new Vector2(0f, -top);

        // gather tiles: direct buttons, or buttons inside the *Buttons containers
        var tiles = new List<RectTransform>();
        foreach (RectTransform child in panel)
        {
            if (IsTile(child))
            {
                tiles.Add(child);
            }
            else if (child.name.EndsWith("Buttons"))
            {
                child.anchorMin = child.anchorMax = child.pivot = new Vector2(0.5f, 0.5f);
                child.anchoredPosition = Vector2.zero;
                foreach (RectTransform inner in child)
                {
                    if (IsTile(inner)) tiles.Add(inner);
                }
            }
        }
        if (tiles.Count == 0) return;

        // shrink the row to fit the screen (once: the tiles also grow on hover, so don't keep fighting their scale)
        float widest = 0f;
        foreach (var t in tiles)
        {
            var mk = t.GetComponent<Fitted>();
            float baseScale = mk != null ? mk.original : t.localScale.x;
            widest = Mathf.Max(widest, t.rect.width * baseScale);
        }
        const float gap = 30f;
        float fit = Mathf.Min(1f, panel.rect.width * 0.88f / (tiles.Count * widest + (tiles.Count - 1) * gap));
        foreach (var t in tiles)
        {
            var mk = t.GetComponent<Fitted>();
            if (mk == null)
            {
                mk = t.gameObject.AddComponent<Fitted>();
                mk.original = t.localScale.x;
                t.localScale = t.localScale * fit;
            }
        }
        float spacing = widest * fit + gap;
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(0.5f, 0.5f);
            t.anchoredPosition = new Vector2((i - (tiles.Count - 1) * 0.5f) * spacing, 0f);
        }
    }

    private class Fitted : MonoBehaviour { public float original; }
}
