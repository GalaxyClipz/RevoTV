using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Content Hub / theme store screens are authored as a 1080x1920 portrait page (top bar + stacked views + bottom bar).
/// Stretched over a landscape screen they overlap and get cut off. Instead we keep them at their authored proportions,
/// scaled to the screen height and centred (the same way channel previews are shown), so every view, list and
/// theme/animation preview looks exactly as designed.
/// </summary>
[DefaultExecutionOrder(32000)]
public class TVShopFit : MonoBehaviour
{
    private const float DesignWidth = 1080f;
    private const float DesignHeight = 1920f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVShopFit");
        DontDestroyOnLoad(go);
        go.AddComponent<TVShopFit>();
    }

    private CanvasGroup veil;
    private bool wasOpen;
    private float revealAt;

    private void LateUpdate()
    {
        var shop = ShopController._instance;
        if (shop == null || shop.shopMenu == null) return;
        if (veil == null)
        {
            // its own group on the hub page (a group Revo already uses is left alone: then on the content only)
            var host = shop.shopMenu.GetComponent<CanvasGroup>() == null ? shop.shopMenu : (shop.shopMenu.transform.Find("Content") != null ? shop.shopMenu.transform.Find("Content").gameObject : null);
            if (host != null && host.GetComponent<CanvasGroup>() == null) veil = host.AddComponent<CanvasGroup>();
            if (veil != null) veil.alpha = shop.shopMenu.activeInHierarchy ? 1f : 0f;
        }
        bool open = shop.shopMenu.activeInHierarchy;
        if (!open) { if (veil != null) veil.alpha = 0f; wasOpen = false; return; }
        if (!wasOpen) { wasOpen = true; revealAt = Time.unscaledTime + 0.1f; } // laid out below this frame, shown once its lists have settled
        if (veil != null) veil.alpha = Time.unscaledTime < revealAt ? 0f : Mathf.MoveTowards(veil.alpha, 1f, Time.unscaledDeltaTime / 0.15f);
        if (CameraController.IsPortrait()) return;
        var content = shop.shopMenu.transform.Find("Content") as RectTransform;
        if (content == null) return;
        var parent = content.parent as RectTransform;
        float scale = parent.rect.height / DesignHeight;

        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.pivot = new Vector2(0.5f, 0.5f);
        content.sizeDelta = new Vector2(DesignWidth, DesignHeight);
        content.anchoredPosition = Vector2.zero;
        content.localScale = new Vector3(scale, scale, 1f);

        // the portrait page leaves two wide empty strips on a TV: classic fills them with remote hints (Glass 2026 stays as it was)
        if (!TVGlass.On) UpdateSides(shop.shopMenu.transform as RectTransform, DesignWidth * scale * 0.5f + parent.rect.height * 0.18f, parent.rect.height); // starts clear of the page's wide buttons
    }

    private RectTransform left, right;

    private void UpdateSides(RectTransform menu, float halfContent, float h)
    {
        if (menu == null) return;
        if (left == null || right == null)
        {
            left = MakeSide(menu, "TV Hub Left", "Remote", new[] { "▲ ▼ ◄ ►     Move", "OK     Choose", "Back     Go back" }, true, h);
            right = MakeSide(menu, "TV Hub Right", "Content Hub", new[] { "Download animations and themes made by other people.", "Prefer your phone? Settings > App Settings > Edit from phone or computer can open this same hub." }, false, h);
        }
        left.anchorMin = new Vector2(0f, 0f); left.anchorMax = new Vector2(0.5f, 1f);
        left.offsetMin = Vector2.zero; left.offsetMax = new Vector2(-halfContent, 0f);
        right.anchorMin = new Vector2(0.5f, 0f); right.anchorMax = new Vector2(1f, 1f);
        right.offsetMin = new Vector2(halfContent, 0f); right.offsetMax = Vector2.zero;
    }

    private static RectTransform MakeSide(RectTransform menu, string name, string title, string[] lines, bool isLeft, float h)
    {
        var rt = TVServerScreen.Child(menu, name);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.86f, 0.9f, 0.94f, 1f);
        bg.raycastTarget = false;
        var font = TVServerScreen.SettingsFont();
        var ink = new Color(0.22f, 0.3f, 0.4f, 1f);
        var head = TVServerScreen.Text(rt, title, font, h * 0.05f, new Color(0.1f, 0.6f, 0.78f, 1f));
        head.rectTransform.anchorMin = new Vector2(0.08f, 0.62f); head.rectTransform.anchorMax = new Vector2(0.92f, 0.74f);
        head.rectTransform.offsetMin = head.rectTransform.offsetMax = Vector2.zero;
        var body = TVServerScreen.Text(rt, string.Join("\n\n", lines), font, h * 0.03f, ink);
        body.rectTransform.anchorMin = new Vector2(0.1f, 0.3f); body.rectTransform.anchorMax = new Vector2(0.9f, 0.6f);
        body.rectTransform.offsetMin = body.rectTransform.offsetMax = Vector2.zero;
        body.alignment = TextAlignmentOptions.Top;
        return rt;
    }
}
