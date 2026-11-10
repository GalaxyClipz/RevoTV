using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class ArrowController : MonoBehaviour
{
    public bool shouldIncrease;
    private bool isHiding = true;

    public RectTransform rectTransform;
    public RectTransform channelHitboxTransform;
    public RectTransform innerHolder;
    public GameObject arrowImage;
    public GameObject signImage;
    public Image signImageFader;
    public Image signImageAccentFader;
    private Vector3 originalScale;

    private Tween animationLoop;

    public void Awake()
    {
        StartLoopAnimation();
        gameObject.SetActive(false);
        originalScale = arrowImage.transform.localScale;
        if (TVMode.IsTV)
        {
            // smooth the low-res arrow art when it is scaled up on a big screen
            foreach (var im in GetComponentsInChildren<Image>(true))
                if (im.sprite != null && im.sprite.texture != null) im.sprite.texture.filterMode = FilterMode.Bilinear;
        }
    }

    public void ClickedArrow()
    {
        if (GridController._instance.isChangingGrid || AppController.IsTransitioning() || !gameObject.activeInHierarchy || isHiding)
        {
            return;
        }

        if (ChannelController._instance.currentChannel == null)
        {
            // Here, we're clicking arrows from the Main
            int newGrid = GridController._instance.currentGrid.number;
            newGrid = shouldIncrease ? newGrid + 1 : newGrid - 1;
            GridController._instance.ChangeGrid(newGrid);
            AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
        
            RunHighlight(signImageFader);
            RunHighlight(signImageAccentFader);
        }
        else
        {
            // Here we click arrows from the channel view
            StartCoroutine(ChangeChannel());
        }
    }

    private void RunHighlight(Image image)
    {
        DOTween.Sequence()
            .Append(DOTween.ToAlpha(() => image.color, x => image.color = x, 0.9f, 0.067f))
            .AppendInterval(0.1f)
            .Append(DOTween.ToAlpha(() => image.color, x => image.color = x, 0f, 0.167f));
    }

    private IEnumerator ChangeChannel()
    {
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_PLUSLESS);
        yield return new WaitForSeconds(AudioLibrary._instance.CLICK_PLUSLESS.length/3);
        ChannelController._instance.ChangeChannel(shouldIncrease);
    }

    public void MouseEnter()
    {
        if (TVMode.IsTV) return;
        DOTween.Sequence()
            .Append(arrowImage.transform.DOScaleY(0.8f, 0.066f))
            .Append(arrowImage.transform.DOScaleY(1f, 0.05f))
            .Join(signImage.transform.DOScale(new Vector3(1f, 1f, 1f), 0.1f))
            .Append(signImage.transform.DOScaleX(1.15f, 0.05f))
            .Append(signImage.transform.DOScaleX(1f, 0.05f));
    }


    public void MouseLeave()
    {
        if (TVMode.IsTV) return;
        DOTween.Sequence()
            .Append(signImage.transform.DOScaleX(1.15f, 0.05f))
            .Append(arrowImage.transform.DOScaleY(0.8f, 0.05f))
            .Join(signImage.transform.DOScale(new Vector3(0f, 0f, 0f), 0.1f))
            .Append(arrowImage.transform.DOScaleY(1f, 0.05f));
    }

    private void ResetState()
    {
        animationLoop?.SetLoops(0);
        animationLoop?.Complete();
        animationLoop = null;
    }

    private void ResetSign()
    {
        signImage.transform.localScale = new Vector3(0, 0, 0);
        arrowImage.transform.localScale = originalScale;
    }

    public void Hide()
    {
        ResetState();
        isHiding = true;
        animationLoop = DOTween.Sequence()
            .Append(rectTransform.DOAnchorPosX((shouldIncrease ? 150 : -150) + (TVMode.IsTV ? TVLayout.ArrowOffset(this) : 0f), 0.25f))
            .AppendCallback(() =>
            {
                ResetSign();
                gameObject.SetActive(false);
            });
    }

    public void Show()
    {
        // on a TV the D-pad pages through the grid, so the home-menu arrows stay away
        if (TVMode.IsTV && GridController._instance != null && (this == GridController._instance.leftArrow || this == GridController._instance.rightArrow)) return;
        ResetState();
        isHiding = false;
        gameObject.SetActive(true);
        var t = rectTransform.DOAnchorPosX((shouldIncrease ? 8 : -8) + (TVMode.IsTV ? TVLayout.ArrowOffset(this) : 0f), 0.25f);
        if (TVMode.IsTV)
        {
            transform.SetAsLastSibling();
            t.OnComplete(KeepOnScreen);
        }
    }

    // the widened TV preview can push the arrows past the screen edge: pull them back in
    private void KeepOnScreen()
    {
        if (innerHolder == null) return;
        var canvas = innerHolder.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        Camera cam = canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        var cs = new Vector3[4];
        innerHolder.GetWorldCorners(cs);
        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float x = RectTransformUtility.WorldToScreenPoint(cam, cs[i]).x;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }
        float w = maxX - minX;
        if (w < 1f) return;
        float margin = Screen.width * 0.02f;
        float shiftPx = 0f;
        if (minX < margin) shiftPx = margin - minX;
        else if (maxX > Screen.width - margin) shiftPx = (Screen.width - margin) - maxX;
        if (Mathf.Abs(shiftPx) < 1f) return;
        float unitsPerPx = innerHolder.rect.width / w;
        rectTransform.anchoredPosition += new Vector2(shiftPx * unitsPerPx, 0f);
    }

    private void StartLoopAnimation()
    {
        if (TVMode.IsTV) return; // the focus light needs a steady target
        float value = shouldIncrease ? 12 : -12;
        DOTween.Sequence()
            .Append(innerHolder.DOAnchorPosX(value, 0.7f))
            .SetLoops(-1, LoopType.Yoyo);
    }
}
