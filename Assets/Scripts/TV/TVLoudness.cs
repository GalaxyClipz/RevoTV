using UnityEngine;

/// <summary>
/// On a TV / projector speaker Revo sounded far too quiet even at 100 %. Everything Revo plays is made a little louder
/// (x1.6, about +4 dB) with a soft limit, so loud parts round off instead of crackling. One multiply per sample on the
/// audio thread, on the scene's listener.
/// </summary>
public class TVLoudness : MonoBehaviour
{
    public const float Gain = 1.6f;
    private const float Knee = 0.7f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var l = Object.FindFirstObjectByType<AudioListener>();
        if (l == null) { Debug.LogWarning("[Audio] no listener: loudness not added"); return; }
        if (l.GetComponent<TVLoudness>() == null) l.gameObject.AddComponent<TVLoudness>();
        Debug.Log("[Audio] TV loudness x" + Gain);
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++)
        {
            float x = data[i] * Gain;
            float a = x < 0f ? -x : x;
            if (a > Knee)
            {
                float over = (a - Knee) / (1f - Knee);
                a = Knee + (1f - Knee) * over / (1f + over); // never past 1
                x = x < 0f ? -a : a;
            }
            data[i] = x;
        }
    }
}
