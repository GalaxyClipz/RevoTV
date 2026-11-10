using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI.Extensions;

public class WiiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public bool shouldGetBigger = true;
    public bool shouldOutline = false;
    public bool shouldPlayClick = true;
    public bool shouldPlayBack = false;
    public bool shouldPlayHover = true;
    private bool isBigger = false;
    private bool isOutline = false;
    private Tween scaleTween;
    private Tween outlineTween;
    public float multiplier = 1.175f;
    public Vector2 outlineSource = new Vector2(5, 5);
    public Vector2 outlineTarget = new Vector2(12, 12);
    public float outlineFillTime = 0.2f;
    private NicerOutline outline;

    private void Awake()
    {
        TryGetComponent(out outline);
    }

    private Vector3 CompleteAndSetScale()
    {
        var localScale = transform.localScale;
        Vector3 currentScale = new Vector3(localScale.x, localScale.y, localScale.z);
        scaleTween?.Complete();
        Vector3 originalScale = transform.localScale;
        transform.localScale = currentScale;
        return originalScale;
    }

    // only one button is hovered at a time: a button whose pointer-exit was missed (the TV's pointer jumps between
    // tiles, pages change under it) would otherwise keep its outline / size and show as a second highlight
    private static readonly List<WiiButton> hot = new List<WiiButton>();

    /// <summary>The remote moved the light to <paramref name="focus"/>: any button still hovered elsewhere lets go.</summary>
    public static void ReleaseOthers(Transform focus)
    {
        for (int i = hot.Count - 1; i >= 0; i--)
        {
            var h = hot[i];
            if (h == null) { hot.RemoveAt(i); continue; }
            if (focus != null && (focus.IsChildOf(h.transform) || h.transform.IsChildOf(focus))) continue;
            hot.RemoveAt(i);
            h.GetSmaller();
        }
    }

    private void OnDisable()
    {
        hot.Remove(this);
        if (isOutline)
        {
            isOutline = false;
            outlineTween?.Kill();
            if (outline != null) outline.effectDistance = outlineSource;
        }
        if (isBigger)
        {
            isBigger = false;
            scaleTween?.Kill();
            scaleTween = null;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        for (int i = hot.Count - 1; i >= 0; i--)
        {
            var h = hot[i];
            if (h == null) { hot.RemoveAt(i); continue; }
            if (h != this) { h.GetSmaller(); hot.RemoveAt(i); }
        }
        if (!hot.Contains(this)) hot.Add(this);
        if (shouldPlayHover)
        {
            if (!TVMode.IsTV || TVNav.UserActive) AudioController.PlaySoundEffect(AudioLibrary._instance.HOVER_BUTTON);
        }
        if (shouldGetBigger && !isBigger && !TVMode.IsTV) // on TV the glow marks the focus; growing buttons overlap their neighbours
        {
            isBigger = true;
            Vector3 localScale = CompleteAndSetScale();
            if (transform != null)
            {
                scaleTween = DOTween.Sequence()
                    .Append(transform.DOScale(new Vector3(localScale.x * multiplier, localScale.y * multiplier,
                        localScale.z * multiplier), 0.25f))
                    .AppendCallback(() => scaleTween = null);
            }
        }
        if (shouldOutline && !isOutline && !TVMode.IsTV) // on TV the remote's light marks the focus: a hover outline would be a second highlight
        {
            isOutline = true;
            outlineTween?.Complete();
            if (outline != null)
            {
                outlineTween = DOTween.Sequence()
                    .Append(DOTween.To(() => outline.effectDistance, x => outline.effectDistance = x, outlineTarget, outlineFillTime))
                    .AppendCallback(() => outlineTween = null);
            }
        }
        WiimoteController.RumbleMain(WiimoteController._instance.buttonRumbleTime);
    }


    public void OnPointerExit(PointerEventData eventData)
    {
        hot.Remove(this);
        GetSmaller();
    }

    private void GetSmaller()
    {
        if (shouldGetBigger && isBigger)
        {
            isBigger = false;
            Vector3 localScale = CompleteAndSetScale();
            if (transform != null)
            {
                scaleTween = DOTween.Sequence()
                    .Append(transform.DOScale(new Vector3(localScale.x / multiplier, localScale.y / multiplier,
                        localScale.z / multiplier), 0.25f))
                    .AppendCallback(() => scaleTween = null);
            }
        }
        if (shouldOutline && isOutline)
        {
            isOutline = false;
            outlineTween?.Complete();
            if (outline != null)
            {
                outlineTween = DOTween.Sequence()
                    .Append(DOTween.To(() => outline.effectDistance, x => outline.effectDistance = x, outlineSource, outlineFillTime))
                    .AppendCallback(() => outlineTween = null);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (shouldPlayClick)
        {
            if (shouldPlayBack)
            {
                AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON_BACK);
            }
            else
            {
                AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
            }
        }
        GetSmaller();
    }
}
