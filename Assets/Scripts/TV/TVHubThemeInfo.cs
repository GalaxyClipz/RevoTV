using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A theme's page in the Content Hub lists "Replaces colors / textures / audio" as little tick and cross boxes. They were
/// plain pictures, so the remote could never reach them. On a TV each box becomes a button that says what it means.
/// </summary>
public class TVHubThemeInfo : MonoBehaviour
{
    private float wait;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TV Hub Theme Info");
        DontDestroyOnLoad(go);
        go.AddComponent<TVHubThemeInfo>();
    }

    private void Update()
    {
        wait -= Time.unscaledDeltaTime;
        if (wait > 0f) return;
        wait = 0.4f;
        var s = ShopController._instance;
        if (s == null) return;
        Wire(s.themePreviewColorsYes, "hub.theme.colors", true);
        Wire(s.themePreviewColorsNo, "hub.theme.colors", false);
        Wire(s.themePreviewTexturesYes, "hub.theme.textures", true);
        Wire(s.themePreviewTexturesNo, "hub.theme.textures", false);
        Wire(s.themePreviewAudioYes, "hub.theme.audio", true);
        Wire(s.themePreviewAudioNo, "hub.theme.audio", false);
    }

    private static void Wire(GameObject go, string key, bool yes)
    {
        if (go == null || !go.activeInHierarchy || go.GetComponent<Button>() != null) return;
        var img = go.GetComponent<Graphic>() ?? go.GetComponentInChildren<Graphic>();
        if (img == null) return;
        img.raycastTarget = true;
        var b = go.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => PopupController.ShowPopup("hub.theme.info", true, new[] { TextController.GetTranslation(key + (yes ? ".yes" : ".no")) }));
    }
}
