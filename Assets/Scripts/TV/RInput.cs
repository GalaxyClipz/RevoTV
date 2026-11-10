using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Remote-friendly replacement for the mouse parts of UnityEngine.Input.
/// Off TV it just forwards to Input. On TV the D-pad / left stick moves a virtual cursor
/// and OK / A clicks (Menu / X acts as right click). A real air-mouse still takes over when it moves.
/// </summary>
[DefaultExecutionOrder(-32000)] // update before the EventSystem so clicks land on the same frame
public class RInput : MonoBehaviour
{
    private static RInput _instance;

    private Vector2 pos;
    private Vector2 lastRealMouse;
    private float lastKeyTime = -10f;
    private bool left, right;
    private bool leftHeld, rightHeld;     // physical state, tracked from key events
    private bool leftPulse, rightPulse;   // a press+release landed inside one frame: keep it down for a frame
    private int downFrame = -1, upFrame = -1, rDownFrame = -1, rUpFrame = -1;
    private float moduleCheck;

    public static Vector2 mousePosition
    {
        get
        {
            if (!TVMode.IsTV) return Input.mousePosition;
            Ensure();
            return _instance.pos;
        }
    }

    public static bool GetMouseButton(int b)
    {
        if (!TVMode.IsTV) return Input.GetMouseButton(b);
        Ensure();
        return b == 0 ? _instance.left : b == 1 && _instance.right;
    }

    public static bool GetMouseButtonDown(int b)
    {
        if (!TVMode.IsTV) return Input.GetMouseButtonDown(b);
        Ensure();
        return b == 0 ? _instance.downFrame == Time.frameCount : b == 1 && _instance.rDownFrame == Time.frameCount;
    }

    public static bool GetMouseButtonUp(int b)
    {
        if (!TVMode.IsTV) return Input.GetMouseButtonUp(b);
        Ensure();
        return b == 0 ? _instance.upFrame == Time.frameCount : b == 1 && _instance.rUpFrame == Time.frameCount;
    }

    public static int touchCount => TVMode.IsTV ? 0 : Input.touchCount;

    private static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("RInput");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<RInput>();
        _instance.pos = new Vector2(Screen.width / 2f, Screen.height / 2f);
        _instance.lastRealMouse = Input.mousePosition;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (TVMode.IsTV) Ensure();
    }

    // The EventSystem may be created after us, so keep looking until we've hooked it
    private void HookEventSystem()
    {
        var module = EventSystem.current != null ? EventSystem.current.currentInputModule as StandaloneInputModule : null;
        if (module == null && EventSystem.current != null) module = EventSystem.current.GetComponent<StandaloneInputModule>();
        if (module != null && !(module.inputOverride is RBaseInput))
        {
            module.inputOverride = gameObject.GetComponent<RBaseInput>() ?? gameObject.AddComponent<RBaseInput>();
        }
        // the pointer-hover check only runs in full when something changed (TVInputModule)
        if (module != null && !(module is TVInputModule) && !TVInputModule.Off) TVInputModule.Install(module, module.inputOverride);
    }

    public static bool RealMouseMoved { get; private set; }

    /// <summary>Park the virtual pointer somewhere (used by TVNav to sit on the focused element).</summary>
    public static void PlacePointer(Vector2 screenPos)
    {
        Ensure();
        _instance.pos = screenPos;
    }

    /// <summary>Free movement fallback when nothing is focusable.</summary>
    public static void NudgePointer(Vector2 dir)
    {
        Ensure();
        _instance.pos += dir * (Screen.width * 0.05f);
        _instance.pos.x = Mathf.Clamp(_instance.pos.x, 0, Screen.width);
        _instance.pos.y = Mathf.Clamp(_instance.pos.y, 0, Screen.height);
    }

    private bool swallowOk;

    private static bool OkDown() =>
        Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0)
        || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);

    private static bool OkUp() =>
        Input.GetKeyUp(KeyCode.Return) || Input.GetKeyUp(KeyCode.KeypadEnter) || Input.GetKeyUp(KeyCode.JoystickButton0)
        || Input.GetKeyUp(KeyCode.Space) || Input.GetMouseButtonUp(0);

    private static bool MenuDown() =>
        Input.GetKeyDown(KeyCode.Menu) || Input.GetKeyDown(KeyCode.JoystickButton2) || Input.GetKeyDown(KeyCode.JoystickButton3)
        || Input.GetMouseButtonDown(1);

    private static bool MenuUp() =>
        Input.GetKeyUp(KeyCode.Menu) || Input.GetKeyUp(KeyCode.JoystickButton2) || Input.GetKeyUp(KeyCode.JoystickButton3)
        || Input.GetMouseButtonUp(1);

    private void Update()
    {
        if (!TVMode.IsTV) return;

        moduleCheck -= Time.unscaledDeltaTime;
        if (moduleCheck <= 0f) { moduleCheck = 0.5f; HookEventSystem(); }

        float dt = Time.unscaledDeltaTime;

        // real pointer (air mouse / USB mouse / a tap) wins when it moves
        Vector2 real = Input.mousePosition;
        // a remote press can make the system pointer jump when a screen opens: ignore moves right after any key press
        if (Input.anyKeyDown) lastKeyTime = Time.unscaledTime;
        RealMouseMoved = (real - lastRealMouse).sqrMagnitude > 900f && Time.unscaledTime - lastKeyTime > 0.8f; // ignore remote jitter
        if (RealMouseMoved)
        {
            pos = real;
        }
        lastRealMouse = real;

        bool fresh = Time.unscaledTime < PopupController.FreshUntil;
        if (OkDown()) { if (fresh) swallowOk = true; else { TVNav.EnsurePointerOnFocus(); leftHeld = true; leftPulse = true; } }
        if (OkUp()) { if (swallowOk) swallowOk = false; else { leftHeld = false; TVNav.FinishClick(); } }
        if (MenuDown()) { rightHeld = true; rightPulse = true; }
        if (MenuUp()) rightHeld = false;

        bool newLeft = leftHeld || leftPulse;
        bool newRight = rightHeld || rightPulse;
        leftPulse = rightPulse = false;

        if (newLeft && !left) downFrame = Time.frameCount;
        if (!newLeft && left) upFrame = Time.frameCount;
        if (newRight && !right) rDownFrame = Time.frameCount;
        if (!newRight && right) rUpFrame = Time.frameCount;
        left = newLeft;
        right = newRight;
    }
}

/// <summary>Feeds the virtual pointer to Unity's UI event system.</summary>
public class RBaseInput : BaseInput
{
    public override bool mousePresent => true;
    public override Vector2 mousePosition => RInput.mousePosition;
    public override bool GetMouseButton(int button) => RInput.GetMouseButton(button);
    public override bool GetMouseButtonDown(int button) => RInput.GetMouseButtonDown(button);
    public override bool GetMouseButtonUp(int button) => RInput.GetMouseButtonUp(button);
    public override int touchCount => 0;
    // keep the D-pad for the virtual cursor only, no UI focus navigation fighting it
    public override float GetAxisRaw(string axisName) => 0f;
    public override bool GetButtonDown(string buttonName) => false;
}
