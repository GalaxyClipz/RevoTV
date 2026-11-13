using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Grid : MonoBehaviour
{
    public List<EmptyAppHandler> emptyChannels = new List<EmptyAppHandler>();
    public int number;
    public RectTransform innerGrid;
    public GameObject backMask;
    public RawImage backgroundImage;
    public ThemedTransform clockTransform;
    public Clock clockScript;
    public GridLayoutGroup gridLayout;
    private Vector2 originalCellSize;
    private Coroutine lockedImage;
    private RectTransform rectTransform;
    private bool lockImage;
    private Vector2 basePos; private bool baseKnown;

    public void FindElements()
    {
        rectTransform = transform.GetComponent<RectTransform>();
        innerGrid = transform.Find("Grid").GetComponent<RectTransform>();
        gridLayout = innerGrid.GetComponent<GridLayoutGroup>();
        originalCellSize = gridLayout.cellSize;
        backMask = transform.Find("BackMask").gameObject;
        backgroundImage = backMask.transform.Find("Background").gameObject.GetComponent<RawImage>();
        clockTransform = transform.Find("BottomClip").Find("ClockHolder").GetComponent<ThemedTransform>();
        clockScript = transform.Find("BottomClip").Find("ClockHolder").Find("Clock").GetComponent<Clock>();
        backMask.SetActive(false);
        ResizeGrid();
    }

    private void Start()
    {
        FixPositions(true);
    }

    public void ResizeGrid()
    {
        rectTransform.sizeDelta = new Vector2(CameraController.GetFixedWidth(), CameraController.GetFixedHeight());
        //innerGrid.sizeDelta = new Vector2(CameraController.GetFixedWidth(), CameraController.GetFixedHeight());
        //innerGrid.offsetMax = new Vector2(-290, 0);
        innerGrid.localScale = CameraController.IsPortrait() ? Vector2.one : new Vector2(0.9f, 0.9f);
        gridLayout.constraintCount = GridController._instance.GetAppsPerX();

        // we setup the new app sizes
        float size = GridController._instance.GetChannelSizeMultiplier();
        if (TVMode.IsTV && !CameraController.IsPortrait())
        {
            // fill the area above the bottom bar like the Wii does
            float s = innerGrid.localScale.x;
            float availW = CameraController.GetFixedWidth() * 0.88f / s;
            float availH = (CameraController.GetFixedHeight() * 0.66f) / s;
            int cols = GridController._instance.GetAppsPerX(), rows = GridController._instance.GetAppsPerY();
            size = availH / (rows * originalCellSize.y);
            // Wii columns: 4 tiles of 22% of the screen width, 4.6% in from the left; the next page shows at the right edge
            float cellX = CameraController.GetFixedWidth() * 0.219f / s;
            TVMode.StretchX = cellX / (originalCellSize.x * size);
            if (!baseKnown) { basePos = innerGrid.anchoredPosition; baseKnown = true; }
            innerGrid.anchoredPosition = basePos + new Vector2(0f, -CameraController.GetFixedHeight() * (TVGlass.On ? 0.037f : 0.040f));
            float pageW = cols * cellX * s;
            rectTransform.sizeDelta = new Vector2(pageW, CameraController.GetFixedHeight());
            var lg = transform.parent != null ? transform.parent.GetComponent<HorizontalLayoutGroup>() : null;
            if (lg != null)
            {
                float fw = CameraController.GetFixedWidth();
                int padL = Mathf.RoundToInt(fw * (TVGlass.On ? 0.046f : 0.018f)); // classic sits a touch further left
                lg.padding = new RectOffset(padL, Mathf.Max(0, Mathf.RoundToInt(fw - padL - pageW)), lg.padding.top, lg.padding.bottom);
                lg.spacing = 0f;
                TVEdgePeek.Disabled = true;
                // the blue line along the top of the bar stretches with the page, so give it back the width the narrower page took away
                foreach (var tr in GetComponentsInChildren<RectTransform>(true))
                {
                    if (tr.name != "TopLine" || tr.anchorMin.x != 0f || tr.anchorMax.x != 1f) continue;
                    tr.sizeDelta = new Vector2(100f + (fw - pageW), tr.sizeDelta.y);
                    tr.anchoredPosition = new Vector2(fw * 0.5f - (padL + pageW * 0.5f), tr.anchoredPosition.y);
                }
            }
        }
        gridLayout.cellSize = new Vector2(originalCellSize.x * size * (TVMode.IsTV && !CameraController.IsPortrait() ? TVMode.StretchX : TVMode.TileStretch.x), originalCellSize.y * size);
        gridLayout.spacing = new Vector2();
        innerGrid.transform.GetComponentsInChildren<AppHandler>().ToList().ForEach(appHandler =>
        {
            appHandler.Resize(size);
            // classic TV: a filled tile has its own proportions (the same ones TVNav keeps them at); set at once, not a moment later, so a resize never shows a tile in the wrong shape
            if (TVMode.IsTV && !TVGlass.On && !CameraController.IsPortrait())
                appHandler.transform.localScale = Vector3.Scale(appHandler.transform.localScale, TVMode.TileFill);
        });
        innerGrid.transform.GetComponentsInChildren<EmptyAppHandler>().ToList().ForEach(appHandler => appHandler.Resize(size));
    }

    // after the page tween has moved this frame (LateUpdate), so the locked pieces never lag a frame behind
    private void LateUpdate()
    {
        if (lockImage)
        {
            FixPositions();
            // the curve, time and date don't ride along with the page: GridController shows one still copy while pages slide
        }
        else if (TVMode.IsTV && GridController._instance != null && GridController._instance.currentGrid == this && !GridController._instance.isChangingGrid)
            RememberClock();
    }

    // TV: every page carries its own copy of the time and date; while the pages slide they stay put on the screen
    // (the Wii's clock never moves), instead of riding along with their page
    private static Vector3 clockRest, dateRest; private static bool clockRestKnown, dateRestKnown;
    private Transform dateT; private bool dateLooked;
    private Transform DateT()
    {
        if (!dateLooked) { dateLooked = true; var bc = transform.Find("BottomClip"); if (bc != null) foreach (var t in bc.GetComponentsInChildren<Transform>(true)) if (t.name == "Date") { dateT = t; break; } }
        return dateT;
    }
    private void RememberClock()
    {
        if (clockTransform == null) return;
        clockRest = clockTransform.transform.position; clockRestKnown = true;
        var d = DateT(); if (d != null && !d.IsChildOf(clockTransform.transform)) { dateRest = d.position; dateRestKnown = true; }
    }
    private void HoldClock()
    {
        if (!TVMode.IsTV || clockTransform == null) return;
        if (clockRestKnown) clockTransform.transform.position = clockRest;
        var d = DateT(); if (dateRestKnown && d != null && !d.IsChildOf(clockTransform.transform)) d.position = dateRest;
    }

    public void EnableBackground()
    {
        if (TVGlass.On) TVGlass.KeepHidden(backMask); // Glass 2026 has its own backdrop; the page background would cover the fixed clock
        else if (TVMode.IsTV && TVTune.Get("nobackmask", 0f) > 0.5f) backMask.SetActive(false);
        else backMask.SetActive(true);
        clockTransform.gameObject.SetActive(true);
        clockScript.shouldDisable = false;
    }

    public void DisableBackground()
    {
        backMask.SetActive(false);
        clockScript.shouldDisable = true;
    }

    public void FixPositions(bool refreshClock = false)
    {
        backgroundImage.rectTransform.position = new Vector3();
        backgroundImage.rectTransform.sizeDelta = AppController._instance.gameCanvas.sizeDelta;
        
        clockTransform.UpdateElement();
        TV2026.ReapplyClock(clockTransform.transform);
        if (refreshClock)
        {
            clockScript.UpdateHour(true);
        }
    }

    public void LockImage(float duration)
    {
        lockImage = true;
        if (lockedImage != null)
        {
            StopCoroutine(lockedImage);
        }
        lockedImage = StartCoroutine(_LockImage(duration+0.1f));
    }

    private IEnumerator _LockImage(float duration)
    {
        yield return new WaitForSeconds(duration);
        lockImage = false;
    }
}