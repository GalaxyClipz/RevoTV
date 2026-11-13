using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Data;
using DG.Tweening;
using TriInspector;
using UnityEngine;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class GridController : MonoBehaviour
{

    public static GridController _instance;

    [Title("Grid prefabs")]
    public GameObject appPrefab;
    public GameObject emptyAppPrefab;

    public int appsPerX;
    public int appsPerXHorizontal;
    public int appsPerY;
    public int appsPerYHorizontal;
    public GameObject gridHolder;
    public GameObject gridPrefab;
    public GameObject liftChannelPrefab;
    public Transform liftChannelHolder;

    private List<Grid> grids = new List<Grid>();
    [Title("Grid Settings")]
    public Grid currentGrid;
    public ScrollRect scrollRect;
    public ArrowController leftArrow;
    public ArrowController rightArrow;
    public bool isChangingGrid = false;
    public GameObject sdCard;
    public ThemedImage sdCardCover;

    [Title("Lifting Items")]
    public GameObject liftingItemsHolder;
    public Image deleteButtonImage;
    public Image editButtonImage;
    private RectTransform deleteButtonTransform;
    private RectTransform editButtonTransform;
    private bool isOverDelete;
    private bool isOverEdit;
    private bool isOverArrow;
    private ArrowController holdingArrow;
    private float arrowTimer = 0f;

    public Channel currentLiftingChannel = null;
    private GameObject liftingImage;

    private void Awake()
    {
        _instance = this;
        deleteButtonTransform = deleteButtonImage.gameObject.GetComponent<RectTransform>();
        editButtonTransform = editButtonImage.gameObject.GetComponent<RectTransform>();
        RefreshAppNumbers();
    }

    private void Start()
    {
        RefreshSdCard();
        RefreshGrids();
        if (TVMode.IsTV) StartCoroutine(WarmPages());
    }

    // TV: every page has its own bar (curve, time, date), switched on the first time the page is visited. That first switch-on
    // runs each themed piece's one-time setup (slow on a projector: 200-300 ms frames) and puts the clock at the theme's spot a
    // frame late, so the first page change after a fresh start stuttered and the time hopped sideways once. A moment after the
    // menu appears, the other pages' bars (off screen) are switched on for a second and put away again, so that's done early.
    private IEnumerator WarmPages()
    {
        var app = AppController._instance;
        yield return new WaitUntil(() => app != null && app.finishedLoading && app.mainView != null && app.mainView.activeInHierarchy
                                         && (app.warningView == null || !app.warningView.activeInHierarchy));
        yield return new WaitForSecondsRealtime(2.5f);
        while (isChangingGrid || currentGrid == null) yield return null;
        var warmed = new List<Grid>();
        // every page's clock sits on the same spot of the screen (it never slides): the warmed pages' clocks stay see-through, or one
        // of them showed over the real one for a frame (bigger, before its theme size) = the time "jumping" once after start-up
        var hush = new Dictionary<Grid, CanvasGroup>();
        foreach (var g in grids) if (g != null && g != currentGrid)
        {
            var cg = g.clockTransform != null ? (g.clockTransform.GetComponent<CanvasGroup>() ?? g.clockTransform.gameObject.AddComponent<CanvasGroup>()) : null;
            if (cg != null) { cg.alpha = 0f; hush[g] = cg; }
            g.EnableBackground(); warmed.Add(g);
        }
        if (warmed.Count == 0) yield break;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Hush() { foreach (var kv in hush) if (kv.Value != null) kv.Value.alpha = kv.Key == currentGrid && !isChangingGrid ? 1f : 0f; }
        for (float until = Time.unscaledTime + 2f; Time.unscaledTime < until;) { Hush(); yield return null; }
        while (isChangingGrid) { Hush(); yield return null; }
        foreach (var g in warmed) if (g != null && g != currentGrid) g.FixPositions(true);
        Hush(); yield return null;
        while (isChangingGrid) { Hush(); yield return null; }
        foreach (var g in warmed) if (g != null && g != currentGrid) g.DisableBackground();
        yield return null; yield return null; // the clocks switch off in their own next Update (Clock.shouldDisable)
        foreach (var kv in hush) if (kv.Value != null) kv.Value.alpha = 1f;
        Debug.Log("[Grid] warmed " + warmed.Count + " pages' bars in " + watch.ElapsedMilliseconds + " ms");
    }

    public void RefreshAppNumbers()
    {
        GRIDSIZE size = GetChosenGridSize();
        if (TVMode.IsTV && size != GRIDSIZE.CUSTOM)
        {
            SetAppNumbers(3, 4); // the Wii menu: 4 channels across, 3 down
        }
        else if (size != GRIDSIZE.CUSTOM)
        {
            int vertical = CalculateVerticalYApps(size);
            int horizontal = CalculateVerticalXApps(size);
            SetAppNumbers(horizontal, vertical);
        }
        else
        {
            // we load them from the preferences
            SetAppNumbersFromPrefs();
        }
    }

    private int GetDefaultAppsOnVerticalY()
    {
        // This formula is ancient from the code, no idea why I did it like this :/
        return CameraController.IsPortrait()
            ? (int) Math.Floor(CameraController.GetFixedHeight() / 435f)
            : (int) Math.Floor(CameraController.GetFixedWidth() / 435f);
    }

    public void SetAppNumbersFromPrefs()
    {
        bool vertical = !PREFS.AllowHorizontal.GetBool();
        int chosenX = vertical ? PREFS.NumberAppsX.GetInt() : PREFS.NumberAppsXHorizontal.GetInt();
        int chosenY = vertical ? PREFS.NumberAppsY.GetInt() : PREFS.NumberAppsYHorizontal.GetInt();
        SetAppNumbers(vertical ? chosenX : chosenY, vertical ? chosenY : chosenX);
    }

    public void SetAppNumbers(int verticalAppsPerX, int verticalAppsPerY)
    {
        appsPerX = verticalAppsPerX;
        PREFS.NumberAppsX.SetInt(verticalAppsPerX);
        appsPerYHorizontal = verticalAppsPerX;
        PREFS.NumberAppsYHorizontal.SetInt(verticalAppsPerX);
        appsPerY = verticalAppsPerY;
        PREFS.NumberAppsY.SetInt(verticalAppsPerY);
        appsPerXHorizontal = verticalAppsPerY;
        PREFS.NumberAppsXHorizontal.SetInt(verticalAppsPerY);
        
        RefreshGrids();
    }

    public GRIDSIZE GetChosenGridSize()
    {
        return Enum.Parse<GRIDSIZE>(PREFS.GridSize.GetString());
    }

    public void RefreshGridSizes()
    {
        //RefreshApps();
        foreach (Grid grid in grids)
        {
            grid.ResizeGrid();
        }
        // And we refresh the grid
        AppController._instance.gameCanvas.ForceUpdateRectTransforms();
        AppController._instance.overlayCanvas.ForceUpdateRectTransforms();
        StaticUtils.RefreshLayoutGroupsImmediateAndRecursive(gridHolder);
        if (currentGrid != null)
        {
            ChangeGrid(currentGrid.number, true, 0.1f);
        }
    }

    public void RefreshSdCard()
    {
        bool shown = PREFS.ShowSdCard.GetBool();
        sdCard.SetActive(shown);
        bool enabled = PREFS.SdCardColor.GetBool();
        SetSdCardColor(enabled);
        RectTransform dateTransform = TextController._instance.date.dateText.rectTransform;
        dateTransform.sizeDelta = new Vector2(shown ? 320 : 440, dateTransform.sizeDelta.y);
    }

    public void SetSdCardColor(bool colored)
    {
        sdCardCover.themeColor = colored ? ThemeColor.SdCardCover : ThemeColor.SdCardCoverDisabled;
        sdCardCover.UpdateElement();
        PREFS.SdCardColor.SetBool(colored);
    }

    public void RefreshClocks()
    {
        foreach (Grid grid in grids)
        {
            grid.FixPositions(true);
        }
    }

    [Button]
    public void RefreshGrids(bool goToFirstGrid = true)
    {
        // First we remove the extra grids
        List<Grid> removeList = new List<Grid>();
        for (int i = GetNumberOfGrids(); i < grids.Count; i++)
        {
            Grid grid = grids[i];
            Destroy(grid.gameObject);
            removeList.Add(grid);
        }
        removeList.ForEach(grid => grids.Remove(grid));

        // Then we add missing grids
        for (int i = grids.Count; i < GetNumberOfGrids(); i++)
        {
            GameObject obj = Instantiate(gridPrefab, gridHolder.transform);
            Grid grid = obj.AddComponent<Grid>();
            grid.number = i;
            grid.FindElements();
            grids.Add(grid);
        }
        
        // We refresh all the channel and grid sizes
        HandleEmptySpaces();
        RefreshGridSizes();
        if (goToFirstGrid)
        {
            ChangeGrid(0, true, 0f);
        }
        
        // And we also refresh the default channel viewer size
        ChannelController._instance.RefreshChannelObjectsSize();
    }

    public void ResetGridSize()
    {
        // used in case of the user abusing the system
        SetAppNumbers(2, GetDefaultAppsOnVerticalY());
    }

    public int GetNumberOfGrids()
    {
        return PREFS.NumberOfPages.GetInt();
    }

    public int GetAppsOnShortSide()
    {
        return CameraController.IsPortrait() ? appsPerX : appsPerYHorizontal;
    }

    public int GetAppsOnLongSide()
    {
        return CameraController.IsPortrait() ? appsPerY : appsPerXHorizontal;
    }

    public int GetAppsPerX()
    {
        // return 1; // animshowcase toremove
        return CameraController.IsPortrait() ? appsPerX : appsPerXHorizontal;
    }

    public int GetAppsPerY()
    {
        // return 1; // animshowcase toremove
        return CameraController.IsPortrait() ? appsPerY : appsPerYHorizontal;
    }

    public int GetMaxAppsNumber()
    {
        return GetAppsPerX() * GetAppsPerY();
    }

    public float GetChannelSizeMultiplier()
    {
        // we return the smallest, either on X or Y
        int shortApps = GetAppsOnShortSide();
        float horizontalSize = 2f / shortApps;
        
        int longApps = GetAppsOnLongSide();
        float verticalSize = GetDefaultAppsOnVerticalY() / (float) longApps;

        return Math.Min(horizontalSize, verticalSize);
    }

    public int CalculateVerticalYApps(GRIDSIZE size)
    {
        int defaultHeight = GetDefaultAppsOnVerticalY();
        switch (size)
        {
            case GRIDSIZE.BIG:
            {
                return defaultHeight;
            }
            case GRIDSIZE.MEDIUM:
            {
                return (int) (defaultHeight * 1.5f);
            }
            case GRIDSIZE.SMALL:
            {
                return defaultHeight * 2;
            }
        }
        return defaultHeight;
    }
    
    public int CalculateVerticalXApps(GRIDSIZE size)
    {
        switch (size)
        {
            case GRIDSIZE.BIG:
            {
                return 2;
            }
            case GRIDSIZE.MEDIUM:
            {
                return 3;
            }
            case GRIDSIZE.SMALL:
            {
                return 4;
            }
        }
        return 2;
    }

    // TV: the bar along the bottom (curve, time, date, buttons) must not move while the pages slide, whatever look or theme
    // draws it. At the end of the frame the slide starts in, the bar area of the screen is copied on the GPU (no read-back) into a
    // texture, and that picture is shown on top of the bar until the slide has settled. Works the same for every look and theme.
    private static RenderTexture barShot;
    private static UnityEngine.UI.RawImage barImg;
    private static int barHolds; // page changes in progress (a quick second one keeps the first picture)

    private static bool FreezeBar(Grid page)
    {
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null || page == null) return false;
        if (barHolds > 0 && barImg != null && barImg.enabled) { barHolds++; return true; }
        var canvas = page.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var cs = new UnityEngine.Vector3[4];
        // the band: from the top of the page's bar parts down to the screen's bottom, but never over the lowest tiles
        float top = 0f;
        foreach (var t in new[] { page.transform.Find("BottomClip"), page.backMask != null && page.backMask.activeInHierarchy ? page.backMask.transform : null })
        {
            if (!(t is RectTransform rt)) continue;
            rt.GetWorldCorners(cs);
            foreach (var c in cs) top = Mathf.Max(top, RectTransformUtility.WorldToScreenPoint(cam, c).y);
        }
        float tiles = float.MaxValue;
        foreach (var h in page.GetComponentsInChildren<ChannelHandler>(false))
        {
            ((RectTransform) h.transform).GetWorldCorners(cs);
            foreach (var c in cs) tiles = Mathf.Min(tiles, RectTransformUtility.WorldToScreenPoint(cam, c).y);
        }
        if (top <= 0f) top = Screen.height * 0.3f;
        if (tiles < float.MaxValue) top = Mathf.Min(top, tiles - 2f);
        top = Mathf.Clamp(top, 8f, Screen.height * 0.5f);

        if (barShot == null || barShot.width != Screen.width || barShot.height != Screen.height)
        {
            if (barShot != null) { barShot.Release(); Destroy(barShot); }
            barShot = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "Still Bar" };
        }
        ScreenCapture.CaptureScreenshotIntoRenderTexture(barShot);
        if (barImg == null)
        {
            var go = new GameObject("Still Bar", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            go.transform.SetParent(app.overlayCanvas, false);
            barImg = go.GetComponent<UnityEngine.UI.RawImage>();
            barImg.raycastTarget = false;
        }
        // just above the screen-wide scanlines/vignette (the picture already has them: under them they'd be applied twice and the bar
        // looked darker while the pages slid), still under the hover light and the pointer
        var fx = TVScreenFx.Root;
        if (fx != null && fx.parent == barImg.transform.parent) barImg.transform.SetSiblingIndex(fx.GetSiblingIndex() + (barImg.transform.GetSiblingIndex() < fx.GetSiblingIndex() ? 0 : 1));
        else barImg.transform.SetAsFirstSibling();
        float v = top / Screen.height;
        var irt = barImg.rectTransform;
        irt.anchorMin = Vector2.zero; irt.anchorMax = new Vector2(1f, v); irt.offsetMin = irt.offsetMax = Vector2.zero;
        barImg.texture = barShot;
        // on Vulkan/D3D-style platforms the copy is upside down
        barImg.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -v) : new Rect(0f, 0f, 1f, v);
        barImg.enabled = true;
        TVScreenFx.SweepOver(irt);
        barHolds = 1;
        TVVideoWall.HoldForSlide(true);
        return true;
    }

    /// <summary>TV: runs work (a theme refresh) behind a still picture of the bar, so its clock doesn't visibly jump while it resettles.</summary>
    public IEnumerator StillBarWhile(System.Action work, int frames)
    {
        UnityEngine.UI.Image sep = null; float sepA = 1f;
        bool fresh = barHolds == 0;
        if (fresh)
        {
            yield return null;
            sep = BarColon();
            if (sep != null) { var c0 = sep.color; sepA = c0.a; c0.a = 0f; sep.color = c0; }
        }
        yield return new WaitForEndOfFrame();
        bool frozen = TVMode.IsTV && currentGrid != null && FreezeBar(currentGrid);
        if (sep != null) { var c1 = sep.color; c1.a = sepA; sep.color = c1; if (frozen) LiveColon(sep); }
        try { work(); } catch (System.Exception e) { Debug.LogException(e); }
        for (int i = 0; i < frames; i++) yield return null;
        if (frozen) ThawBar();
    }

    private static void ThawBar()
    {
        barHolds = Mathf.Max(0, barHolds - 1);
        if (barHolds == 0 && barImg != null) { barImg.enabled = false; TVScreenFx.SweepOver(null); TVVideoWall.HoldForSlide(false); }
        if (barHolds == 0 && liveColon != null) { liveColon.enabled = false; colonSrc = null; }
    }

    // the clock's colon in the bar (classic and custom looks; 2026 draws its own clock)
    private static UnityEngine.UI.Image BarColon()
    {
        if (TVGlass.On || _instance == null) return null;
        // the pages' own clocks (looking through the whole scene for them cost ~30 ms on the frame the slide starts)
        foreach (var gr in _instance.grids)
        {
            var ck = gr != null && gr.clockTransform != null ? gr.clockTransform.GetComponentInChildren<Clock>(false) : null;
            if (ck == null) continue;
            var s = ck.imageSeparator;
            if (s == null || !s.isActiveAndEnabled) continue;
            var cv = s.canvas; Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
            var p = RectTransformUtility.WorldToScreenPoint(cam, s.rectTransform.position);
            if (p.y < Screen.height * 0.45f && p.x > 0 && p.x < Screen.width) return s;
        }
        return null;
    }

    private static UnityEngine.UI.Image liveColon, colonSrc;
    private static void LiveColon(UnityEngine.UI.Image src)
    {
        var app = AppController._instance; if (app == null || barImg == null) return;
        var parent = (RectTransform) barImg.transform.parent;
        if (liveColon == null)
        {
            var go = new GameObject("Still Bar Colon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            liveColon = go.GetComponent<UnityEngine.UI.Image>(); liveColon.raycastTarget = false;
        }
        liveColon.transform.SetSiblingIndex(barImg.transform.GetSiblingIndex() + 1);
        var cv = src.canvas; Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
        var pc = parent.GetComponentInParent<Canvas>(); Camera pcam = pc != null && pc.renderMode != RenderMode.ScreenSpaceOverlay ? pc.worldCamera : null;
        var cs = new Vector3[4]; src.rectTransform.GetWorldCorners(cs);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(cam, cs[0]), pcam, out var a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(cam, cs[2]), pcam, out var b);
        var rt = liveColon.rectTransform; rt.anchorMin = rt.anchorMax = parent.pivot; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (a + b) * 0.5f; rt.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        liveColon.sprite = src.sprite; liveColon.type = src.type; liveColon.preserveAspect = src.preserveAspect; liveColon.material = src.material;
        liveColon.color = src.color; liveColon.enabled = true; colonSrc = src;
    }

    // the slowest frame of a page change (a stall there looks like the clock's colon freezing): logged once per change
    private float slideUntil, slideWorst, slideStart, slideWorstAt;
    private static readonly System.Diagnostics.Stopwatch slideWatch = new System.Diagnostics.Stopwatch();
    private static string slideParts = "";
    private void LateUpdate()
    {
        if (Time.unscaledTime < slideUntil) { if (Time.unscaledDeltaTime > slideWorst) { slideWorst = Time.unscaledDeltaTime; slideWorstAt = Time.unscaledTime - slideStart; } }
        else if (slideWorst > 0f) { Debug.Log("[Grid] page change: slowest frame " + Mathf.RoundToInt(slideWorst * 1000f) + " ms at +" + Mathf.RoundToInt(slideWorstAt * 1000f) + " ms" + slideParts + " || slow frames" + TVFrameProbe.Report); slideWorst = 0f; slideParts = ""; TVFrameProbe.Report = ""; }
        // blinks with the clocks' shared timer, not with the clock it was copied from: during several page changes in a row that
        // page's clock is switched off when the first slide ends, and the colon stood still until the last one finished
        if (liveColon != null && liveColon.enabled) { var c = liveColon.color; c.a = Clock.BlinkAlpha(); liveColon.color = c; }
    }


    public void ChangeGrid(int grid, bool force = false, float duration = 0.5f)
    {
        if (!force && (grid < 0 || grid >= grids.Count || grid == currentGrid.number))
        {
            return;
        }

        StartCoroutine(_ChangeGrid(grid, duration));
    }

    private IEnumerator _ChangeGrid(int grid, float duration)
    {
        Debug.Log($"Changing to grid {grid} in {duration} seconds.");
        long p0 = TVFrameProbe.Begin();
        // First we clear the blue border of the handlers (if there's any)
        if (currentGrid is not null)
        {
            foreach (EmptyAppHandler handler in currentGrid.emptyChannels)
            {
                handler.isBeingHovered = false;
                handler.HideBlueBorder();
            }
            // installed channels too: their hover state used to stay behind and light up next to the real focus
            foreach (AppHandler ah in currentGrid.GetComponentsInChildren<AppHandler>(true)) ah.MouseLeave();
        }


        TVFrameProbe.End("leavehover", p0);
        // Then we proceed
        Grid previousGrid = currentGrid;
        currentGrid = grids[grid];
        isChangingGrid = true;

        float endValue;
        if (grids.Count <= 1)
        {
            endValue = 0;
        }
        else
        {
            endValue = (float)currentGrid.number / (grids.Count - 1);
        }

        // TV: the bar is shown as a still picture while the pages slide (see FreezeBar)
        bool frozen = false;
        slideUntil = Time.unscaledTime + duration + 0.6f; slideStart = Time.unscaledTime;
        TVFrameProbe.ProbeStart = slideStart; TVFrameProbe.ProbeUntil = slideUntil;
        if (TVMode.IsTV && duration > 0 && previousGrid != null)
        {
            UnityEngine.UI.Image sep = null; float sepA = 1f;
            if (barHolds == 0)
            {
                yield return null; // after this frame's animations, so nothing re-shows the colon before the picture
                sep = BarColon();
                if (sep != null) { var c0 = sep.color; sepA = c0.a; c0.a = 0f; sep.color = c0; }
                TVHoliday.HideForCapture(true);
                TVScreenFx.HideSweepForCapture(true);
            }
            yield return new WaitForEndOfFrame();
            slideWatch.Restart();
            frozen = FreezeBar(previousGrid);
            slideParts += " | capture " + slideWatch.ElapsedMilliseconds + " ms";
            TVHoliday.HideForCapture(false);
            TVScreenFx.HideSweepForCapture(false);
            if (sep != null) { var c1 = sep.color; c1.a = sepA; sep.color = c1; if (frozen) LiveColon(sep); }
        }

        long p1 = TVFrameProbe.Begin();
        if (duration > 0)
        {
            scrollRect.DOHorizontalNormalizedPos(endValue, duration).SetEase(TVMode.IsTV ? Ease.OutQuart : Ease.OutQuad);
        }
        else
        {
            scrollRect.horizontalNormalizedPosition = endValue;
        }
        
        // Now we have to lock the background of all the grids we're gonna go through
        if (previousGrid is not null)
        {
            foreach (Grid checkGrid in grids)
            {
                if (checkGrid != null && StaticUtils.IsWithinRange(checkGrid.number, previousGrid.number, currentGrid.number))
                {
                    checkGrid.EnableBackground();
                    if (checkGrid.isActiveAndEnabled)
                    {
                        checkGrid.LockImage(duration);
                    }
                }
            }
        }
        
        TVFrameProbe.End("slidestart", p1);
        yield return new WaitForSeconds(duration);

        // Here we check which arrows should be enabled
        if (currentGrid.number <= 0)
        {
            leftArrow.Hide();
        }
        else
        {
            leftArrow.Show();
        }

        if (currentGrid.number < grids.Count - 1)
        {
            rightArrow.Show();
        }
        else
        {
            rightArrow.Hide();
        }
        
        // And finally we clear the backgrounds (TV: only the pages the slide went over; the others are already put away)
        slideWatch.Restart();
        foreach (Grid checkGrid in grids)
        {
            if (TVMode.IsTV && previousGrid != null && checkGrid != null && checkGrid != currentGrid
                && !StaticUtils.IsWithinRange(checkGrid.number, previousGrid.number, currentGrid.number)) continue;
            checkGrid.FixPositions(!TVMode.IsTV || checkGrid == currentGrid);
            if (checkGrid.number != currentGrid.number)
            {
                checkGrid.DisableBackground();
            }
            else
            {
                checkGrid.EnableBackground();
            }
        }
        slideParts += " | end " + slideWatch.ElapsedMilliseconds + " ms";
        
        // only the last of several quick page changes shows the pages' own copies again
        if (frozen)
        {
            // the pages' own bars settle over the next frames (themes put their clocks back): the picture stays until they have
            isChangingGrid = false;
            // the new page's clock was asleep while its page was hidden (old minute, layout not rebuilt): set it now, under the
            // still picture, so the bar's time doesn't hop sideways when the picture goes
            if (currentGrid != null) foreach (var ck in currentGrid.GetComponentsInChildren<Clock>(true)) ck.UpdateHour(true);
            if (!TVGlass.On) TV2026.CenterTimeNow(); // centred under the still picture too, before the page's own copy shows
            for (int f = 0; f < 4; f++) yield return null;
            slideParts += " | thaw at +" + Mathf.RoundToInt((Time.unscaledTime - slideStart) * 1000f) + " ms";
            long tp = TVFrameProbe.Begin();
            ThawBar();
            TVFrameProbe.End("thaw", tp);
        }
        isChangingGrid = false;
    }

    public void HideArrows()
    {
        leftArrow.Hide();
        rightArrow.Hide();
    }

    public void ShowArrows()
    {
        if (currentGrid.number > 0)
        {
            leftArrow.Show();
        }

        if (currentGrid.number < grids.Count - 1)
        {
            rightArrow.Show();
        }
    }

    public bool InsertChannel(Channel channel)
    {
        // if it gets out of the app limits, we don't actually draw it
        if (channel.position >= GetMaxAppsNumber())
        {
            return true;
        }
        
        GameObject prefab = null;
        try
        {
            Transform grid = grids[channel.gridNumber].innerGrid;
            prefab = Instantiate(appPrefab, grid);
            int childIndex = channel.position;

            // Since things get weird when loading, we first swap their child index and then destroy it
            if (grid.childCount > childIndex)
            {
                DestroyEmptyChannel(channel.gridNumber, channel.position);
                prefab.transform.SetSiblingIndex(childIndex);
            }

            AppHandler drawer = prefab.GetComponent<AppHandler>();
            drawer.Draw(channel);
            // TV: a new tile starts at its home-grid size, not the prefab's (it used to show a wrong shape until the size check ran)
            if (TVMode.IsTV) drawer.transform.localScale = ChannelController.HomeTileScale();
            return true;
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
            if (prefab != null)
            {
                Destroy(prefab);
            }

            return false;
        }
    }

    private void HandleEmptySpaces()
    {
        int max = GetMaxAppsNumber();
        for (int gridNumber = 0; gridNumber < GetNumberOfGrids(); gridNumber++)
        {
            Transform grid = grids[gridNumber].innerGrid;
            int previousCount = grid.childCount;
            // first we add the missing empty ones
            for (int child = grid.childCount; child < max; child++)
            {
                int number = gridNumber;
                SpawnEmptyChannel(number, child);
            }
            
            // now we check if we were mising any channels - if so, we add them
            foreach (Channel channel in ChannelController._instance.loadedChannels)
            {
                if (channel.gridNumber == gridNumber && channel.position >= previousCount)
                {
                    InsertChannel(channel);
                    channel.LinkAnimations();
                }
            }
            
            // on the other hand, we remove the remaining channels if the size became smaller than previous one
            foreach (Channel channel in ChannelController._instance.loadedChannels)
            {
                if (channel.gridNumber == gridNumber && channel.position >= max && channel.appHandler != null)
                {
                    RemoveChannelFromGrid(channel.appHandler);
                }
            }
            
            // finally we remove extra empty ones if they exist
            for (int child = grid.childCount; child > max; child--)
            {
                int number = gridNumber;
                DestroyEmptyChannel(number, child-1);
            }
        }
    }

    public void LiftChannel(AppHandler appHandler)
    {
        if (AppController.IsTransitioning() || isChangingGrid || ChannelController._instance.currentChannel != null ||
            PopupController.IsPopupOpen())
        {
            return;
        }

        currentLiftingChannel = appHandler.channel;
        RemoveChannelFromGrid(appHandler);

        liftingImage = Instantiate(liftChannelPrefab, liftChannelHolder);
        liftingImage.transform.localPosition = CameraController._instance.GetMiddleCursor();
        //liftingImage.transform.SetSiblingIndex(AppController._instance.mainView.transform.childCount - 3);
        liftingImage.transform.localScale = Vector3.one * GetChannelSizeMultiplier();

        liftingItemsHolder.SetActive(true);
        DOTween.ToAlpha(() => deleteButtonImage.color, x => deleteButtonImage.color = x, 1f, 0.3f);
        DOTween.ToAlpha(() => editButtonImage.color, x => editButtonImage.color = x, 1f, 0.3f);
        ChannelController._instance.CoverChannels();
        PopupController.ShowBlocker();
    }

    public void RemoveChannelFromGrid(AppHandler appHandler)
    {
        // We unlink the channel animations so that they won't try to update themselves here
        appHandler.channel.UnlinkAnimations();
        
        // We remove the channel object
        Destroy(appHandler.gameObject);
        
        // And we spawn an empty channel in its place
        SpawnEmptyChannel(appHandler.channel.gridNumber, appHandler.channel.position);
    }

    public void DeleteChannel(AppHandler appHandler)
    {
        // First we remove from grid
        RemoveChannelFromGrid(appHandler);
        
        // Now we delete from files
        ChannelController._instance.DeleteChannel(appHandler.channel);
    }

    private void Update()
    {
        if (IsLiftingChannel())
        {
            bool shouldStop;
            if (TVMode.UseTouch)
            {
                shouldStop = RInput.touchCount == 0;
            }
            else
            {
                shouldStop = !RInput.GetMouseButton(0) && !RInput.GetMouseButton(1);
            }

            if (!shouldStop)
            {
                // If we're still holding the channel, we update the position and check for blue borders
                liftingImage.transform.localPosition = CameraController._instance.GetMiddleCursor();

                foreach (EmptyAppHandler handler in currentGrid.emptyChannels)
                {
                    // First we check for the channels
                    bool cursorIsOver = handler.ButtonContainsPosition(liftingImage.transform.localPosition);
                    if (!cursorIsOver && handler.isBeingHovered)
                    {
                        handler.isBeingHovered = false;
                        handler.HideBlueBorder();
                    }
                    else if (cursorIsOver && !handler.isBeingHovered)
                    {
                        handler.isBeingHovered = true;
                        handler.ShowBlueBorder();
                    }

                    // Then we check for the delete button
                    bool cursorDelete = StaticUtils.IsBeingHovered(deleteButtonTransform, liftingImage.transform.localPosition);
                    if (cursorDelete && !isOverDelete)
                    {
                        isOverDelete = true;
                        deleteButtonTransform.DOScale(new Vector3(1.75f, 1.75f, 1.75f), 0.25f);
                    }
                    else if (!cursorDelete && isOverDelete)
                    {
                        isOverDelete = false;
                        deleteButtonTransform.DOScale(new Vector3(1f, 1f, 1f), 0.25f);
                    }
                    
                    // Finally we check the edit button
                    bool cursorEdit = StaticUtils.IsBeingHovered(editButtonTransform, liftingImage.transform.localPosition);
                    if (cursorEdit && !isOverEdit)
                    {
                        isOverEdit = true;
                        editButtonTransform.DOScale(new Vector3(1.75f, 1.75f, 1.75f), 0.25f);
                    }
                    else if (!cursorEdit && isOverEdit)
                    {
                        isOverEdit = false;
                        editButtonTransform.DOScale(new Vector3(1f, 1f, 1f), 0.25f);
                    }
                }

                // Then we check for the arrows
                CheckChannelOverArrow();
            }
            else
            {
                LayDownChannel();
            }
        }
        else
        {
            // we check for the back button
            // the menu must have been on screen, still, for a moment: the Back that closes a channel's preview (or that comes
            // back from an app) only returns to the menu, at the tile you were on, never to page 1
            bool steady = !AppController.IsTransitioning() && !isChangingGrid && (ChannelController._instance == null || ChannelController._instance.currentChannel == null) && scrollRect.gameObject.activeInHierarchy && !PopupController.IsPopupOpen() && Application.isFocused;
            if (!steady) homeBusyAt = Time.unscaledTime;
            if (steady && Time.unscaledTime - homeBusyAt > 0.5f && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
                && !TVMove.ConsumesBack) { // Back that just cancelled a TV move
                // we move to the first grid
                AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
                ChangeGrid(0);
            }
        }
    }

    private float homeBusyAt;

    private void CheckChannelOverArrow()
    {
        if (arrowTimer > 1f)
        {
            holdingArrow.ClickedArrow();
            arrowTimer -= 0.5f;
        }

        if (isChangingGrid)
        {
            return;
        }

        Vector3 localPosition = liftingImage.transform.localPosition;
        bool overLeft = StaticUtils.IsBeingHovered(leftArrow.channelHitboxTransform, localPosition);
        bool overRight = StaticUtils.IsBeingHovered(rightArrow.channelHitboxTransform, localPosition);

        if (isOverArrow)
        {
            // Here we either keep the timer going, reset it to another arrow, or cancel it
            if (overLeft)
            {
                if (holdingArrow.Equals(leftArrow))
                {
                    arrowTimer += Time.deltaTime;
                }
                else
                {
                    holdingArrow.MouseLeave();
                    holdingArrow = leftArrow;
                    holdingArrow.MouseEnter();
                    arrowTimer = 0;
                }
            }
            else if (overRight)
            {
                if (holdingArrow.Equals(rightArrow))
                {
                    arrowTimer += Time.deltaTime;
                }
                else
                {
                    holdingArrow.MouseLeave();
                    holdingArrow = rightArrow;
                    holdingArrow.MouseEnter();
                    arrowTimer = 0;
                }
            }
            else
            {
                isOverArrow = false;
                holdingArrow.MouseLeave();
                holdingArrow = null;
                arrowTimer = 0;
            }
        }
        else
        {
            if (overLeft)
            {
                holdingArrow = leftArrow;
                isOverArrow = true;
                holdingArrow.MouseEnter();
            }
            else if (overRight)
            {
                holdingArrow = rightArrow;
                isOverArrow = true;
                holdingArrow.MouseEnter();
            }
            else
            {
                isOverArrow = false;
                holdingArrow = null;
                arrowTimer = 0;
            }
        }
    }

    private void LayDownChannel()
    {
        if (currentLiftingChannel == null)
        {
            return;
        }

        if (isOverDelete && !TVTiles.BlockDelete(currentLiftingChannel))
        {
            ChannelController._instance.DeleteChannel(currentLiftingChannel);
        }
        else
        {
            if (isOverEdit)
            {
                // we enter the edit menu
                SettingsController._instance.EditChannel(currentLiftingChannel);
            }
            else
            {
                // Now we check if there's any empty channel underneath. Otherwise we just reinsert the channel in its new position
                EmptyAppHandler emptyChannel = null;
                foreach (EmptyAppHandler handler in currentGrid.emptyChannels)
                {
                    if (handler.ButtonContainsPosition(liftingImage.transform.localPosition))
                    {
                        emptyChannel = handler;
                        break;
                    }
                }

                if (emptyChannel != null)
                {
                    currentLiftingChannel.position = emptyChannel.position;
                    currentLiftingChannel.gridNumber = emptyChannel.gridNumber;
                }
            }
            
            // We put the channels back to wherever they should be
            InsertChannel(currentLiftingChannel);
            currentLiftingChannel.LinkAnimations();
            currentLiftingChannel.Save();

            if (!isOverEdit)
            {
                AnimationController._instance.RestartIconAnimations();
            }
        }

        // We reset all the lifting channel status
        Destroy(liftingImage.gameObject);
        currentLiftingChannel = null;
        liftingImage = null;

        isOverDelete = false;
        deleteButtonTransform.DOScale(new Vector3(1f, 1f, 1f), 0.3f);
        editButtonTransform.DOScale(new Vector3(1f, 1f, 1f), 0.3f);
        DOTween.ToAlpha(() => deleteButtonImage.color, x => deleteButtonImage.color = x, 0f, 0.3f);
        DOTween.ToAlpha(() => editButtonImage.color, x => editButtonImage.color = x, 0f, 0.3f);

        ChannelController._instance.UncoverChannels();
        PopupController.CloseBlocker();
    }

    public void SpawnEmptyChannel(int gridNumber, int position)
    {
        Transform grid = grids[gridNumber].innerGrid;
        GameObject prefab = Instantiate(emptyAppPrefab, grid);
        int childIndex = position;
        prefab.transform.SetSiblingIndex(childIndex);
        prefab.transform.Find("Mask").Find("Click").GetComponent<LongClickButton>()
            .onLongClick.AddListener(() => ChannelController._instance.OnHoldEmptyChannel(gridNumber, position));
        EmptyAppHandler emptyAppHandler = prefab.GetComponent<EmptyAppHandler>();
        emptyAppHandler.gridNumber = gridNumber;
        emptyAppHandler.position = position;


        // Then we add it to our list of empty channels
        grids[gridNumber].emptyChannels.Add(emptyAppHandler);
    }

    public void DestroyEmptyChannel(int gridNumber, int pos)
    {
        Transform grid = grids[gridNumber].innerGrid;
        GameObject emptyChannel = grid.GetChild(pos).gameObject;
        emptyChannel.transform.SetAsLastSibling();

        EmptyAppHandler handler = emptyChannel.GetComponent<EmptyAppHandler>();
        if (handler != null)
        {
            handler.StopAnimations();
            grids[gridNumber].emptyChannels.Remove(handler);
            Destroy(emptyChannel.gameObject);
        }
    }

    public bool IsLiftingChannel()
    {
        return currentLiftingChannel != null;
    }

    public List<EmptyAppHandler> GetAllEmptyAppHandlers()
    {
        List<EmptyAppHandler> list = new List<EmptyAppHandler>();
        foreach (Grid grid in grids)
        {
            list.AddRange(grid.emptyChannels);
        }

        return list;
    }

}
