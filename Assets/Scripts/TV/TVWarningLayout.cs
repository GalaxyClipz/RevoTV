using System.Collections;
using UnityEngine;

/// <summary>
/// The health & safety screen is authored as a 1080 wide portrait column, which wraps the text into a narrow strip on a landscape TV.
/// Widen the text column once so the lines read like the original Wii screen.
/// </summary>
public class TVWarningLayout : MonoBehaviour
{
    private const float Width = 1700f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        new GameObject("TVWarningLayout").AddComponent<TVWarningLayout>();
    }

    private IEnumerator Start()
    {
        while (AppController._instance == null || AppController._instance.warningView == null) yield return null;
        var view = AppController._instance.warningView.transform;
        var objects = view.Find("Objects") as RectTransform;
        if (objects == null) { Destroy(gameObject); yield break; }

        Fit(objects);
        // text is rewritten by translations/themes after boot, so apply a few times
        for (int i = 0; i < 8; i++)
        {
            yield return new WaitForSeconds(0.5f);
            Fit(objects);
        }
        Destroy(gameObject);
    }

    private static void Fit(RectTransform objects)
    {
        objects.localScale = Vector3.one * 1.1f;
        objects.sizeDelta = new Vector2(Width, objects.sizeDelta.y);
        foreach (RectTransform child in objects)
        {
            // everything is top-left anchored with its centre at x = old/2: re-centre in the wider panel
            var pos = child.anchoredPosition;
            pos.x = Width * 0.5f;
            child.anchoredPosition = pos;
            if (child.name == "Warning Description" || child.name == "Warning Link")
                child.sizeDelta = new Vector2(Width - 120f, child.sizeDelta.y);
        }
    }
}
