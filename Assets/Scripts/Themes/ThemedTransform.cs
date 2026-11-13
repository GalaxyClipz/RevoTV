using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class ThemedTransform : ThemedElement
{
    public ThemeOffsets category;
    public Vector3 originalPositionLocal; // gotta serialize it because the UI is fucking me over
    private RectTransform rectTransform;
    private Vector3 originalRotation;
    private Vector3 originalScale;

    protected override void FindElements()
    {
        if (rectTransform is not null)
        {
            return;
        }
        
        rectTransform = GetComponent<RectTransform>();

        if (rectTransform is not null)
        {
            originalRotation = rectTransform.localEulerAngles;
            originalScale = rectTransform.localScale;
        }
    }

    protected override void _UpdateElement()
    {
        FindElements();
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        ThemeTransform trans = ThemeController.GetThemeTransform(category);
        Vector3 offset = trans is null ? Vector3.zero : trans.offset;
        Vector3 scale = trans is null ? originalScale : trans.scale;
        Vector3 rotation = trans is null ? Vector3.zero : new Vector3(0,0,trans.rotation);
        if (trans is null || trans.position == ThemePositions.Neutral)
        {
            rectTransform.anchoredPosition = originalPositionLocal + offset;
        }
        else
        {
            rectTransform.position = GetOriginalPosition(trans) + offset;
            usedCanvas = AppController._instance.gameCanvas.rect.size;
            parentAt = rectTransform.parent != null ? rectTransform.parent.position : Vector3.zero;
        }
        rectTransform.localEulerAngles = originalRotation + rotation;
        rectTransform.localScale = scale;
        // rectTransform.ForceUpdateRectTransforms();
    }

    // placed from the screen's corners: the screen's size settles after the first placing at start-up (the theme refresh a few
    // seconds later then moved the Settings / SD buttons = the bar "jumped"), so it is placed again as soon as the size changes
    private Vector2 usedCanvas;
    // placed in screen terms, so it must follow when its parent moves: the bar was still sliding into place when the theme placed
    // Wii U's wrench / SD buttons, and they sat off until a theme refresh 4 s later moved them (the bar "jumped")
    private Vector3 parentAt;
    private void LateUpdate()
    {
        if (usedCanvas == Vector2.zero) return;
        // the clock's centred spot (TV2026) is kept too: without it the time sat at the theme's spot while the menu zoomed back
        // out of a channel and hopped ~10 px sideways when the zoom stopped
        if (rectTransform.parent != null && (rectTransform.parent.position - parentAt).sqrMagnitude > 0.0001f) { UpdatePosition(); TV2026.ReapplyClock(transform); return; }
        if ((Time.frameCount & 3) != 0) return;
        var app = AppController._instance;
        if (app == null || app.gameCanvas == null) return;
        if (app.gameCanvas.rect.size != usedCanvas) { usedCanvas = Vector2.zero; UpdatePosition(); TV2026.ReapplyClock(transform); }
    }

    private Vector3 GetOriginalPosition(ThemeTransform position)
    {
        Rect rect = AppController._instance.gameCanvas.rect;
        float width = rect.width/2;
        float height = rect.height/2;
        switch (position.position)
        {
            case ThemePositions.TopLeft: return new Vector3(-width, height);
            case ThemePositions.TopMid: return new Vector3(0, height);
            case ThemePositions.TopRight: return new Vector3(width, height);
            case ThemePositions.MidLeft: return new Vector3(-width, 0);
            case ThemePositions.MidRight: return new Vector3(width, 0);
            case ThemePositions.BottomLeft: return new Vector3(-width, -height);
            case ThemePositions.BottomMid: return new Vector3(0, -height);
            case ThemePositions.BottomRight: return new Vector3(width, -height);
            default: return new Vector3(0, 0);
        }
    }
}
