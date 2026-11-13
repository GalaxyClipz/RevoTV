using System;
using System.Collections;
using System.Collections.Generic;
using Animations;
using DG.Tweening;
using TMPro;
using TriInspector;
using UnityEngine;
using UnityEngine.UI;

public class PreviewController : MonoBehaviour
{
    private GameObject currentDecoy;
    public GameObject mainDecoy;
    public GameObject decoyBackground;
    public RectTransform decoyContainer;
    public GameObject decoyGlobalOverlay;
    public GameObject decoyWhiteBottom;
    public GameObject decoyOverlay;
    public GameObject decoyOverlayThumbnail;
    public GameObject decoyOverlayThumbnailPreview;
    public GameObject decoyBanner;
    public GameObject decoyIcon;

    public AnimatedImage currentDebugImage;
    public TextMeshProUGUI maxDebugText;
    
    [Title("Thumbnail Corners")]
    public GameObject iconBottomCorner;
    public GameObject iconTopCorner;
    public GameObject bannerBottomCorner;
    public GameObject bannerTopCorner;

    public ChannelAnimation currentAnimation;
    public Texture2D currentThumbnail;
    private Tween currentTween;
    public static PreviewController _instance;
    public bool isOpen;

    private void Awake()
    {
        _instance = this;

        // we're gonna cheat badly for a sec, until we implement proper wide support
        bool ogBool = CameraController._instance.shouldDebugOrientation;
        ScreenOrientation ogOrientation = CameraController._instance.debugOrientation;
        CameraController._instance.shouldDebugOrientation = true;
        CameraController._instance.debugOrientation = ScreenOrientation.Portrait;
        float height = CameraController.GetFixedHeight();
        CameraController._instance.shouldDebugOrientation = ogBool;
        CameraController._instance.debugOrientation = ogOrientation;
        
        // now that we have a 100% portrait height, we can continue
        Vector3 size = new Vector3(1080f, height, 0);
        RectTransform rectTransform = mainDecoy.GetComponent<RectTransform>();
        rectTransform.localPosition = new Vector3(0, height, 0);
        rectTransform.sizeDelta = size;
        float sizeMultiplier = CameraController.GetChannelSize(1f);
        decoyContainer.localScale = new Vector3(sizeMultiplier, sizeMultiplier, 0f);
    }

    public void SetupDebugText(AnimatedImage image)
    {
        currentDebugImage = image;
        maxDebugText.gameObject.SetActive(true);
        maxDebugText.text = "MAX: (?)";
    }

    public void ShowDecoy(ChannelAnimation animation, bool showEditorOverlay = false, bool showThumbnailButtons = false, bool manuallyPlayAnimation = false)
    {
        isOpen = true;
        currentAnimation = animation;
        currentDecoy?.SetActive(false);
        currentDecoy = animation.type == CHANNELTYPE.ICON ? decoyIcon : decoyBanner;
        AnimationController._instance.FreezeChannelTimer();
        AudioController.MuteBackgroundAudio(true);
        mainDecoy.SetActive(true);
        currentDecoy.SetActive(true);
        decoyOverlay.SetActive(showEditorOverlay || showThumbnailButtons);
        decoyOverlayThumbnail.SetActive(showThumbnailButtons);

        currentTween?.Kill();
        TvListPreview = TVMode.IsTV && !CameraController.IsPortrait() && !showEditorOverlay && !showThumbnailButtons;
        TvLayout(TvListPreview);
        StartCoroutine(_ShowDecoy(animation, manuallyPlayAnimation));
    }

    // ---- TV: a plain preview (an animation held in a list) fills the TV screen like a channel opened from the home menu.
    // The panel is drawn for a phone (1080 x 1920): on a TV it came down as a tall black strip cut off at the top.
    public bool TvListPreview { get; private set; }
    private bool tvSaved, tvApplied;
    private Vector2 tvSize, tvBannerSize; private Vector3 tvScale;
    private RectTransform tvBanner;
    private readonly System.Collections.Generic.List<(RectTransform rt, Vector2 size)> tvBars = new System.Collections.Generic.List<(RectTransform, Vector2)>();

    private void TvLayout(bool on)
    {
        var rt = (RectTransform) mainDecoy.transform;
        if (!tvSaved)
        {
            tvSaved = true;
            tvSize = rt.sizeDelta; tvScale = rt.localScale;
            var bt = decoyBanner != null ? decoyBanner.transform.Find("ChannelObjects") : null;
            tvBanner = bt as RectTransform;
            if (tvBanner != null)
            {
                tvBannerSize = tvBanner.sizeDelta;
                foreach (var c in tvBanner.GetComponentsInChildren<RectTransform>(true)) if (c.name == "Black Bar") tvBars.Add((c, c.sizeDelta));
            }
        }
        if (on)
        {
            var parent = rt.parent as RectTransform;
            float pw = parent != null && parent.rect.width > 1f ? parent.rect.width : CameraController.GetFixedWidth();
            float ph = parent != null && parent.rect.height > 1f ? parent.rect.height : CameraController.GetFixedHeight();
            float k = ph / tvSize.y; // the phone panel's height becomes the screen's height
            rt.localScale = new Vector3(tvScale.x * k, tvScale.y * k, tvScale.z);
            rt.sizeDelta = new Vector2(pw / k, tvSize.y); // and it is as wide as the screen
            if (!tvApplied) rt.localPosition = new Vector3(0f, ph, rt.localPosition.z); // comes down from above the screen
            if (tvBanner != null) { tvBanner.sizeDelta = tvBannerSize; TVLayout.FitChannelView(tvBanner.gameObject, null, null); } // the banner widened like the home menu's
            tvApplied = true;
            Debug.Log("[Preview] TV layout " + Mathf.RoundToInt(pw) + "x" + Mathf.RoundToInt(ph) + " scale " + k.ToString("0.000"));
        }
        else if (tvApplied)
        {
            rt.sizeDelta = tvSize; rt.localScale = tvScale;
            if (tvBanner != null) tvBanner.sizeDelta = tvBannerSize;
            foreach (var b in tvBars) if (b.rt != null) b.rt.sizeDelta = b.size;
            tvApplied = false;
        }
    }

    public IEnumerator _ShowDecoy(ChannelAnimation animation, bool manuallyPlayAnimation = false)
    {
        // we wait until the animation is ready!
        yield return new WaitUntil(() => animation is null || animation.FinishedLoading());
        
        if (manuallyPlayAnimation)
        {
            currentTween = mainDecoy.transform.DOLocalMoveY(0f, TvListPreview ? 0.45f : 0.7f);
        }
        else
        {
            currentTween = DOTween.Sequence()
                .AppendCallback(() => AnimationController._instance.ChangeCurrentAnimation(animation))
                .Append(mainDecoy.transform.DOLocalMoveY(0f, TvListPreview ? 0.45f : 0.7f))
                .AppendCallback(() =>
                {
                    AnimationController._instance.RestartChannelAnimation(false);
                });
        }
    }

    public void HideDecoy(bool isEditor)
    {
        isOpen = false;
        currentDebugImage = null;
        maxDebugText.gameObject.SetActive(false);
        currentTween?.Kill();
        
        currentTween = DOTween.Sequence()
            .Append(mainDecoy.transform.DOLocalMoveY(TvListPreview ? HideY() : CameraController.GetFixedHeight(), TvListPreview ? 0.45f : 0.7f))
            .AppendCallback(() =>
            {
                currentDecoy.SetActive(false);
                mainDecoy.SetActive(false);
                currentAnimation = null;
                HideThumbnail();
                TvListPreview = false;
                if (!isEditor)
                {
                    AnimationController._instance.ChangeCurrentAnimation(null);
                }
            });
        AudioController.StopMainAudio();
        AudioController.UnmuteBackgroundAudio();
    }

    private float HideY() { var p = mainDecoy.transform.parent as RectTransform; return p != null && p.rect.height > 1f ? p.rect.height : CameraController.GetFixedHeight(); }

    public void ShowThumbnail(Texture2D thumbnail)
    {
        currentThumbnail = thumbnail;
        decoyOverlayThumbnailPreview.SetActive(true);
        RawImage rawImage = decoyOverlayThumbnailPreview.transform.Find("Image").GetComponent<RawImage>();
        rawImage.texture = thumbnail;
        rawImage.SetNativeSize();
    }

    public void HideThumbnail()
    {
        currentThumbnail = null;
        decoyOverlayThumbnailPreview.SetActive(false);
        decoyOverlayThumbnailPreview.transform.Find("Image").GetComponent<RawImage>().texture = null;
    }

    public void ClearPreview()
    {
        foreach (Transform child in (currentAnimation.type == CHANNELTYPE.ICON ?
            AnimationController._instance.iconDecoy.transform : AnimationController._instance.channelDecoy.transform)) {
            Destroy(child.gameObject);
        }
    }

    public GameObject GetCurrentDecoy()
    {
        return currentDecoy;
    }

    public GameObject GetBottomCorner(CHANNELTYPE channeltype)
    {
        return channeltype == CHANNELTYPE.BANNER ? bannerBottomCorner : iconBottomCorner;
    }
    
    public GameObject GetTopCorner(CHANNELTYPE channeltype)
    {
        return channeltype == CHANNELTYPE.BANNER ? bannerTopCorner : iconTopCorner;
    }
}
