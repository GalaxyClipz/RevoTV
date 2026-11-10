using UnityEngine;
using UnityEngine.UI;

/// Glass 2026 clock: leans a digit's pieces forward like the reference's italic digits. Each vertex moves right by
/// its height above the middle of the digit (root), so the whole digit slants as one.
public class Slant : BaseMeshEffect
{
    public RectTransform root;
    public float k = 0.10f; // sideways per unit of height (measured on the reference: ~6 degrees)

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || root == null) return;
        var rt = (RectTransform) transform;
        float toLocal = root.lossyScale.x / Mathf.Max(0.0001f, rt.lossyScale.x);
        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float y = root.InverseTransformPoint(rt.TransformPoint(v.position)).y - root.rect.center.y;
            v.position.x += y * k * toLocal;
            vh.SetUIVertex(v, i);
        }
    }
}
