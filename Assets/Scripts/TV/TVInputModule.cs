using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// TV: the pointer is invisible and only moves when the remote's light moves, yet Unity's input module ray-cast it against every
/// one of the menu's ~1000 pictures on every frame to find what it hovers (7% of the main thread, measured 2026-11-07).
/// While the pointer stands still and no button is down, that full check runs 5 times a second instead of every frame:
/// a move, a press, a release or a scroll is still handled on the very frame it happens, and keys/navigation every frame.
/// </summary>
public class TVInputModule : StandaloneInputModule
{
    private Vector2 lastPos = new Vector2(-1f, -1f);
    private float nextFull;
    public static bool Off; // test switch: --ez fullinput true

    public override void Process()
    {
        if (Off) { base.Process(); return; }
        var inp = input;
        Vector2 p = inp.mousePosition;
        bool buttons = inp.GetMouseButton(0) || inp.GetMouseButton(1) || inp.GetMouseButton(2)
                       || inp.GetMouseButtonDown(0) || inp.GetMouseButtonDown(1) || inp.GetMouseButtonDown(2)
                       || inp.GetMouseButtonUp(0) || inp.GetMouseButtonUp(1) || inp.GetMouseButtonUp(2);
        bool scroll = inp.mouseScrollDelta.sqrMagnitude > 0f;
        if (buttons || scroll || (p - lastPos).sqrMagnitude > 0.25f || Time.unscaledTime >= nextFull || inp.touchCount > 0)
        {
            lastPos = p;
            nextFull = Time.unscaledTime + 0.2f;
            base.Process();
            return;
        }
        // the pointer is standing still: only what the keys do (the selected object, move and submit), as the base module does
        if (!eventSystem.isFocused) return;
        bool used = SendUpdateEventToSelectedObject();
        if (eventSystem.sendNavigationEvents)
        {
            if (!used) used |= SendMoveEventToSelectedObject();
            if (!used) SendSubmitEventToSelectedObject();
        }
    }

    // swaps the scene's StandaloneInputModule for this one, keeping its settings and the TV's input override
    public static void Install(StandaloneInputModule old, BaseInput inputOverride)
    {
        if (old == null || old is TVInputModule) return;
        var go = old.gameObject;
        if (go.GetComponent<TVInputModule>() != null) { old.enabled = false; Destroy(old); return; } // already swapped
        var axisH = old.horizontalAxis; var axisV = old.verticalAxis; var submit = old.submitButton; var cancel = old.cancelButton;
        float rate = old.inputActionsPerSecond, delay = old.repeatDelay;
        old.enabled = false;
        Destroy(old);
        var m = go.AddComponent<TVInputModule>();
        m.horizontalAxis = axisH; m.verticalAxis = axisV; m.submitButton = submit; m.cancelButton = cancel;
        m.inputActionsPerSecond = rate; m.repeatDelay = delay;
        m.inputOverride = inputOverride;
        Debug.Log("[PERF] TV input module installed");
    }
}
