using UnityEngine;

/// <summary>
/// Diagnostics while a page change is in progress (GridController.ProbeUntil): any slow frame is broken down into
/// scripts (first Update to last LateUpdate), UI rebuild (layout + graphics), garbage collections, and the rest
/// (drawing, batching, waiting for the GPU). One log line per page change.
/// </summary>
[DefaultExecutionOrder(-32000)]
public class TVFrameProbe : MonoBehaviour
{
    public static float ProbeUntil, ProbeStart;
    public static string Report = "";
    private static readonly System.Diagnostics.Stopwatch frame = new System.Diagnostics.Stopwatch(), ui = new System.Diagnostics.Stopwatch();
    private static double scriptsMs, uiMs, updEnd, lateStart, updMs, coMs, lateMs;
    private static int gcAtStart;
    private static bool hooked;
    // named pieces of work timed during the frame (Begin / End): a slow frame lists the ones over 5 ms
    private static readonly System.Diagnostics.Stopwatch piece = new System.Diagnostics.Stopwatch();
    private static string pieces = "", lastPieces = "";
    public static long Begin() { if (!piece.IsRunning) piece.Start(); return piece.ElapsedTicks; }
    public static void End(string name, long from)
    {
        double ms = (piece.ElapsedTicks - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ms > 5.0 && Time.unscaledTime < ProbeUntil + 0.3f) pieces += " " + name + "=" + ms.ToString("0");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVFrameProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<TVFrameProbe>();
        go.AddComponent<Late>();
        go.AddComponent<EarlyLate>();
    }

    private void Update()
    {
        if (!hooked)
        {
            hooked = true;
            Canvas.preWillRenderCanvases += () => ui.Restart();
            Canvas.willRenderCanvases += () => { ui.Stop(); uiMs = ui.Elapsed.TotalMilliseconds; }; // after the UI's own rebuild (it subscribed first)
        }
        // the frame that just ended
        float dt = Time.unscaledDeltaTime;
        if (Time.unscaledTime < ProbeUntil + 0.3f && dt > 0.09f && ProbeStart > 0f)
        {
            int gcs = System.GC.CollectionCount(0) - gcAtStart;
            Report += $" | {Mathf.RoundToInt(dt * 1000)}ms@+{Mathf.RoundToInt((Time.unscaledTime - ProbeStart) * 1000)} scripts={scriptsMs:0} (upd={updMs:0} co={coMs:0} late={lateMs:0}) ui={uiMs:0} gc={gcs} [{pieces.Trim()}]";
        }
        pieces = "";
        gcAtStart = System.GC.CollectionCount(0);
        frame.Restart();
    }

    [DefaultExecutionOrder(32000)]
    private class Late : MonoBehaviour
    {
        // end of the Update phase (coroutines resumed with "yield return null" run after it, then LateUpdate)
        private void Update() { updEnd = frame.Elapsed.TotalMilliseconds; }
        private void LateUpdate()
        {
            scriptsMs = frame.Elapsed.TotalMilliseconds;
            updMs = updEnd; coMs = lateStart - updEnd; lateMs = scriptsMs - lateStart;
        }
    }

    [DefaultExecutionOrder(-32000)]
    private class EarlyLate : MonoBehaviour
    {
        private void LateUpdate() { lateStart = frame.Elapsed.TotalMilliseconds; }
    }
}
