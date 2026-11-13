using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Console-style remote navigation. The D-pad moves a focus between everything clickable on screen,
/// the focused thing gets a soft breathing blue glow (think Wii disc-slot light) that glides from item to item,
/// and OK clicks it.
/// Focus is applied by parking RInput's virtual pointer on the element, so all the app's normal hover/click logic keeps working.
/// If nothing clickable exists in a direction (sliders, drag areas) the pointer can still be moved freely.
/// </summary>
public class TVNav : MonoBehaviour
{
    public static TVNav _instance;

    // true while a real mouse / air mouse is being used (or nothing is focusable): show the hand pointer instead of the glow
    public static bool PointerMode { get; private set; }

    private struct Candidate
    {
        public GameObject go;
        public Rect screen;      // screen px
        public Vector2 center;
        public Vector2 press;    // where the pointer goes to click it, when that isn't the centre
    }

    private GameObject focus;
    private Vector2 lastFocusCenter;
    private float holdTimer;
    private Vector2 heldDir;
    private float checkTimer;

    // candidate cache: scanning every graphic is the expensive part, so held keys reuse one scan
    private List<Candidate> cache = new List<Candidate>();
    private float cacheTime = -10f;
    private readonly List<Canvas> canvases = new List<Canvas>();
    private float canvasTime = -10f;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly HashSet<GameObject> seen = new HashSet<GameObject>();
    private PointerEventData scratchData;

    private const float RepeatDelay = 0.30f;
    private const float RepeatInterval = 0.085f;
    private const float CacheLife = 0.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVNav");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<TVNav>();
    }

    // ---------------------------------------------------------------- input

    // the health & safety screen is a "press anything" page: no highlight, no pointer
    private static bool OnWarningScreen =>
        AppController._instance != null && AppController._instance.warningView != null && AppController._instance.warningView.activeInHierarchy;

    // safety net: whatever an animation does, home tiles must come back to the grid size (e.g. after a channel preview)
    private float tileCheck;
    private bool wasInSettings;
    private float settleUntil;
    private void EnforceTileSizes()
    {
        tileCheck -= Time.unscaledDeltaTime;
        if (tileCheck > 0f) return;
        tileCheck = Time.unscaledTime < settleUntil ? 0.1f : 1.5f;
        var cc = ChannelController._instance; var gc = GridController._instance;
        if (cc == null || gc == null || cc.currentChannel != null || gc.currentGrid == null || gc.isChangingGrid || AppController.IsTransitioning()) return;
        var pagesRoot = gc.currentGrid.transform.parent != null ? gc.currentGrid.transform.parent : gc.currentGrid.transform;
        // the quick checks right after a page change look at the page on screen only (every page's pictures: ~60 ms on a custom theme)
        var fitRoot = Time.unscaledTime < settleUntil ? gc.currentGrid.transform : pagesRoot;
        foreach (var ah in fitRoot.GetComponentsInChildren<AppHandler>(false)) { TVFx.FitTileFx(ah.transform); TVThemeTiles.Fit(ah.transform); }
        // every page, not only the one on screen: a page left at an old size showed up zoomed in/out when slid to
        var hs = (TVMode.IsTV && !CameraController.IsPortrait() ? pagesRoot : gc.currentGrid.transform).GetComponentsInChildren<ChannelHandler>(false);
        if (hs.Length < 2) return;
        Vector3 want = Vector3.zero;
        foreach (var h in hs) if (!(h is EmptyAppHandler) && h.transform != liftT && h.transform.localScale.x > want.x) want = h.transform.localScale;
        if (want == Vector3.zero) return;
        // the size every tile is meant to have, not the biggest one found (a tile caught mid-animation, like one zooming back from
        // its preview, made every tile on the page too big for a moment). Now in Glass 2026 too.
        if (TVMode.IsTV && !CameraController.IsPortrait())
        {
            float sz = gc.GetChannelSizeMultiplier();
            want = new Vector3(sz * TVMode.TileStretch.x, sz * TVMode.TileStretch.y, 1f);
        }
        // an empty slot's art fills its whole box, a channel's has a margin: the slot is drawn a little smaller so both leave the same gap
        float eg = TVMode.IsTV && !TVGlass.On ? TVTune.Get("empty", 1.0f) : 1f;
        foreach (var h in hs)
        {
            if (h.transform == liftT) continue;
            // a channel's frame is taller and narrower than an empty slot's: drawn to the same footprint, every gap is the same
            var target = h is EmptyAppHandler ? want * eg
                : TVMode.IsTV && !TVGlass.On ? Vector3.Scale(want, TVMode.TileFill) : want;
            if ((h.transform.localScale - target).sqrMagnitude > 0.0004f) h.transform.localScale = target;
        }
        if (liftT != null) liftBase = want;
    }

    // Glass 2026: the focused channel lifts a little and its rim turns cyan (layout never moves: only its own scale)
    private Transform liftT;
    private Vector3 liftBase;
    private float liftK;
    private Graphic liftRim;
    private Color liftRimColor;
    private Image liftShadow;
    private Vector2 liftShadowPos;
    private Color liftShadowColor;

    private void UpdateLift()
    {
        Transform want = null;
        var gc = GridController._instance;
        if (TVGlass.On && focus != null && focus.activeInHierarchy && gc != null && !gc.isChangingGrid && !pageBusy && !AppController.IsTransitioning())
        {
            var ch = focus.GetComponentInParent<ChannelHandler>();
            if (ch != null && (ChannelController._instance == null || ChannelController._instance.currentChannel == null)) want = ch.transform;
        }
        if (want != liftT)
        {
            if (liftT != null) liftT.localScale = liftBase;
            if (liftRim != null) liftRim.color = liftRimColor;
            if (liftShadow != null) { liftShadow.rectTransform.anchoredPosition = liftShadowPos; liftShadow.color = liftShadowColor; }
            liftT = want; liftK = 0f; liftRim = null; liftShadow = null;
            if (want != null)
            {
                liftBase = want.localScale;
                foreach (var g in want.GetComponentsInChildren<Graphic>(false))
                    if (g.name == "Rim") { liftRim = g; liftRimColor = g.color; break; }
                var sh = want.Find("TV Shadow");
                if (sh != null && sh.GetComponent<Image>() != null)
                {
                    liftShadow = sh.GetComponent<Image>(); liftShadowPos = liftShadow.rectTransform.anchoredPosition; liftShadowColor = liftShadow.color;
                }
            }
        }
        if (liftT == null) return;
        liftK = Mathf.MoveTowards(liftK, 1f, Time.unscaledDeltaTime * 14f);
        float e = 1f - (1f - liftK) * (1f - liftK);
        liftT.localScale = liftBase * (1f + 0.085f * e);
        if (liftRim != null) liftRim.color = Color.Lerp(liftRimColor, new Color(0.55f, 0.92f, 1f, 1f), e);
        if (liftShadow != null)
        {
            // the shadow falls further below and darkens as the tile rises
            var r = liftShadow.rectTransform.rect;
            liftShadow.rectTransform.anchoredPosition = liftShadowPos + new Vector2(0f, -r.height * 0.06f * e);
            liftShadow.color = new Color(liftShadowColor.r, liftShadowColor.g, liftShadowColor.b, Mathf.Lerp(liftShadowColor.a, 1f, e));
        }
    }

    // the bar's round buttons (Revo, mail, HDMI): their own round face, whatever box their clickable parent has
    // asked every frame for the focused item (glow shape, 2026 pop): the answer is kept per object (the walk compares names,
    // and each name read makes garbage)
    private static readonly Dictionary<GameObject, RectTransform> roundFaces = new Dictionary<GameObject, RectTransform>();
    private static RectTransform RoundFace(GameObject go)
    {
        if (go == null) return null;
        if (roundFaces.TryGetValue(go, out var known)) return known;
        if (roundFaces.Count > 256) roundFaces.Clear();
        return roundFaces[go] = RoundFaceWalk(go);
    }
    private static RectTransform RoundFaceWalk(GameObject go)
    {
        for (var t = go.transform; t != null; t = t.parent)
        {
            if (t.name == "WiiClip" || t.name == "MailClip") return t.Find(t.name == "MailClip" ? "ButtonHolder/MailButton" : "ButtonHolder/WiiButton") as RectTransform;
            if (t.GetComponent<HdmiButton>() != null) return (RectTransform) t;
            if (t.GetComponent<Canvas>() != null) break;
        }
        return null;
    }
    private static bool IsRoundButton(GameObject go) => RoundFace(go) != null;

    // Glass 2026: the focused round button rises a little toward you, glow ring and all
    private Transform popT;
    private Vector3 popBase;
    private float popK;
    private void UpdatePop()
    {
        Transform want = null;
        if (TVGlass.On && !PointerMode && focus != null && focus.activeInHierarchy && !pageBusy && !AppController.IsTransitioning())
        {
            var f = RoundFace(focus);
            if (f != null) want = f.parent != null && f.parent.name == "ButtonHolder" ? f.parent : f;
        }
        if (want != popT)
        {
            if (popT != null) popT.localScale = popBase;
            popT = want; popK = 0f;
            if (want != null) popBase = want.localScale;
        }
        if (popT == null) return;
        popK = Mathf.MoveTowards(popK, 1f, Time.unscaledDeltaTime * 13f);
        float e = 1f - (1f - popK) * (1f - popK);
        popT.localScale = popBase * (1f + 0.07f * e);
    }

    private bool stuckKeys;
    private float heldFor;
    private void OnApplicationFocus(bool f) { if (f) stuckKeys = true; }

    private void Update()
    {
        if (OnWarningScreen)
        {
            focus = null;
            PointerMode = false;
            RInput.PlacePointer(new Vector2(Screen.width * 0.08f, Screen.height * 0.5f)); // away from the wiki link
            UpdateGlow();
            return;
        }

        // the system pointer never switches to the hand: the remote only ever uses the glow
        Input.backButtonLeavesApp = false;

        Vector2 dir = ReadDirection(out bool pressedNow);
        // a key-up can get lost (system volume bar, focus change): then the pad looks held forever. Require a fresh press.
        if (dir == Vector2.zero || pressedNow) { stuckKeys = false; heldFor = 0f; }
        else
        {
            heldFor += Time.unscaledDeltaTime;
            if (heldFor > 3f) stuckKeys = true;
        }
        if (stuckKeys) dir = Vector2.zero;
        bool fire = false;
        if (dir != Vector2.zero)
        {
            if (pressedNow || dir != heldDir)
            {
                fire = true;
                holdTimer = -RepeatDelay;
            }
            else
            {
                holdTimer += Time.unscaledDeltaTime;
                if (holdTimer >= RepeatInterval) { holdTimer = 0f; fire = true; }
            }
        }
        heldDir = dir;

        if (fire || Input.anyKeyDown) LastUserInput = Time.unscaledTime;
        if (fire)
        {
            autoUntil = 0f;
            PointerMode = false;
            long pm = TVFrameProbe.Begin();
            Move(dir);
            TVFrameProbe.End("move", pm);
        }

        long p0 = TVFrameProbe.Begin();
        HandleBack();
        HomeSteps();
        PopupGuard();
        HandleMenu();
        TVFrameProbe.End("keys", p0);
        // a move in progress ends if the home menu goes away (Settings, a popup, a channel opening)
        if (TVMove.Active && (SettingsOpen() || PopupController.IsPopupOpen()
                              || (ChannelController._instance != null && ChannelController._instance.currentChannel != null))) TVMove.Cancel();
        // a held OK opened a tile's menu (the phone's long press): once OK is let go, the remote moves onto its buttons
        if (!InOptions && focus != null && !pageBusy && !RInput.GetMouseButton(0))
        {
            var tile = TileOf(focus);
            if (tile != null && TileMenu(tile) != null) FocusInTile(tile);
        }
        if (InOptions) menuUntil = Time.unscaledTime + 1.5f;
        // the channel being previewed is where the light goes back to when it closes (not the grid's middle)
        var open = ChannelController._instance != null ? ChannelController._instance.currentChannel : null;
        if (open != null && open.appHandler != null) ReturnTo(open.appHandler.transform);
        // back from Settings: the tiles are put at their size at once, then every moment for a few seconds
        bool settingsNow = SettingsOpen();
        if (wasInSettings && !settingsNow) settleUntil = Time.unscaledTime + 3f;
        wasInSettings = settingsNow;
        if (Time.unscaledTime < settleUntil) tileCheck = Mathf.Min(tileCheck, 0f);
        long pt = TVFrameProbe.Begin();
        CheckWatchedClick(); TVFrameProbe.End("watch", pt); pt = TVFrameProbe.Begin();
        EnforceTileSizes(); TVFrameProbe.End("tilesizes", pt); pt = TVFrameProbe.Begin();
        TVEdgePeek.Tick(Time.unscaledDeltaTime); TVFrameProbe.End("edgepeek", pt); pt = TVFrameProbe.Begin();
        TVScreenFx.Tick(Time.unscaledDeltaTime); TVFrameProbe.End("screenfx", pt); pt = TVFrameProbe.Begin();

        staleTimer -= Time.unscaledDeltaTime;
        if (staleTimer <= 0f) { staleTimer = 0.4f; ReleaseStaleHover(); }
        TVFrameProbe.End("stalehover", pt); pt = TVFrameProbe.Begin();
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = 0.25f;
            KeepFocusValid();
        }
        TVFrameProbe.End("keepfocus", pt); pt = TVFrameProbe.Begin();
        UpdateLift(); TVFrameProbe.End("lift", pt); pt = TVFrameProbe.Begin();
        UpdatePop();
        UpdateGlow(); TVFrameProbe.End("glow", pt);
    }

    private static Vector2 ReadDirection(out bool pressedNow)
    {
        pressedNow = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
                     || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow);
        Vector2 d = Vector2.zero;
        if (Input.GetKey(KeyCode.LeftArrow)) d.x -= 1;
        if (Input.GetKey(KeyCode.RightArrow)) d.x += 1;
        if (Input.GetKey(KeyCode.DownArrow)) d.y -= 1;
        if (Input.GetKey(KeyCode.UpArrow)) d.y += 1;
        if (d == Vector2.zero)
        {
            // left stick as a digital pad
            float x = Input.GetAxisRaw("Horizontal"), y = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(x) > 0.6f || Mathf.Abs(y) > 0.6f)
            {
                d = Mathf.Abs(x) > Mathf.Abs(y) ? new Vector2(Mathf.Sign(x), 0) : new Vector2(0, Mathf.Sign(y));
            }
        }
        else if (d.x != 0 && d.y != 0)
        {
            d.y = 0; // one axis at a time
        }
        return d;
    }

    // ---------------------------------------------------------------- focus

    private bool pageBusy;

    private static bool SettingsOpen()
    {
        var sc = SettingsController._instance;
        return sc != null && sc.settingsView != null && sc.settingsView.activeInHierarchy;
    }

    // settings pages are dense lists: the light has to keep up there
    private static float GlideScale() => SettingsOpen() ? 0.55f : 1f;

    private static bool IsTile(GameObject go) => go.GetComponentInParent<AppHandler>() != null || go.GetComponentInParent<EmptyAppHandler>() != null;

    // Left/right off the edge of the channel grid slides to the next/previous page, like the Wii (no arrow buttons)
    private bool TryPage(List<Candidate> cands, int cur, int next, Vector2 dir)
    {
        var gc = GridController._instance;
        var cc = ChannelController._instance;
        if (dir.x == 0 || gc == null || gc.currentGrid == null || cc == null || cc.currentChannel != null) return false;
        if (PopupController.IsPopupOpen() || AppController.IsTransitioning() || gc.isChangingGrid) return false;
        if (!IsTile(cands[cur].go)) return false;
        if (next >= 0 && IsTile(cands[next].go)) return false;
        int target = gc.currentGrid.number + (dir.x > 0 ? 1 : -1);
        if (target < 0 || target >= gc.GetNumberOfGrids()) return next < 0; // nothing there: just stay
        pageBusy = true;
        float row = cands[cur].center.y;
        focus = null;
        RInput.PlacePointer(new Vector2(-400f, -400f)); // park off screen so tiles sliding past never get hovered
        gc.ChangeGrid(target, false, 0.42f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
        StartCoroutine(FocusAfterPage(row, dir.x));
        return true;
    }

    private System.Collections.IEnumerator FocusAfterPage(float row, float dirX)
    {
        yield return new WaitForSecondsRealtime(0.52f);
        // after the slide has finished putting the other pages away (its own heavy frame): the new page's tiles are then the only
        // ones to look through, and the two jobs no longer land on the same frame (a stall there held the clock's colon)
        var gcw = GridController._instance; float waitUntil = Time.unscaledTime + 1f;
        while (gcw != null && gcw.isChangingGrid && Time.unscaledTime < waitUntil) yield return null;
        yield return null;
        long pf = TVFrameProbe.Begin();
        pageBusy = false;
        holdTimer = -0.12f; // a held left/right carries on across the new page and its tiles
        var gc0 = GridController._instance;
        var cands = Gather(true);
        float extreme = dirX > 0 ? float.MaxValue : float.MinValue;
        // only tiles of the page we landed on (the neighbour's edge column peeks in at the side)
        int pg = gc0 != null && gc0.currentGrid != null ? gc0.currentGrid.number : -1;
        cands.RemoveAll(c => pg >= 0 && IsTile(c.go) && c.go.GetComponentInParent<Grid>() != null && c.go.GetComponentInParent<Grid>().number != pg);
        foreach (var c in cands) if (IsTile(c.go)) extreme = dirX > 0 ? Mathf.Min(extreme, c.center.x) : Mathf.Max(extreme, c.center.x);
        int best = -1; float bestD = float.MaxValue;
        for (int i = 0; i < cands.Count; i++)
        {
            if (!IsTile(cands[i].go) || Mathf.Abs(cands[i].center.x - extreme) > 60f) continue;
            float d = Mathf.Abs(cands[i].center.y - row);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best >= 0) SetFocus(cands[best]);
        TVFrameProbe.End("focuspage", pf);
    }

    private void Move(Vector2 dir)
    {
        moveCount++;
        if (pageBusy) return;
        if (InOptions)
        {
            var oc = OptionCandidates();
            if (oc.Count > 0)
            {
                int at = IndexOf(oc, focus);
                if (at < 0) { SetFocus(oc[0]); return; }
                int to = Pick(oc, at, dir);
                if (to >= 0) SetFocus(oc[to]);
                return;
            }
        }
        if (!InOptions) optionsTile = null;
        var cands = Gather(false);
        if (cands.Count == 0)
        {
            // nothing to focus: fall back to a free pointer
            PointerMode = true;
            RInput.NudgePointer(dir);
            return;
        }

        int cur = IndexOf(cands, focus);
        if (cur < 0)
        {
            // first press on this screen: land on the thing nearest to where we were
            Vector2 from = lastFocusCenter != Vector2.zero ? lastFocusCenter : RInput.mousePosition;
            // lost inside a scroll list: stay in that list rather than land on a button beside it
            var inList = lastList != null && lastList.isActiveAndEnabled ? cands.FindAll(k => k.go.transform.IsChildOf(lastList.transform)) : null;
            if (inList != null && inList.Count > 0) { SetFocus(inList[Nearest(inList, from)]); return; }
            SetFocus(cands[Nearest(cands, from)]);
            return;
        }

        // a row with a slider (music / sound volume...): Left/Right change the value in 5% steps instead of moving the light
        if (Mathf.Abs(dir.x) > 0 && SliderStep(cands[cur].go, dir.x)) return;
        if (ListStep(cands[cur].go, dir)) return;
        var ccp = ChannelController._instance;
        if (ccp != null && ccp.currentChannel != null && Mathf.Abs(dir.x) > 0 && cands[cur].go.name.Contains("Arrow"))
        {
            bool fromLeft = cands[cur].center.x < Screen.width * 0.5f;
            if (fromLeft == (dir.x > 0))
            {
                int pick = -1; float bx = 0f;
                for (int i = 0; i < cands.Count; i++)
                {
                    if (i == cur || cands[i].go.name.Contains("Arrow") || cands[i].center.y > Screen.height * 0.35f) continue; // the buttons along the bottom
                    float x = cands[i].center.x;
                    if (pick < 0 || (fromLeft ? x < bx : x > bx)) { pick = i; bx = x; }
                }
                if (pick >= 0) { SetFocus(cands[pick]); return; }
            }
            else return; // pressing outward on an arrow: stay on it
        }

        int next = Pick(cands, cur, dir);
        if (SearchJump(cands, cur, next, dir)) return;
        // home: Up from the top row reaches the power button in the corner
        if (dir.y > 0 && (next < 0 || !IsTile(cands[next].go)) && IsTile(cands[cur].go) && TVPower.ButtonGo != null)
        {
            var prt = (RectTransform) TVPower.ButtonGo.transform;
            var pc = new Candidate { go = TVPower.ButtonGo };
            pc.screen = ScreenRect(prt, corners); pc.center = pc.screen.center; pc.press = pc.center;
            SetFocus(pc);
            return;
        }
        if (TryPage(cands, cur, next, dir)) return;

        // inside a scrolling list, scroll first: don't leap out to a button that happens to be below/above the list
        if (next >= 0 && LeavesScrollList(cands[cur].go, cands[next].go, dir))
        {
            if (ScrollAround(cands[cur].go, dir))
            {
                Canvas.ForceUpdateCanvases();
                cands = Gather(true);
                int again = IndexOf(cands, focus);
                if (again >= 0)
                {
                    int inList = Pick(cands, again, dir);
                    if (inList >= 0 && !LeavesScrollList(cands[again].go, cands[inList].go, dir))
                    {
                        SetFocus(cands[inList]);
                        return;
                    }
                    cur = again;
                    next = Pick(cands, cur, dir);
                }
            }
        }

        if (next >= 0)
        {
            SetFocus(cands[next]);
            return;
        }

        // nothing further: if we're inside a scroll list, scroll it and try again
        if (ScrollAround(cands[cur].go, dir))
        {
            Canvas.ForceUpdateCanvases();
            cands = Gather(true);
            cur = IndexOf(cands, focus);
            if (cur < 0) cur = Nearest(cands, lastFocusCenter);
            next = Pick(cands, cur, dir);
            if (next >= 0) SetFocus(cands[next]);
        }
    }

    // Settings lists (Channel creation's app list...): the round search button sits alone in the top corner, so Up from the top
    // row (or Right with nothing further right) never reached it. Now those presses land on it.
    private bool SearchJump(List<Candidate> cands, int cur, int next, Vector2 dir)
    {
        var sc = SettingsController._instance;
        if (sc == null || sc.searchIcon == null || !sc.searchIcon.activeInHierarchy) return false;
        GameObject sb = ExecuteEvents.GetEventHandler<IPointerClickHandler>(sc.searchIcon);
        if (sb == null) { var b = sc.searchIcon.GetComponentInChildren<Button>(); if (b != null) sb = b.gameObject; }
        if (sb == null || focus == sb || (focus != null && focus.transform.IsChildOf(sb.transform))) return false;
        bool up = dir.y > 0, right = dir.x > 0;
        if (!up && !right) return false;
        if (right && next >= 0) return false;
        if (up)
        {
            float y = cands[cur].center.y + 10f;
            foreach (var k in cands) if (k.center.y > y && k.go != sb && !k.go.transform.IsChildOf(sb.transform) && k.go.GetComponentInParent<ScrollRect>() != null) return false;
            if (next >= 0 && cands[next].go.GetComponentInParent<ScrollRect>() == null && next != cur && cands[next].go != sb && cands[next].center.y > y
                && Mathf.Abs(cands[next].center.x - cands[cur].center.x) < Screen.width * 0.25f) return false; // a header button straight above
        }
        var rt = (RectTransform) sb.transform;
        var c = new Candidate { go = sb };
        c.screen = ScreenRect(rt, corners); c.center = c.screen.center; c.press = c.center;
        SetFocus(c);
        return true;
    }

    private float staleTimer;

    // a tile can keep its hover frame (and name tag) after the light has left it; only the tile holding the light stays hovered
    private void ReleaseStaleHover()
    {
        if (!TVMode.IsTV || ChannelController._instance == null) return;
        foreach (var ch in ChannelController._instance.loadedChannels)
        {
            var ah = ch.appHandler;
            if (ah == null || !ah.isBeingHovered) continue;
            if (focus == null || !focus.transform.IsChildOf(ah.transform)) ah.MouseLeave();
        }
    }

    private void SetFocus(Candidate c)
    {
        // a list item the light lands on is scrolled fully into view: half hidden under the list's edge it stops being
        // found, and the next quick press jumped to whatever was nearest (the Back button)
        if (!IsTile(c.go) && ScrollIntoView(c.go))
        {
            Canvas.ForceUpdateCanvases();
            c.screen = ScreenRect((RectTransform) c.go.transform, corners);
            c.center = c.screen.center;
            c.press = Vector2.zero;
            cacheTime = -10f;
        }
        lastList = IsTile(c.go) ? null : c.go.GetComponentInParent<ScrollRect>();
        focus = c.go;
        WiiButton.ReleaseOthers(c.go.transform);
        ReleaseStaleHover();
        fitGraphics = null;
        focusTime = Time.unscaledTime;
        glideOffset = glowWasShown && lastFocusCenter != Vector2.zero && Vector2.Distance(lastFocusCenter, c.center) < Screen.width * 0.6f ? lastFocusCenter - c.center : Vector2.zero;
        glideVel = Vector2.zero;
        glowWasShown = false;
        lastFocusCenter = c.center;
        RInput.PlacePointer(c.press != Vector2.zero ? c.press : c.center);
        pointerOnCenter = c.press == Vector2.zero;
        pointerAt = c.center;
    }
    private bool pointerOnCenter;
    private Vector2 pointerAt;

    // OK was just pressed: whatever the light is on must be what the click point is on, wherever a popup or list moved it since.
    // And if the press still does nothing (a theme's own pictures can swallow it), the button is pressed once OK is let go.
    private UnityEngine.UI.Button watchBtn;
    private bool watchHit;
    private float okDownAt, watchAt;
    private void OnWatchedClick() { watchHit = true; }
    private void StopWatching()
    {
        if (watchBtn != null) watchBtn.onClick.RemoveListener(OnWatchedClick);
        watchBtn = null; watchAt = 0f;
    }

    // puts the light on a tile itself (its Click target), e.g. when a move starts from the tile's menu
    // any button on screen (screens built at run time put the light on their first / last-used row)
    public static GameObject Focused => _instance != null ? _instance.focus : null;
    public static void FocusButton(GameObject go)
    {
        var n = _instance;
        if (n == null || go == null) return;
        var cs = n.Gather(true); int i = IndexOf(cs, go);
        if (i >= 0) { n.autoUntil = 0f; n.SetFocus(cs[i]); } // the screen chose: no more re-aiming at the middle
        else Debug.Log("[NAV] FocusButton: not focusable now: " + go.name);
    }

    // something outside the usual lists (the power button in the corner)
    public static void FocusDirect(GameObject go)
    {
        var n = _instance;
        if (n == null || go == null) return;
        var c = new Candidate { go = go };
        c.screen = ScreenRect((RectTransform) go.transform, n.corners); c.center = c.screen.center; c.press = c.center;
        n.autoUntil = 0f; n.SetFocus(c);
    }

    public static void FocusTile(Transform tile)
    {
        var n = _instance;
        if (n == null || tile == null) return;
        Transform click = null;
        foreach (var t in tile.GetComponentsInChildren<Transform>(true)) if (t.name == "Click") { click = t; break; }
        if (click == null) return;
        var cs = n.Gather(false); int i = IndexOf(cs, click.gameObject);
        if (i >= 0) n.SetFocus(cs[i]);
    }

    public static void EnsurePointerOnFocus()
    {
        var n = _instance;
        if (n == null) return;
        n.StopWatching();
        // OK pressed as a popup comes up, before the light reached it: the press used to go nowhere (a second press was needed).
        // A one-button popup is answered at once; a Yes/No one only gets the light on Yes (never a choice nobody saw)
        var pop = PopupController.CurrentPopup;
        if (!PointerMode && pop != null && pop.activeInHierarchy && (n.focus == null || !n.focus.activeInHierarchy || !n.focus.transform.IsChildOf(pop.transform)))
        {
            UnityEngine.UI.Button ok = null, yes = null;
            foreach (var b in pop.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                if (!b.interactable) continue;
                if (b.name == "Button_OK") ok = b; else if (b.name == "Button_Yes") yes = b;
            }
            if (yes == null && ok != null) { Debug.Log("[Click] popup OK pressed directly"); ok.onClick.Invoke(); return; }
            var target = yes != null ? yes : ok;
            if (target != null) { var cs = n.Gather(false); int i = IndexOf(cs, target.gameObject); if (i >= 0) n.SetFocus(cs[i]); return; }
        }
        if (PointerMode || n.focus == null || !n.focus.activeInHierarchy || IsTile(n.focus)) return;
        var rt = n.focus.transform as RectTransform;
        if (rt == null || EventSystem.current == null) return;
        var btn = n.focus.GetComponent<UnityEngine.UI.Button>();
        if (btn != null && btn.interactable) { n.watchBtn = btn; n.watchHit = false; n.okDownAt = Time.unscaledTime; btn.onClick.AddListener(n.OnWatchedClick); }
        var cursorRoot = InputController._instance != null ? InputController._instance.mainCursor : null;
        var data = n.Data();
        if (n.IsTopmost(n.focus, RInput.mousePosition, data, cursorRoot)) return;
        Rect r = ScreenRect(rt, n.corners);
        if (n.IsTopmost(n.focus, r.center, data, cursorRoot)) { RInput.PlacePointer(r.center); n.pointerAt = r.center; return; }
        Rect fr = n.FitRect(rt);
        if (n.IsTopmost(n.focus, fr.center, data, cursorRoot)) { RInput.PlacePointer(fr.center); n.pointerAt = fr.center; }
    }

    public static void FinishClick()
    {
        var n = _instance;
        if (n == null || n.watchBtn == null) return;
        if (Time.unscaledTime - n.okDownAt > 0.5f) { n.StopWatching(); return; } // a held OK is the long press, not a click
        n.watchAt = Time.unscaledTime + 0.15f;
    }

    private void CheckWatchedClick()
    {
        if (watchBtn == null || watchAt <= 0f || Time.unscaledTime < watchAt) return;
        var b = watchBtn;
        bool hit = watchHit;
        StopWatching();
        if (!hit && b != null && b.isActiveAndEnabled && b.interactable && focus == b.gameObject)
        {
            Debug.Log("[Click] pressed " + b.name + " directly");
            b.onClick.Invoke();
        }
    }

    private static int IndexOf(List<Candidate> list, GameObject go)
    {
        if (go == null) return -1;
        for (int i = 0; i < list.Count; i++) if (list[i].go == go) return i;
        return -1;
    }

    private static int Nearest(List<Candidate> list, Vector2 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            float d = (list[i].center - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static int Pick(List<Candidate> list, int cur, Vector2 dir)
    {
        Candidate from = list[cur];
        int best = -1;
        float bestScore = float.MaxValue;
        // Left/Right stay in the row they're in (HDMI -> SD card along the bar, not up to a tile above) while that row has
        // something further that way; only then may the light leave the row
        if (Mathf.Abs(dir.x) > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (i == cur) continue;
                Vector2 v = list[i].center - from.center;
                float along = Vector2.Dot(v, dir);
                if (along < 12f) continue;
                float band = Mathf.Min(from.screen.height, list[i].screen.height) * 0.5f;
                if (Mathf.Abs(v.y) > band) continue;
                if (along < bestScore) { bestScore = along; best = i; }
            }
            if (best >= 0) return best;
            // Settings: Left with nothing beside goes straight to the on-screen Back button (no scrolling down to reach it)
            if (dir.x < 0 && SettingsOpen())
            {
                var bb = SettingsController._instance.backButton;
                if (bb != null) for (int i = 0; i < list.Count; i++) if (i != cur && list[i].go != null && list[i].go.transform.IsChildOf(bb.transform)) return i;
            }
            // a row of a settings list with nothing beside it: Left/Right stay on it (it used to jump to the checkbox of the
            // row above, so one more OK changed a setting nobody meant to touch)
            var sr = from.go != null ? from.go.GetComponentInParent<ScrollRect>() : null;
            if (sr != null && sr.vertical && !sr.horizontal && sr.content != null && from.go.transform.IsChildOf(sr.content) && !IsTile(from.go)) return -1;
            bestScore = float.MaxValue;
        }
        for (int i = 0; i < list.Count; i++)
        {
            if (i == cur) continue;
            Vector2 v = list[i].center - from.center;
            float along = Vector2.Dot(v, dir);
            // use edge distance for wide items so rows of big tiles still chain nicely
            float halfFrom = Mathf.Abs(dir.x) > 0 ? from.screen.width * 0.5f : from.screen.height * 0.5f;
            float halfTo = Mathf.Abs(dir.x) > 0 ? list[i].screen.width * 0.5f : list[i].screen.height * 0.5f;
            float gap = along - halfFrom - halfTo;
            if (along < 12f) continue;
            float perp = Mathf.Abs(dir.x) > 0 ? Mathf.Abs(v.y) : Mathf.Abs(v.x);
            if (perp > along * 2.2f + 60f) continue; // outside a wide cone
            float score = Mathf.Max(gap, 0f) * 1.5f + along * 0.25f + perp * 1.5f; // the near button below wins over a far one straight below (Hub: Preview Icon -> Download, not Back)
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    // A "TextHolder" is a Button with an invisible, sprite-less background and no click of its own: just a caption.
    // Text-only buttons the app does wire up keep their runtime listener, so they stay focusable.
    private static bool IsBareLabel(GameObject target)
    {
        var b = target.GetComponent<Button>();
        if (b == null || b.onClick.GetPersistentEventCount() > 0 || RuntimeListeners(b.onClick) != 0) return false;
        if (!(b.targetGraphic is Image img) || img.sprite != null || img.color.a > 0.02f) return false;
        return target.GetComponentInChildren<TMPro.TMP_Text>() != null;
    }

    private static System.Reflection.FieldInfo callsField, runtimeField;

    // how many listeners were added from code (AddListener); -1 when that can't be read (then treat it as wired)
    private static int RuntimeListeners(UnityEngine.Events.UnityEventBase e)
    {
        try
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            callsField ??= typeof(UnityEngine.Events.UnityEventBase).GetField("m_Calls", F);
            var calls = callsField?.GetValue(e);
            if (calls == null) return -1;
            runtimeField ??= calls.GetType().GetField("m_RuntimeCalls", F);
            return runtimeField?.GetValue(calls) is System.Collections.ICollection list ? list.Count : -1;
        }
        catch (System.Exception) { return -1; }
    }

    // The entry next to 'item' in its list, by the list's own order (works while it's still scrolled out of view).
    // Only for plain one-column / one-row lists (a vertical or horizontal layout group); grids are left to Pick.
    private static bool SliderStep(GameObject item, float dx)
    {
        var sl = item.GetComponentInParent<Slider>();
        if (sl == null)
        {
            // the light sits on the row: the slider is somewhere inside the same settings row
            var holder = SettingsController._instance != null ? SettingsController._instance.optionsHolder : null;
            Transform row = item.transform;
            while (holder != null && row.parent != null && row.parent != holder) row = row.parent;
            if (holder != null && row.parent == holder) sl = row.GetComponentInChildren<Slider>(false);
        }
        if (sl == null || !sl.IsInteractable()) return false;
        float step = sl.wholeNumbers ? 1f : (sl.maxValue - sl.minValue) * 0.05f;
        float v = Mathf.Clamp(sl.value + Mathf.Sign(dx) * step, sl.minValue, sl.maxValue);
        if (sl.wholeNumbers) v = Mathf.Round(v);
        if (v != sl.value) { sl.value = v; AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON); }
        return true;
    }

    private bool ListStep(GameObject item, Vector2 dir)
    {
        if (IsTile(item)) return false;
        var sr = item.GetComponentInParent<ScrollRect>();
        if (sr == null || sr.content == null || !item.transform.IsChildOf(sr.content)) return false;
        bool vertical = Mathf.Abs(dir.y) > 0;
        if (vertical ? !(sr.vertical && sr.content.GetComponent<VerticalLayoutGroup>() != null)
                     : !(sr.horizontal && sr.content.GetComponent<HorizontalLayoutGroup>() != null)) return false;
        // the list entry holding the item
        Transform row = item.transform;
        while (row.parent != sr.content) row = row.parent;
        int step = vertical ? (dir.y < 0 ? 1 : -1) : (dir.x > 0 ? 1 : -1);
        var here = ScreenRect((RectTransform) item.transform, corners);
        for (int i = row.GetSiblingIndex() + step; i >= 0 && i < sr.content.childCount; i += step)
        {
            var other = sr.content.GetChild(i);
            if (!other.gameObject.activeInHierarchy) continue;
            GameObject best = null; float bestD = float.MaxValue;
            foreach (var g in other.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.raycastTarget || !g.isActiveAndEnabled) continue;
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(g.gameObject);
                if (target == null)
                {
                    var sel = g.GetComponentInParent<Selectable>();
                    if (sel != null && sel.IsInteractable()) target = sel.gameObject;
                }
                if (target == null || !target.transform.IsChildOf(other) || IsBareLabel(target)) continue;
                var r = ScreenRect((RectTransform) target.transform, corners);
                if (r.width < 14f || r.height < 14f) continue;
                // the entry's control nearest the light's column (a row's checkbox vs. the row itself)
                float d = vertical ? Mathf.Abs(r.center.x - here.center.x) : Mathf.Abs(r.center.y - here.center.y);
                if (d < bestD) { bestD = d; best = target; }
            }
            if (best == null) continue; // a heading or spacer: skip over it
            var rt = (RectTransform) best.transform;
            var c = new Candidate { go = best };
            c.screen = ScreenRect(rt, corners); c.center = c.screen.center; c.press = c.center;
            SetFocus(c); // scrolls it fully into view and re-measures it
            return true;
        }
        return false; // the list's last entry: leaving it is up to Pick
    }

    // true when moving from 'from' to 'to' would leave a ScrollRect that can still scroll in that direction
    private static bool LeavesScrollList(GameObject from, GameObject to, Vector2 dir)
    {
        var sr = from.GetComponentInParent<ScrollRect>();
        if (sr == null || to.transform.IsChildOf(sr.transform)) return false;
        if (Mathf.Abs(dir.y) > 0 && sr.vertical)
        {
            return dir.y < 0 ? sr.verticalNormalizedPosition > 0.002f : sr.verticalNormalizedPosition < 0.998f;
        }
        if (Mathf.Abs(dir.x) > 0 && sr.horizontal)
        {
            return dir.x > 0 ? sr.horizontalNormalizedPosition < 0.998f : sr.horizontalNormalizedPosition > 0.002f;
        }
        return false;
    }

    private static bool ScrollAround(GameObject item, Vector2 dir)
    {
        var sr = item.GetComponentInParent<ScrollRect>();
        if (sr == null || sr.content == null || sr.viewport == null && sr.GetComponent<RectTransform>() == null) return false;
        var vp = sr.viewport != null ? sr.viewport : (RectTransform) sr.transform;
        bool vertical = Mathf.Abs(dir.y) > 0;
        if (vertical && !sr.vertical) return false;
        if (!vertical && !sr.horizontal) return false;

        float step = (vertical ? vp.rect.height : vp.rect.width) * 0.45f;
        Vector2 pos = sr.content.anchoredPosition;
        Vector2 before = pos;
        if (vertical) pos.y += -dir.y * step; else pos.x += -dir.x * step;
        sr.content.anchoredPosition = pos;
        // let the ScrollRect clamp it
        sr.velocity = Vector2.zero;
        sr.StopMovement();
        if (vertical) sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition);
        else sr.horizontalNormalizedPosition = Mathf.Clamp01(sr.horizontalNormalizedPosition);
        return (sr.content.anchoredPosition - before).sqrMagnitude > 1f;
    }

    private static readonly Vector3[] itemCorners = new Vector3[4];
    private ScrollRect lastList;

    // moves a scroll list just enough that 'item' sits wholly inside its view; true when it moved
    private static bool ScrollIntoView(GameObject item)
    {
        var sr = item.GetComponentInParent<ScrollRect>();
        if (sr == null || sr.content == null || !item.transform.IsChildOf(sr.content)) return false;
        if (GridController._instance != null && sr == GridController._instance.scrollRect) return false; // the home pages slide on their own
        var vp = sr.viewport != null ? sr.viewport : (RectTransform) sr.transform;
        if (!(item.transform is RectTransform rt)) return false;
        rt.GetWorldCorners(itemCorners);
        Vector3 lo = vp.InverseTransformPoint(itemCorners[0]), hi = vp.InverseTransformPoint(itemCorners[2]);
        Rect v = vp.rect;
        const float pad = 6f;
        Vector2 shift = Vector2.zero;
        if (sr.vertical && hi.y - lo.y < v.height - pad * 2f)
        {
            if (hi.y > v.yMax - pad) shift.y = (v.yMax - pad) - hi.y;
            else if (lo.y < v.yMin + pad) shift.y = (v.yMin + pad) - lo.y;
        }
        if (sr.horizontal && hi.x - lo.x < v.width - pad * 2f)
        {
            if (hi.x > v.xMax - pad) shift.x = (v.xMax - pad) - hi.x;
            else if (lo.x < v.xMin + pad) shift.x = (v.xMin + pad) - lo.x;
        }
        if (shift.sqrMagnitude < 1f) return false;
        Vector2 before = sr.content.anchoredPosition;
        sr.content.anchoredPosition = before + shift;
        sr.velocity = Vector2.zero;
        sr.StopMovement();
        if (sr.vertical) sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition);
        if (sr.horizontal) sr.horizontalNormalizedPosition = Mathf.Clamp01(sr.horizontalNormalizedPosition);
        return (sr.content.anchoredPosition - before).sqrMagnitude > 1f;
    }

    // Called a few times a second: drop focus that got covered / hidden and, in remote mode, always have something highlighted
    private void KeepFocusValid()
    {
        if (PointerMode || EventSystem.current == null || pageBusy) return;
        // a tile's open menu keeps the focus (a channel's buttons sit under the tile's own cover graphics)
        if (InOptions && focus != null && focus.activeInHierarchy && focus.transform.IsChildOf(OptionsMenu.transform)) return;
        if (!InOptions) optionsTile = null;
        // a held OK just opened this tile's menu over it: keep the tile until OK is let go and the light moves onto the menu
        if (!InOptions && focus != null && focus.activeInHierarchy)
        {
            var held = TileOf(focus);
            if (held != null && TileMenu(held) != null) return;
        }

        if (focus != null)
        {
        // home tiles: the light sits just BEHIND the focused tile (only the halo shows around its edge, nothing covers the art)
        Transform tileRoot = InOptions && focus != null && focus.transform.IsChildOf(OptionsMenu.transform) ? null : TileRoot(focus); // a tile's menu buttons: lit on top, not behind the tile
        Transform wantParent = tileRoot != null ? tileRoot.parent : AppController._instance.overlayCanvas;
        if (wantParent != null && (glowRect.parent != wantParent || (tileRoot != null && glowRect.GetSiblingIndex() > tileRoot.GetSiblingIndex())))
        {
            glowRect.SetParent(wantParent, false);
            var le = glowRect.GetComponent<LayoutElement>() ?? glowRect.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            if (tileRoot != null) glowRect.SetSiblingIndex(Mathf.Max(0, tileRoot.GetSiblingIndex()));
            else if (InputController._instance != null && InputController._instance.cursorHolder != null) glowRect.SetSiblingIndex(InputController._instance.cursorHolder.GetSiblingIndex());
        }
            var rt = focus.transform as RectTransform;
            bool ok = focus.activeInHierarchy && rt != null;
            if (ok)
            {
                Rect r = ScreenRect(rt, corners);
                var cursorRoot = InputController._instance != null ? InputController._instance.mainCursor : null;
                ok = r.width > 8f && IsTopmost(focus, r.center, Data(), cursorRoot);
                var pop = ModalRoot();
                if (ok && pop != null && !focus.transform.IsChildOf(pop)) ok = false;
            }
            if (ok)
            {
                // lists load a moment after a screen opens: until the user moves, keep re-aiming at the content
                // only for screens whose lists load late; the home grid keeps the focus the user chose
                if (Time.unscaledTime >= autoUntil || AppController.IsTransitioning() || IsTile(focus) || (GridController._instance != null && GridController._instance.isChangingGrid)) return;
                var again = Gather(true);
                if (again.Count == 0) return;
                int ni = Nearest(again, new Vector2(Screen.width * 0.5f, Screen.height * 0.4f));
                if (again[ni].go != focus) SetFocus(again[ni]); // no re-arming: this runs once, at screen open
                return;
            }
            focus = null;
        }

        if (AppController._instance != null && AppController.IsTransitioning()) return;
        var cands = Gather(true);
        if (cands.Count == 0) return;
        // on an open channel the obvious thing to highlight is Start
        if (ChannelController._instance != null && ChannelController._instance.currentChannel != null)
        {
            foreach (var c in cands)
            {
                if (c.go.name == "Start") { SetFocus(c); return; }
            }
        }
        else if (returnTile != null)
        {
            foreach (var c in cands) if (c.go.transform.IsChildOf(returnTile)) { returnTile = null; SetFocus(c); return; }
            // not on screen yet (Settings still closing after the app list): wait for it rather than pick something else
            if (Time.unscaledTime - returnAt < 3f) return;
            returnTile = null;
        }
        if (returnName != null)
        {
            // a screen that rebuilt itself (the weather map after a pan): the light goes back to the same button
            string want = returnName;
            foreach (var c in cands) if (c.go.name == want) { returnName = null; autoUntil = 0f; SetFocus(c); return; }
            if (Time.unscaledTime - returnNameAt < 0.5f) return; // being built: wait for it
            returnName = null;
        }
        if (Time.unscaledTime < menuUntil)
        {
            // the tile's own buttons removed it: the light lands on the slot that took its place
            var tiles = cands.FindAll(c => IsTile(c.go));
            if (tiles.Count > 0) { menuUntil = 0f; SetFocus(tiles[Nearest(tiles, menuPoint)]); return; }
        }
        Vector2 from = new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
        SetFocus(cands[Nearest(cands, from)]);
        autoUntil = Time.unscaledTime + 3f;
    }
    private float autoUntil;
    // hover sounds only count when the person actually moved or pressed something a moment ago
    public static float LastUserInput = -10f;
    public static bool UserActive => Time.unscaledTime - LastUserInput < 0.7f;

    // ---------------------------------------------------------------- candidates

    private static readonly List<RaycastResult> hits = new List<RaycastResult>();

    private PointerEventData Data()
    {
        if (scratchData == null || EventSystem.current == null) scratchData = new PointerEventData(EventSystem.current);
        return scratchData;
    }

    private void RefreshCanvases()
    {
        if (Time.unscaledTime - canvasTime < 1.5f && canvases.Count > 0) return;
        canvasTime = Time.unscaledTime;
        canvases.Clear();
        canvases.AddRange(FindObjectsByType<Canvas>(FindObjectsSortMode.None));
    }

    private List<Candidate> Gather(bool force)
    {
        if (!force && Time.unscaledTime - cacheTime < CacheLife) return cache;
        cacheTime = Time.unscaledTime;
        cache = new List<Candidate>();
        var result = cache;
        if (EventSystem.current == null) return result;

        long pgc = TVFrameProbe.Begin();
        RefreshCanvases();
        TVFrameProbe.End("g.canvases", pgc); pgc = TVFrameProbe.Begin();
        seen.Clear();
        var cursorRoot = InputController._instance != null ? InputController._instance.mainCursor : null;
        var data = Data();
        var screenRect = new Rect(0, 0, Screen.width, Screen.height);
        var popupRoot = ModalRoot();
        // just the home menu on screen (nothing open over it): its tiles can't be covered, so they skip the "is it on top"
        // ray test, one full UI hit-test per tile, ~60 ms on the projector for every press that turned a page
        var ccG = ChannelController._instance; var shopG = ShopController._instance;
        bool homeClear = popupRoot == null && !InOptions && !SettingsOpen() && !OnWarningScreen && !TVMove.Active
                         && (ccG == null || ccG.currentChannel == null) && !AppController.IsTransitioning()
                         && (shopG == null || shopG.shopMenu == null || !shopG.shopMenu.activeInHierarchy)
                         && !TVWeather.Open && !TVServerScreen.Open && !TVControllers.Open && !TVGameScreen.Open;
        var gcG = GridController._instance;
        Transform curGridT = gcG != null && gcG.currentGrid != null && !gcG.isChangingGrid ? gcG.currentGrid.transform : null;

        foreach (var canvas in canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled) continue;
            var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
            for (int i = 0; i < graphics.Count; i++)
            {
                var g = graphics[i];
                if (g == null || !g.raycastTarget || !g.isActiveAndEnabled) continue;
                if (cursorRoot != null && g.transform.IsChildOf(cursorRoot)) continue;
                if (glowRect != null && g.transform.IsChildOf(glowRect)) continue;

                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(g.gameObject);
                if (target == null)
                {
                    var sel = g.GetComponentInParent<Selectable>();
                    if (sel != null && sel.IsInteractable()) target = sel.gameObject;
                }
                if (target == null || !seen.Add(target)) continue;
                if (target.GetComponent<Scrollbar>() != null) continue; // a list's scrollbar is not a stop for the light: Up/Down already scroll the list
                if (popupRoot != null && !target.transform.IsChildOf(popupRoot)) continue;
                if (IsBareLabel(target)) continue;

                var rt = target.transform as RectTransform;
                if (rt == null) continue;
                Rect r = ScreenRect(rt, corners);
                if (r.width < 14f || r.height < 14f) continue;
                // huge catch-all blockers (full screen backgrounds) aren't things to focus
                if (r.width > Screen.width * 0.92f && r.height > Screen.height * 0.8f) continue;
                if (!screenRect.Contains(r.center)) continue;
                Vector2 press = r.center;
                if (!(homeClear && curGridT != null && target.transform.IsChildOf(curGridT) && IsTile(target)) && !IsTopmost(target, r.center, data, cursorRoot))
                {
                    // partly covered (a see-through strip over the middle, the edge of a list's frame): any other spot of it that
                    // is on top still counts, and the press goes there (Content Hub's banner "Set" button was never reachable)
                    bool any = false;
                    foreach (var f in new[] { new Vector2(0f, 0.3f), new Vector2(0f, -0.3f), new Vector2(-0.3f, 0f), new Vector2(0.3f, 0f) })
                    {
                        var pt = r.center + new Vector2(r.width * f.x, r.height * f.y);
                        if (screenRect.Contains(pt) && IsTopmost(target, pt, data, cursorRoot)) { press = pt; any = true; break; }
                    }
                    if (!any) continue;
                }

                // the bar's round buttons: the D-pad finds the button you see, not its clickable box (the mail's box sits
                // left of its face, about where the HDMI button is, so Left from mail used to skip HDMI); the press
                // still lands where it always did
                var face = RoundFace(target);
                if (face != null)
                {
                    Rect fr = ScreenRect(face, corners);
                    if (fr.width > 8f && screenRect.Contains(fr.center)) r = fr;
                }

                result.Add(new Candidate { go = target, screen = r, center = r.center, press = press });
            }
        }
        TVFrameProbe.End("g.scan", pgc);
        return result;
    }

    // the thing must actually be the one under its own centre, otherwise it's hidden or covered (e.g. by a popup)
    private bool IsTopmost(GameObject target, Vector2 center, PointerEventData data, RectTransform cursorRoot)
    {
        data.position = center;
        hits.Clear();
        EventSystem.current.RaycastAll(data, hits);
        GameObject top = null;
        foreach (var h in hits)
        {
            if (cursorRoot != null && h.gameObject.transform.IsChildOf(cursorRoot)) continue;
            if (glowRect != null && h.gameObject.transform.IsChildOf(glowRect)) continue;
            top = ExecuteEvents.GetEventHandler<IPointerClickHandler>(h.gameObject);
            if (top == null)
            {
                var s2 = h.gameObject.GetComponentInParent<Selectable>();
                top = s2 != null ? s2.gameObject : h.gameObject;
            }
            break;
        }
        if (top == null) return false;
        return top == target || top.transform.IsChildOf(target.transform) || target.transform.IsChildOf(top.transform);
    }

    private static Rect ScreenRect(RectTransform rt, Vector3[] cs)
    {
        rt.GetWorldCorners(cs);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, cs[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, cs[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ---------------------------------------------------------------- back key

    // the thing that currently owns the focus (an open popup, or the theme-preview bar)
    private static Transform ModalRoot()
    {
        if (PopupController.CurrentPopup != null && PopupController.CurrentPopup.activeInHierarchy) return PopupController.CurrentPopup.transform;
        if (TVPower.PanelRoot != null) return TVPower.PanelRoot;
        var tc = ThemeController._instance;
        if (tc != null && tc.themeReviewOverlay != null && tc.themeReviewOverlay.activeInHierarchy) return tc.themeReviewOverlay.transform;
        return null;
    }

    // Menu on a tile: a channel's Edit / Delete buttons, an empty slot's Add App (press again, or Back, to close them)
    private void HandleMenu()
    {
        if (!Input.GetKeyDown(KeyCode.Menu) || focus == null || pageBusy) return;
        var tile = TileOf(focus);
        if (tile == null || ChannelController._instance == null || ChannelController._instance.currentChannel != null) return;
        ToggleTileMenu(tile);
        FocusInTile(tile);
    }

    // the menu that opens over a tile (held OK, or Menu): a channel's Edit / Delete, an empty slot's Add App
    private static ChannelHandler TileOf(GameObject go)
    {
        var ah = go.GetComponentInParent<AppHandler>();
        if (ah != null) return ah;
        return go.GetComponentInParent<EmptyAppHandler>();
    }
    private static GameObject TileMenu(ChannelHandler tile)
    {
        if (tile is AppHandler ah) return ah.OptionsOpen ? ah.options : null;
        if (tile is EmptyAppHandler eh) return eh.MenuOpen ? eh.Menu : null;
        return null;
    }
    private static void ToggleTileMenu(ChannelHandler tile)
    {
        if (tile is AppHandler ah) ah.ToggleOptions();
        else if (tile is EmptyAppHandler eh) eh.ToggleMenu();
    }

    // the tile whose menu is open: the D-pad stays on its buttons until Back or Menu closes them
    private ChannelHandler optionsTile;
    private GameObject OptionsMenu => optionsTile != null ? TileMenu(optionsTile) : null;
    private bool InOptions => OptionsMenu != null;
    // where the tile with the menu was: if its buttons removed it, the light lands on what took its place
    private Vector2 menuPoint;
    private float menuUntil;

    private List<Candidate> OptionCandidates()
    {
        var list = new List<Candidate>();
        if (!InOptions) return list;
        Canvas.ForceUpdateCanvases();
        foreach (var b in OptionsMenu.GetComponentsInChildren<Button>(false))
        {
            if (!b.interactable) continue;
            Rect r = ScreenRect((RectTransform) b.transform, corners);
            if (r.width >= 14f && r.height >= 8f) list.Add(new Candidate { go = b.gameObject, screen = r, center = r.center });
        }
        return list;
    }

    // after the menu opens: land on Edit (the safe one, never straight on Delete); after it closes: back on the tile
    private void FocusInTile(ChannelHandler tile)
    {
        optionsTile = TileMenu(tile) != null ? tile : null;
        if (InOptions)
        {
            menuPoint = ScreenRect((RectTransform) tile.transform, corners).center;
            var oc = OptionCandidates();
            if (oc.Count == 0) return;
            int edit = oc.FindIndex(c => c.go.name == "Edit");
            SetFocus(oc[edit >= 0 ? edit : 0]);
            return;
        }
        Canvas.ForceUpdateCanvases();
        foreach (var c in Gather(true))
            if (c.go.transform.IsChildOf(tile.transform)) { SetFocus(c); return; }
    }

    // where the light goes back to once the screen it was on has gone: the channel just previewed, or a tile that was
    // just added (Back from the app list: the slot it was picked for)
    private static Transform returnTile;
    private static float returnAt;
    private static string returnName;
    public static void ReturnToNamed(string objectName) { returnName = objectName; returnNameAt = Time.unscaledTime; }
    private static float returnNameAt;
    public static string FocusName => _instance != null && _instance.focus != null ? _instance.focus.name : null;
    public static void ReturnTo(Transform tile) { returnTile = tile; returnAt = Time.unscaledTime; }

    // the light goes behind a focused tile, except for the buttons that open on top of it
    private static Transform TileRoot(GameObject go)
    {
        var ch = go.GetComponentInParent<ChannelHandler>();
        if (ch != null)
        {
            var open = TileMenu(ch);
            if (open != null && go.transform.IsChildOf(open.transform)) return null;
        }
        if (ch != null) return ch.transform;
        var eh = go.GetComponentInParent<EmptyAppHandler>();
        return eh != null ? eh.transform : null;
    }

    // an open prompt always owns the light: if the usual search lost its buttons (a screen under it rebuilt, a transition flag stuck), put the light on one of them
    private float popupSeen = -1f;
    private void PopupGuard()
    {
        var pop = PopupController.CurrentPopup;
        if (!PopupController.IsPopupOpen() || pop == null || !pop.activeInHierarchy) { popupSeen = -1f; return; }
        if (popupSeen < 0f) popupSeen = Time.unscaledTime;
        if (Time.unscaledTime - popupSeen < 0.8f) return; // still sliding in
        if (focus != null && focus.activeInHierarchy && focus.transform.IsChildOf(pop.transform)) return;
        foreach (var b in pop.GetComponentsInChildren<Button>(false))
        {
            if (!b.interactable) continue;
            Rect r = ScreenRect((RectTransform) b.transform, corners);
            if (r.width < 14f || r.height < 8f) continue;
            SetFocus(new Candidate { go = b.gameObject, screen = r, center = r.center, press = r.center });
            return;
        }
    }

    private void HandleBack()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) && !(Input.GetKeyDown(KeyCode.JoystickButton1) && !TVControllers.Open)) return; // a game pad's B is Back too (any TV)
        if (homeUntil > 0f) return; // the Home key is already closing everything
        BackStep();
    }

    // the remote's Home while Revo is on screen (Revo is the TV's home): whatever is open closes, one step at a time as Back
    // would, then the menu slides to its first page. Never on the warning screen.
    private static float homeUntil, homeStepAt;
    public static void GoHome() { homeUntil = Time.unscaledTime + 8f; homeStepAt = 0f; }
    private float homePollAt;
    private void HomeSteps()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Time.unscaledTime >= homePollAt)
        {
            homePollAt = Time.unscaledTime + 0.2f;
            bool pressed = false;
            try { using var a = new AndroidJavaClass("com.karuzohikari.revotv.RevoActivity"); pressed = a.CallStatic<bool>("consumeHome"); } catch (System.Exception) { }
            if (pressed && !OnWarningScreen) { GoHome(); Debug.Log("[NAV] Home key: back to the menu"); }
        }
#endif
        if (homeUntil <= 0f) return;
        if (Time.unscaledTime > homeUntil || OnWarningScreen) { homeUntil = 0f; return; }
        if (Time.unscaledTime < homeStepAt || AppController.IsTransitioning()) return;
        var fc = FadeController._instance;
        if (fc != null && fc.IsFading()) return;
        if (BackStep()) { homeStepAt = Time.unscaledTime + 0.35f; return; }
        homeUntil = 0f;
        var gc = GridController._instance;
        if (gc != null && gc.currentGrid != null && gc.currentGrid.number != 0 && !gc.isChangingGrid) gc.ChangeGrid(0);
    }

    // one Back: closes the top-most thing that is open; false when there was nothing to close
    private bool BackStep()
    {
        if (TVMove.Active) { TVMove.Cancel(); return true; }
        if (PopupController.IsPopupOpen())
        {
            PopupController.ClosePopup();
            return true;
        }
        if (InOptions) { var open = optionsTile; ToggleTileMenu(open); FocusInTile(open); return true; }
        // an animation previewed in the Content Hub has no button a remote can reach: Back closes it
        var pv = PreviewController._instance;
        var hub = ShopController._instance;
        if (pv != null && pv.isOpen && hub != null && hub.backButton != null && hub.backButton.activeInHierarchy) { pv.HideDecoy(false); return true; }
        if (TVServerScreen.Open) { TVServerScreen.Close(); return true; }
        if (TVControllers.Open) { TVControllers.Back(); return true; }
        if (TVGameScreen.Open) { TVGameScreen.Close(); return true; }
        if (TVWeather.Open) { TVWeather.Close(); return true; }
        var overlay = ModalRoot();
        if (overlay != null)
        {
            // leaving a theme preview: press its first button (Back / Done)
            var btn = overlay.GetComponentInChildren<Button>(false);
            if (btn != null && btn.interactable) { btn.onClick.Invoke(); return true; }
        }
        // the animation editor had no way out on a remote (the app had to be restarted): Back asks, then leaves it
        var ed = EditorController._instance;
        if (ed != null && ed.currentChannelAnimation != null && !(SettingsController._instance != null && SettingsController._instance.settingsView != null && SettingsController._instance.settingsView.activeInHierarchy))
        {
            PopupController.ShowPopup("tv.editor.leave", () => { PopupController.ClosePopup(); ed.ExitEditor(); }, PopupController.ClosePopup);
            return true;
        }
        var sc = SettingsController._instance;
        if (sc != null && sc.settingsView != null && sc.settingsView.activeInHierarchy && sc.backButton != null && sc.backButton.isActiveAndEnabled)
        {
            sc.backButton.onClick.Invoke();
            return true;
        }
        var shop = ShopController._instance;
        if (shop != null && shop.backButton != null && shop.backButton.activeInHierarchy)
        {
            var b = shop.backButton.GetComponent<Button>();
            if (b != null && b.interactable) { b.onClick.Invoke(); return true; }
        }
        var cc = ChannelController._instance;
        if (cc != null && cc.currentChannel != null && !AppController.IsTransitioning())
        {
            cc.CloseChannel();
            return true;
        }
        // any other page (preferences, animation type, ...): press its Back / Close / Done button
        foreach (var b in FindObjectsOfType<Button>())
        {
            if (!b.isActiveAndEnabled || !b.interactable) continue;
            string n = b.name.ToLowerInvariant();
            if (!(n.Contains("back") || n.Contains("close") || n.Contains("done") || n.Contains("cancel"))) continue;
            var g = b.targetGraphic;
            if (g == null || !g.raycastTarget || g.canvasRenderer.GetInheritedAlpha() < 0.1f) continue;
            b.onClick.Invoke();
            return true;
        }
        // nothing left to close: a launcher stays on its home screen, Back at the root does nothing
        return false;
    }

    // ---------------------------------------------------------------- glow

    private RectTransform glowRect;
    private RawImage glowImage;
    private Texture2D glowTex;
    private Color32[] glowPixels;
    private bool glowShapeDirty;
    private float focusTime;
    private Vector2 glideOffset, glideVel;
    private List<Graphic> fitGraphics;
    private float fitTime;
    private float glowPad;               // screen px of halo around the item
    private Vector2 smCenter, smSize;    // smoothed screen-space centre and size
    private Vector2 builtSize;
    private bool builtRound;
    private float builtThemedRad, glowThemedRad;
    private Vector2 smCenterVel, smSizeVel;
    private bool glowWasShown;
    private GameObject loggedFocus; private bool loggedShow;
    private float breathe;
    // per-distance lookup tables (px from the item edge) so building the bitmap stays cheap
    private float[] lutCore, lutNear, lutWide, lutInner;
    private float lutScale = -1f;

    private void EnsureGlow()
    {
        if (glowRect != null) return;
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;

        var go = new GameObject("TV Focus Glow", typeof(RectTransform), typeof(RawImage));
        glowRect = (RectTransform) go.transform;
        glowRect.SetParent(app.overlayCanvas, false);
        glowImage = go.GetComponent<RawImage>();
        glowImage.raycastTarget = false; // no sub-canvas here: as its own Canvas the light vanished behind the tiles (2026-11-07)
        glowTex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        glowImage.texture = glowTex;
        // sit just under the pointer
        if (InputController._instance != null && InputController._instance.cursorHolder != null)
        {
            glowRect.SetSiblingIndex(InputController._instance.cursorHolder.GetSiblingIndex());
        }
        go.SetActive(false);

    }

    // Wii-style highlight shape: circles stay circles, pills stay pills, tiles keep their soft corners
    private static float CornerRadius(Vector2 size, bool round)
    {
        float min = Mathf.Min(size.x, size.y);
        float ratio = size.x / Mathf.Max(1f, size.y);
        if (round) return min * 0.5f; // the bar's round buttons, however big the 2026 bar draws them
        if (ratio > 0.82f && ratio < 1.22f && min < Screen.height * 0.17f) return min * 0.5f; // round buttons
        if (size.y < Screen.height * 0.17f && ratio > 1.5f) return size.y * 0.5f;             // pill buttons (OK, Start...)
        return Mathf.Clamp(min * TVFx.FrameRadius, 10f, 34f);                                   // tiles & panels (the 2026 tile frame)
    }

    // Rebuild the lookup tables whenever the render resolution changes (all widths are expressed for 1080p)
    // GLSL-style smoothstep (Mathf.SmoothStep is an interpolation between two values, which is something else)
    private static float Step(float e0, float e1, float x)
    {
        x = Mathf.Clamp01((x - e0) / (e1 - e0));
        return x * x * (3f - 2f * x);
    }

    private void EnsureLuts()
    {
        float k = Screen.height / 1080f;
        if (Mathf.Approximately(k, lutScale) && lutCore != null) return;
        lutScale = k;
        const int N = 256;
        lutCore = new float[N]; lutNear = new float[N]; lutWide = new float[N]; lutInner = new float[N];
        for (int i = 0; i < N; i++)
        {
            float d = i / k; // distance in 1080p px
            // crisp bright rim hugging the edge (the Wii's lit channel border)
            lutCore[i] = 1f - Step(0.9f, 2.4f, d);
            // close bloom
            lutNear[i] = 0.78f * Mathf.Exp(-(d * d) / (2f * 5f * 5f));
            // wide faint halo: light spilling onto the wallpaper
            lutWide[i] = (TVGlass.On ? 0.48f : 0.36f) * Mathf.Exp(-(d * d) / (2f * 24f * 24f));
            // just inside the edge so the item itself reads as lit
            lutInner[i] = 0.30f * Mathf.Exp(-(d * d) / (2f * 5f * 5f));
        }
    }

    private string builtGlowKey;
    private void BuildGlowTexture(Vector2 size, bool round)
    {
        EnsureLuts();
        float k = lutScale;
        glowPad = Mathf.Clamp(46f * k, 24f, 80f);
        float totalW = size.x + glowPad * 2f, totalH = size.y + glowPad * 2f;
        // coarse enough to be cheap, fine enough to keep the rim crisp
        float scale = Mathf.Max(1f, Mathf.Max(totalW, totalH) / 360f);
        int w = Mathf.Max(8, Mathf.CeilToInt(totalW / scale)), h = Mathf.Max(8, Mathf.CeilToInt(totalH / scale));
        if (glowPixels == null || glowPixels.Length != w * h) glowPixels = new Color32[w * h];
        float radius = glowThemedRad > 0.05f ? Mathf.Min(size.x, size.y) * glowThemedRad : CornerRadius(size, round);
        float hx = size.x * 0.5f, hy = size.y * 0.5f;

        TVGlowColor.Shades(out var rim, out var near, out var wide); // Wii blue, the custom theme's accent, or the colour picked in Settings
        for (int y = 0; y < h; y++)
        {
            float py = ((y + 0.5f) * scale) - totalH * 0.5f;
            for (int x = 0; x < w; x++)
            {
                float px = ((x + 0.5f) * scale) - totalW * 0.5f;
                float qx = Mathf.Abs(px) - (hx - radius), qy = Mathf.Abs(py) - (hy - radius);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius; // <0 inside

                float aCore, aNear, aWide;
                if (d >= 0f)
                {
                    int i = Mathf.Min(255, (int) d);
                    float fade = 1f - Step(0f, 1f, d / (glowPad * 0.92f)); // reach zero before the bitmap ends: no visible box
                    aCore = lutCore[i];
                    aNear = lutNear[i] * fade;
                    aWide = lutWide[i] * fade;
                }
                else
                {
                    int i = Mathf.Min(255, (int) -d);
                    aCore = lutCore[i];            // the rim straddles the edge
                    aNear = lutInner[i];
                    aWide = 0f;
                }

                float a = 1f - (1f - aCore) * (1f - aNear) * (1f - aWide);
                float wsum = aCore + aNear + aWide + 1e-4f;
                var c = (rim * aCore + near * aNear + wide * aWide) / wsum;
                glowPixels[y * w + x] = new Color32((byte) (Mathf.Clamp01(c.x) * 255f), (byte) (Mathf.Clamp01(c.y) * 255f), (byte) (Mathf.Clamp01(c.z) * 255f), (byte) (Mathf.Clamp01(a) * 255f));
            }
        }
        if (glowTex.width != w || glowTex.height != h) glowTex.Reinitialize(w, h);
        glowTex.SetPixels32(glowPixels);
        glowTex.Apply(false);
    }

    // The click target's rect is often bigger than what you actually see (hit areas, shadows, hover-scaling parents).
    // Fit the glow to the visible pictures/text inside it instead.
    private Rect FitRect(RectTransform rt)
    {
        Rect full = ScreenRect(rt, corners);
        // round bar buttons: a circle of light around the button face itself (their clickable box is a square pocket)
        var face = RoundFace(rt.gameObject);
        if (face != null)
        {
            Rect fr = ScreenRect(face, corners);
            float side = Mathf.Min(fr.width, fr.height);
            if (side > 8f) return new Rect(fr.center.x - side * 0.5f, fr.center.y - side * 0.5f, side, side);
        }
        // page arrows: a steady round light centred on the arrow (their hit box and bobbing art would otherwise warp it)
        var arrow = rt.GetComponentInParent<ArrowController>();
        if (arrow != null && arrow.innerHolder != null)
        {
            Rect a = ScreenRect(arrow.innerHolder, corners);
            float side = Mathf.Min(a.width, a.height) * 0.92f;
            if (side > 8f) return new Rect(a.center.x - side * 0.5f, a.center.y - side * 0.5f, side, side);
        }
        // a row of the Settings animation / theme lists: the light wraps the whole row. The Content Hub's rows have a grey face (the
        // light hugged it) but your own rows are see-through, so the light shrank onto their text and looked smaller (2026-11-13)
        if (rt.Find("EditHolder") != null && rt.Find("Text") != null) return full;
        // a button that has its own visible picture IS the shape: glow around that, not around whatever sits inside it
        var own = rt.GetComponent<Graphic>();
        if (own != null && own.enabled && own.color.a > 0.3f && (own is Image oi ? oi.sprite != null : true) && !(own is TMPro.TMP_Text)) return full;
        if (fitGraphics == null || Time.unscaledTime - fitTime > 0.6f)
        {
            fitTime = Time.unscaledTime;
            fitGraphics = new List<Graphic>();
            foreach (var g in rt.GetComponentsInChildren<Graphic>(false))
            {
                if (g.canvasRenderer.cull || g.color.a < 0.2f || !g.enabled) continue;
                if (g is Image im && im.sprite == null && im.color.a < 0.5f) continue;
                if (g is TMPro.TMP_Text t && string.IsNullOrWhiteSpace(t.text)) continue;
                fitGraphics.Add(g);
            }
        }
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        float fullArea = full.width * full.height;
        int used = 0;
        foreach (var g in fitGraphics)
        {
            if (g == null) continue;
            Rect gr = ScreenRect(g.rectTransform, corners);
            float area = gr.width * gr.height;
            if (area > fullArea * 1.12f || area < 16f) continue; // shadows / decoration bigger than the target
            minX = Mathf.Min(minX, gr.xMin); minY = Mathf.Min(minY, gr.yMin);
            maxX = Mathf.Max(maxX, gr.xMax); maxY = Mathf.Max(maxY, gr.yMax);
            used++;
        }
        if (used == 0) return full;
        var fit = Rect.MinMaxRect(minX, minY, maxX, maxY);
        if (fit.width * fit.height < fullArea * 0.35f) return full;
        return fit;
    }

    private float tileSettleAt = -1f, normalTileH; private bool releasedForTransition;
    // a tile's Edit / Move / Delete buttons are small and sit on the tile's picture, where the light around them didn't show
    // (custom themes especially): the chosen one is also drawn bigger and tinted, so it's always clear which one OK presses
    // A theme's own button pictures can hide a tint, so the chosen one also gets a thick bright edge and the other two are dimmed.
    private Transform litOption; private Vector3 litScale; private Color litColor; private Image litImg; private Outline litEdge;
    private Color litEdgeColor; private bool litPulse;
    private readonly List<(Graphic g, Color c)> optionDimmed = new List<(Graphic, Color)>();
    private void LightOption(GameObject go)
    {
        var t = go != null ? go.transform : null;
        if (t == litOption) return;
        if (litOption != null) { litOption.localScale = litScale; if (litImg != null) litImg.color = litColor; }
        if (litEdge != null) Destroy(litEdge);
        foreach (var d in optionDimmed) if (d.g != null) d.g.color = d.c;
        optionDimmed.Clear();
        litOption = t; litImg = null; litEdge = null;
        if (t == null) return;
        litScale = t.localScale; t.localScale = litScale * 1.15f;
        litImg = t.GetComponent<Image>();
        if (litImg != null)
        {
            litColor = litImg.color;
            litEdge = litImg.gameObject.AddComponent<Outline>();
            litEdge.effectDistance = new Vector2(5f, -5f); litEdge.useGraphicAlpha = false;
            if (TVGlass.On) { litImg.color = new Color(0.55f, 0.82f, 1f, litColor.a); litEdgeColor = new Color(0.35f, 0.85f, 1f, 1f); litEdge.effectColor = litEdgeColor; litPulse = true; } // 2026's own cyan (it was yellow)
            else
            {
                // the hover light's own colour (Settings > Hover light color), breathing like the light does
                TVGlowColor.Shades(out var rimL, out var nearL, out _);
                litImg.color = new Color(Mathf.Lerp(1f, nearL.x, 0.45f), Mathf.Lerp(1f, nearL.y, 0.45f), Mathf.Lerp(1f, nearL.z, 0.45f), litColor.a);
                litEdgeColor = new Color(nearL.x, nearL.y, nearL.z, 1f); litEdge.effectColor = litEdgeColor; litPulse = true;
            }
        }
        var menu = OptionsMenu != null ? OptionsMenu.transform : t.parent;
        foreach (var s in menu.GetComponentsInChildren<Selectable>(false))
        {
            if (s.transform == t || s.transform.IsChildOf(t)) continue;
            foreach (var g in s.GetComponentsInChildren<Graphic>(false))
            {
                optionDimmed.Add((g, g.color));
                g.color = new Color(g.color.r * 0.5f, g.color.g * 0.5f, g.color.b * 0.5f, g.color.a);
            }
        }
    }

    // Settings: coming back to the main page (Back from Colors & Looks, say) lands on the button that was opened, not the nearest one
    // (the main page stays switched on underneath a sub-page, so "came back" = the light was on a sub-page and is now on the
    // main page or nowhere, with no arrow pressed since)
    private GameObject settingsPickGo;
    private string settingsPick; private bool settingsInSub; private float settingsBackUntil; private int settingsMovesAtBack;
    private int moveCount;
    private void ReturnInSettings()
    {
        var sc = SettingsController._instance;
        bool viewOpen = sc != null && sc.settingsView != null && sc.settingsView.activeInHierarchy;
        Transform main = viewOpen ? sc.settingsView.transform.Find("Content/MainSettings") : null;
        if (main == null) { settingsInSub = false; settingsBackUntil = 0f; return; }
        bool mainOn = main.gameObject.activeInHierarchy;
        bool live = focus != null && focus.activeInHierarchy;
        bool inMain = mainOn && live && focus.transform.IsChildOf(main);
        bool inSub = live && !inMain && focus.transform.IsChildOf(sc.settingsView.transform) && !focus.name.ToLowerInvariant().Contains("back");
        if (inSub) { settingsInSub = true; settingsBackUntil = 0f; return; }
        if (settingsInSub && (inMain || !live)) { settingsInSub = false; settingsBackUntil = Time.unscaledTime + 1.5f; settingsMovesAtBack = moveCount; Debug.Log("[NAV] Settings: back from a sub-page, return to " + settingsPick); }
        if (Time.unscaledTime < settingsBackUntil && settingsPick != null && moveCount == settingsMovesAtBack)
        {
            if (inMain && focus == settingsPickGo) { settingsBackUntil = 0f; autoUntil = 0f; return; }
            if (!mainOn || pageBusy || AppController.IsTransitioning() || Time.frameCount % 2 != 0) return;
            foreach (var c in Gather(true)) if (c.go == settingsPickGo && c.go.transform.IsChildOf(main)) { autoUntil = 0f; SetFocus(c); settingsBackUntil = 0f; Debug.Log("[NAV] back on Settings' " + settingsPick); return; }
            return; // not on screen yet (the page is still coming back): keep the pick
        }
        if (inMain) { settingsPick = focus.name; settingsPickGo = focus; }
    }

    private void UpdateGlow()
    {
        if (!PointerMode) ReturnInSettings();
        LightOption(!PointerMode && InOptions && focus != null && focus.activeInHierarchy && focus.transform.IsChildOf(OptionsMenu.transform) ? focus : null);
        if (litEdge != null && litPulse)
        {
            float pz = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f);
            var ec = litEdgeColor; ec.a = 0.45f + 0.55f * pz; litEdge.effectColor = ec;
            float d = 4f + 3f * pz; litEdge.effectDistance = new Vector2(d, -d);
        }
        EnsureGlow();
        if (glowRect == null) return;

        bool show = !PointerMode && focus != null && focus.activeInHierarchy && TVMode.IsTV && !OnWarningScreen;
        // an empty slot on a custom theme shows only the theme's own hover ring, like its installed tiles (Revo's light showed
        // through the see-through slot as a second ring)
        if (show && EmptyAppHandler.ThemeRingOnly(focus)) show = false;
        // while a screen zooms in or out (a channel's preview closing back into its tile) no button is lit: its light and the
        // button's own glow used to shrink along with the preview, a small lit strip inside the tile
        if (show && !IsTile(focus) && AppController.IsTransitioning())
        {
            show = false;
            if (!releasedForTransition) { releasedForTransition = true; WiiButton.ReleaseOthers(null); }
        }
        else releasedForTransition = false;
        // a tile zooming back from its preview is still small for a few frames: the light waits until it has landed
        if (show && IsTile(focus))
        {
            var ccx = ChannelController._instance;
            if (AppController.IsTransitioning() || (ccx != null && ccx.currentChannel != null)) tileSettleAt = Time.unscaledTime;
            if (Time.unscaledTime - tileSettleAt < 0.15f) show = false;
        }
        if (focus != loggedFocus || show != loggedShow)
        {
            loggedFocus = focus; loggedShow = show;
            string path = ""; for (var t = focus != null ? focus.transform : null; t != null && path.Length < 160; t = t.parent) path = t.name + "/" + path;
            Debug.Log($"[NAV] focus={(focus != null ? focus.name : "none")} show={show} pointer={PointerMode} warning={OnWarningScreen} path={path}");
        }
        if (!show)
        {
            if (glowRect.gameObject.activeSelf) glowRect.gameObject.SetActive(false);
            glowWasShown = false;
            if (!PointerMode && focus != null && !focus.activeInHierarchy) focus = null;
            return;
        }

        // home tiles: the light sits just BEHIND the focused tile (only the halo shows around its edge, nothing covers the art)
        Transform tileRoot = InOptions && focus != null && focus.transform.IsChildOf(OptionsMenu.transform) ? null : TileRoot(focus); // a tile's menu buttons: lit on top, not behind the tile
        Transform wantParent = tileRoot != null ? tileRoot.parent : AppController._instance.overlayCanvas;
        if (wantParent != null && (glowRect.parent != wantParent || (tileRoot != null && glowRect.GetSiblingIndex() > tileRoot.GetSiblingIndex())))
        {
            glowRect.SetParent(wantParent, false);
            var le = glowRect.GetComponent<LayoutElement>() ?? glowRect.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            if (tileRoot != null) glowRect.SetSiblingIndex(Mathf.Max(0, tileRoot.GetSiblingIndex()));
            else if (InputController._instance != null && InputController._instance.cursorHolder != null) glowRect.SetSiblingIndex(InputController._instance.cursorHolder.GetSiblingIndex());
        }
        var rt = focus.transform as RectTransform;
        Rect r = FitRect(rt);
        if (IsTile(focus))
        {
            normalTileH = Mathf.Max(normalTileH * 0.998f, r.height);
            if (r.height < normalTileH * 0.85f) tileSettleAt = Time.unscaledTime;
            if (Time.unscaledTime - tileSettleAt < 0.12f)
            {
                if (glowRect.gameObject.activeSelf) glowRect.gameObject.SetActive(false);
                glowWasShown = false; return;
            }
        }
        float themedRad = 0f;
        if (TVThemeTiles.GlowRect(focus.transform, out Rect themed)) { r = themed; themedRad = TVThemeTiles.GlowRadius; } // a custom theme's tile shape
        if (Mathf.Abs(themedRad - builtThemedRad) > 0.01f) { glowShapeDirty = true; builtThemedRad = themedRad; }
        glowThemedRad = themedRad;
        lastFocusCenter = r.center;
        // a popup slides in after its button was focused: the click point follows the button, so the first OK press lands on it
        if (pointerOnCenter && !IsTile(focus) && Vector2.Distance(r.center, pointerAt) > 10f)
        {
            RInput.PlacePointer(r.center);
            pointerAt = r.center;
        }

        // rebuilding the bitmap costs a frame on the projector: only when the shape really changed (not every move, not during the lift)
        bool round = IsRoundButton(focus);
        if (Mathf.Abs(r.width - builtSize.x) > builtSize.x * 0.12f || Mathf.Abs(r.height - builtSize.y) > builtSize.y * 0.12f || round != builtRound) glowShapeDirty = true;
        string gk = TVGlowColor.Key(); if (gk != builtGlowKey) { builtGlowKey = gk; glowShapeDirty = true; }
        if (glowShapeDirty)
        {
            glowShapeDirty = false;
            builtSize = r.size; builtRound = round;
            BuildGlowTexture(r.size, round);
        }

        // glide: critically damped follow so moving between items feels like one fluid motion
        float dt = Time.unscaledDeltaTime;
        smCenter = r.center;
        smSize = r.size;
        Vector2 smear = Vector2.zero;
        if (glideOffset != Vector2.zero)
        {
            // the light eases across to the new item like a weighted drop of light: soft start, long soft landing
            glideOffset = Vector2.SmoothDamp(glideOffset, Vector2.zero, ref glideVel, (0.062f + 0.036f * Mathf.Clamp01(glideOffset.magnitude / (Screen.width * 0.5f))) * GlideScale(), Mathf.Infinity, dt);
            if (glideOffset.sqrMagnitude < 0.25f) glideOffset = Vector2.zero;
            // motion smear: the light stretches back toward where it came from, then tightens onto the new item
            smCenter += glideOffset * 0.5f;
            smear = new Vector2(Mathf.Abs(glideOffset.x), Mathf.Abs(glideOffset.y)) * 0.5f;
        }

        var parent = (RectTransform) glowRect.parent;
        var canvas = glowRect.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        float hw = smSize.x * 0.5f + glowPad + smear.x, hh = smSize.y * 0.5f + glowPad + smear.y;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(smCenter.x - hw, smCenter.y - hh), cam, out Vector3 w0);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(smCenter.x + hw, smCenter.y + hh), cam, out Vector3 w1);
        glowRect.position = (w0 + w1) * 0.5f;
        float unit = Mathf.Max(0.0001f, glowRect.lossyScale.x);
        glowRect.sizeDelta = new Vector2(Mathf.Abs(w1.x - w0.x) / unit, Mathf.Abs(w1.y - w0.y) / unit);

        // slow fade in/out, like the Wii's disc-slot light breathing
        breathe += dt;
        float s = 0.5f + 0.5f * Mathf.Sin(breathe * 2f * Mathf.PI * 0.6f - 1.2f);
        float alpha = Mathf.Lerp(TVGlass.On ? 0.85f : 0.7f, 1.0f, s * s * (3f - 2f * s)); // classic: never fades below 70%, so it shows on light themes (3DS)
        alpha *= (glideOffset != Vector2.zero ? 0.82f : 1f) * Step(0f, 1f, (Time.unscaledTime - focusTime) / (glideOffset != Vector2.zero ? 0.05f : 0.10f));
        glowImage.color = new Color(1f, 1f, 1f, alpha);

        if (!glowRect.gameObject.activeSelf) glowRect.gameObject.SetActive(true);
        glowWasShown = true;
    }
}
