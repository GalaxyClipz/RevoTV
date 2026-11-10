using UnityEngine;

/// <summary>
/// The project is portrait-first: the channel preview is a fixed 1080x1920 panel.
/// On a landscape TV we widen it to the screen so the bottom bar and background span the whole picture
/// (the banner art itself stays at its authored size, centred).
/// </summary>
public static class TVLayout
{
    private const float PortraitWidth = 1080f;

    public static void FitChannelView(GameObject channelObjects, ArrowController left, ArrowController right)
    {
        if (!TVMode.IsTV || CameraController.IsPortrait()) return;

        var rt = (RectTransform) channelObjects.transform;
        float newWidth = rt.sizeDelta.y * Screen.width / Screen.height; // keep the panel's own height, match the screen's aspect
        float extra = newWidth - PortraitWidth;
        if (extra <= 0f) return;

        rt.sizeDelta = new Vector2(newWidth, rt.sizeDelta.y);

        // full-width pieces that were authored at 1080 wide
        foreach (var child in channelObjects.GetComponentsInChildren<RectTransform>(true))
        {
            if (child.name == "Black Bar")
            {
                child.sizeDelta = new Vector2(newWidth, child.sizeDelta.y);
            }
        }

        // push the arrows out to the screen edges
        Shift(left, -Mathf.Max(0f, extra / 2f - 90f)); // keep the arrows (and their glow) clear of the screen edge
        Shift(right, Mathf.Max(0f, extra / 2f - 90f));
    }

    private static void Shift(ArrowController arrow, float dx)
    {
        if (arrow == null) return;
        var t = (RectTransform) arrow.transform;
        // only once per layout: remember what we did on the component
        var marker = arrow.GetComponent<TVShifted>();
        if (marker == null) marker = arrow.gameObject.AddComponent<TVShifted>();
        t.anchoredPosition += new Vector2(dx - marker.applied, 0f);
        marker.applied = dx;
    }

    // extra horizontal offset the arrow's own show/hide animation has to keep
    public static float ArrowOffset(ArrowController arrow)
    {
        var m = arrow.GetComponent<TVShifted>();
        return m != null ? m.applied : 0f;
    }

    private class TVShifted : MonoBehaviour { public float applied; }
}
