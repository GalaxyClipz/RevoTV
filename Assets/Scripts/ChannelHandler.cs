using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

public class ChannelHandler : MonoBehaviour
{
    public CanvasGroup blueBorder;
    protected Tween blueBorderTween;
    public RectTransform buttonRectTransform;
    
    public bool isBeingHovered = false;

    public void Resize(float size)
    {
        transform.localScale = new Vector3(size * TVMode.TileStretch.x, size * TVMode.TileStretch.y, 0);
    }

    public void ShowBlueBorder()
    {

        if (blueBorderTween != null && blueBorderTween.IsActive())
        {
            blueBorderTween.Kill();
            blueBorderTween = null;
        }

        Transform borderTransform = blueBorder.transform;
        borderTransform.SetAsFirstSibling();
        borderTransform.localScale = new Vector3(1, 1, 1);
        if (TVGlass.On)
        {
            blueBorder.alpha = 0f; // Glass 2026 marks the focus with its cyan glow and rim instead
            return;
        }
        if (TVMode.IsTV)
        {
            // fade in just behind the gliding light so the two read as one highlight
            blueBorder.alpha = 0f;
            blueBorderTween = blueBorder.DOFade(1f, 0.16f).SetDelay(0.1f).OnComplete(() => blueBorderTween = null);
            return;
        }
        blueBorder.alpha = 1f;
        
        WiimoteController.RumbleMain(WiimoteController._instance.buttonRumbleTime);
    }

    public void HideBlueBorder()
    {
        // a fade-in still running would finish after this and leave the frame lit on a tile the light has left
        if (blueBorderTween != null && blueBorderTween.IsActive()) { blueBorderTween.Kill(); blueBorderTween = null; }
        if (TVMode.IsTV)
        {
            // TV: the frame just fades where it is (moved over the picture and shrinking, a theme's filled hover frame swept across the art)
            blueBorderTween = DOTween.Sequence().Append(blueBorder.DOFade(0f, 0.1f)).AppendCallback(() => blueBorderTween = null);
            return;
        }
        blueBorder.transform.SetAsLastSibling();

        blueBorderTween = DOTween.Sequence()
            .Append(blueBorder.transform.DOScale(new Vector3(0.85f, 0.85f, 1f), TVMode.IsTV ? 0.18f : 1f))
            .Join(blueBorder.DOFade(0f, TVMode.IsTV ? 0.18f : 1f))
            .AppendCallback(() => blueBorderTween = null);
    }
    
    public bool ButtonContainsPosition(Vector2 xPos)
    {
        return StaticUtils.IsBeingHovered(buttonRectTransform, xPos);
    }
}
