using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Content Hub's front page on a TV gets an account button beside Quit: "Sign in" opens the hub's own sign-in
/// screens (Register / Login), and once signed in it shows the name and offers to log out. On the phone build those
/// screens were only reachable through Upload.
/// </summary>
public class TVHubAccount : MonoBehaviour
{
    private GameObject button;
    private TMP_Text[] labels;
    private string shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TV Hub Account");
        DontDestroyOnLoad(go);
        go.AddComponent<TVHubAccount>();
    }

    private static bool SignedIn => !string.IsNullOrEmpty(ShopController.GetUserId()) && !string.IsNullOrEmpty(ShopController.GetUserAuth());

    private void Update()
    {
        var shop = ShopController._instance;
        if (shop == null || shop.backButton == null || shop.shopMainView == null) return;
        bool front = shop.shopMainView.activeInHierarchy && shop.backButton.activeInHierarchy;
        if (front && button == null) Build(shop);
        if (button == null) return;
        if (button.activeSelf != front) button.SetActive(front);
        if (!front) return;

        // mirror Quit across the footer's middle every frame (the hub lays itself out late)
        var quit = (RectTransform) shop.backButton.transform;
        var rt = (RectTransform) button.transform;
        var parent = (RectTransform) quit.parent;
        Vector3 q = quit.position, mid = parent.TransformPoint(parent.rect.center);
        rt.position = new Vector3(2f * mid.x - q.x, q.y, q.z);

        string name = ShopController.GetUserName() ?? "";
        if (name.Length > 10) name = name.Substring(0, 9) + "…";
        string text = SignedIn ? string.Format(TextController.GetTranslation("hub.signedin"), name)
                               : TextController.GetTranslation("hub.signin");
        // set every frame the label differs: the hub re-captions its own Quit button when views change
        if (labels != null) foreach (var l in labels) if (l != null && l.text != text) { l.text = text; shown = text; }
    }

    private void Build(ShopController shop)
    {
        button = Instantiate(shop.backButton, shop.backButton.transform.parent);
        button.name = "TV Account";
        // a copy of Quit: drop its wiring and its translated caption, keep its look
        foreach (var tt in button.GetComponentsInChildren<TranslatedText>(true)) DestroyImmediate(tt);
        var b = button.GetComponentInChildren<Button>(true);
        if (b != null)
        {
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(Clicked);
        }
        labels = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (var l in labels)
        {
            l.enableAutoSizing = true;
            l.fontSizeMin = l.fontSize * 0.45f;
            l.fontSizeMax = l.fontSize;
            l.enableWordWrapping = false;
            l.overflowMode = TextOverflowModes.Ellipsis;
        }
        shown = null;
    }

    private static void Clicked()
    {
        var shop = ShopController._instance;
        if (shop == null) return;
        if (!SignedIn)
        {
            shop.userAuthView.Show();
            return;
        }
        PopupController.ShowPopup("popup.shouldlogout", () =>
        {
            WebRequestController._instance.Logout();
            PopupController.ClosePopup();
        }, PopupController.ClosePopup);
    }
}
