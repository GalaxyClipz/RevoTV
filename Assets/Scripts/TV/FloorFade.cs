using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Glass 2026 floor reflections: fades a mirrored picture out the further it falls below the floor line.
/// A plain quad is cut into thin rows first, so the fade is smooth across it (four corners alone can't fade).
public class FloorFade : BaseMeshEffect
{
    public float axis; // distance from the picture's own origin up to the floor line, in its local units (it's flipped)
    public float length = 60f;
    private const int Rows = 24;
    private static readonly List<UIVertex> quad = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        if (vh.currentVertCount == 4)
        {
            quad.Clear();
            var v = new UIVertex();
            for (int i = 0; i < 4; i++) { vh.PopulateUIVertex(ref v, i); quad.Add(v); } // bottom-left, top-left, top-right, bottom-right
            vh.Clear();
            for (int r = 0; r <= Rows; r++)
            {
                float t = r / (float) Rows;
                vh.AddVert(Fade(Lerp(quad[0], quad[1], t)));
                vh.AddVert(Fade(Lerp(quad[3], quad[2], t)));
                if (r > 0)
                {
                    int a = (r - 1) * 2;
                    vh.AddTriangle(a, a + 2, a + 3);
                    vh.AddTriangle(a + 3, a + 1, a);
                }
            }
            return;
        }
        var u = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref u, i); vh.SetUIVertex(Fade(u), i); }
    }

    private UIVertex Fade(UIVertex v)
    {
        float d = axis + v.position.y; // how far below the floor line this vertex lands once flipped
        float f = 1f - Mathf.Clamp01(d / Mathf.Max(1f, length));
        v.color.a = (byte) (v.color.a * f * f);
        return v;
    }

    private static UIVertex Lerp(UIVertex a, UIVertex b, float t)
    {
        a.position = Vector3.Lerp(a.position, b.position, t);
        a.uv0 = Vector4.Lerp(a.uv0, b.uv0, t);
        a.color = Color32.Lerp(a.color, b.color, t);
        return a;
    }
}
