using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EmptyAppHandler : ChannelHandler
{
    private RectTransform rectTransform;
    public Image screen;
    public RawImage lines;
    public Sprite[] textures;
    private List<Sprite> originalTextures;
    private int currentTexture = 0;

    public int gridNumber;
    public int position;

    public float screenDelay = 0.2f;
    public float linesDelay = 0.1f;
    public float linesValue = 0.01f;

    private Coroutine screenAnim;
    private Coroutine linesAnim;

    protected void Awake()
    {
        originalTextures = new List<Sprite>(textures);
        rectTransform = GetComponent<RectTransform>();
        Resize(GridController._instance.GetChannelSizeMultiplier());
    }

    // the hover ring the installed tiles show when chosen (the theme's own picture and colour) shows on a chosen empty slot too
    private bool hoverShown;
    public static bool ThemeRingOnly(GameObject f)
    {
        if (f == null || TVGlass.On) return false;
        var eh = f.GetComponentInParent<EmptyAppHandler>();
        if (eh == null || !eh.hoverShown || eh.MenuOpen) return false;
        var tc = ThemeController._instance;
        return tc != null && !tc.IsDefaultLook();
    }
    private void ThemeHover()
    {
        var gcH = GridController._instance;
        if (gcH != null && gcH.IsLiftingChannel()) return; // a tile being carried over this slot shows the frame itself (GridController)
        var f = TVNav.Focused;
        bool want = !TVGlass.On && f != null && f.activeInHierarchy && f.transform.IsChildOf(transform);
        if (want == hoverShown) return;
        hoverShown = want;
        // a theme can make its empty slots see-through (the 3DS one does): the ring is still drawn in full
        if (blueBorder != null) blueBorder.ignoreParentGroups = true;
        if (want)
        {
            var tcS = ThemeController._instance;
            if (TVNav.UserActive && tcS != null && !tcS.IsDefaultLook() && (TVNav.Focused == null || !MenuOpen)) { AudioController.PlaySoundEffect(AudioLibrary._instance.HOVER_BUTTON); Debug.Log("[Sound] empty slot hover"); }
            ShowBlueBorder();
            // over the slot's own picture (a theme's empty-slot art is drawn over the whole slot and hid the ring's corners)
            if (blueBorder != null) blueBorder.transform.SetAsLastSibling();
}
        else HideBlueBorder();
    }

    private float shownEmptyAlpha = 1f; private bool paneShown = true;
    private void LateUpdate()
    {
        // theme components re-apply the static's look, so hold the calm one every frame
        if (!TVMode.IsTV) return;
        if (glass == null) BuildGlass();
        // a theme with its own empty-slot picture shows only that picture (Revo's own static pane under it showed through its
        // see-through parts, so the slot looked doubled); every other look keeps the pane
        bool wantGlass = !themedStatic;
        // the pane stays (it is what the remote focuses and what holds the slot's menu): only its pictures are hidden
        if (glass != null && paneShown != wantGlass)
        {
            paneShown = wantGlass; lookSet = false;
            if (shadowRt != null) shadowRt.gameObject.SetActive(wantGlass && TVGlass.On);
            foreach (var bh in new Behaviour[] { linesImg, markTmp, glossImg, frostImg, depthImg, streakImg }) if (bh != null) bh.enabled = wantGlass;
            var fm = fillImg != null ? fillImg.GetComponent<Mask>() : null; if (fm != null) fm.showMaskGraphic = wantGlass;
        }
        ApplyLook();
        ThemeHover();
if (!paneShown && rimImg != null && rimImg.color.a != 0f) rimImg.color = new Color(1f, 1f, 1f, 0f); // still catches the remote's press
        // a custom theme can make its empty slots see-through (Channel Server > Theme)
        float ea = TVThemeExtras.EmptyAlpha();
        if (ea != shownEmptyAlpha) { shownEmptyAlpha = ea; var cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>(); cg.alpha = ea; }
        if (themedStatic)
        {
            // the theme's own picture for empty slots is laid over the whole slot (on top of the calm Rii static), so a theme's folders,
            // outlines or art show the way its author drew them
            if (themeEmpty == null)
            {
                // a rounded window (the same corners as the pane) with the theme's picture inside, so the empty slot matches the rounded filled tiles
                var mgo = new GameObject("ThemeEmptyMask", typeof(RectTransform), typeof(Image), typeof(Mask));
                mgo.transform.SetParent(transform, false);
                themeMask = mgo.GetComponent<Image>(); themeMask.sprite = TVFx.Rounded(true); themeMask.type = Image.Type.Sliced; themeMask.raycastTarget = false;
                mgo.GetComponent<Mask>().showMaskGraphic = false;
                var go = new GameObject("ThemeEmpty", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(mgo.transform, false);
                themeEmpty = go.GetComponent<Image>(); themeEmpty.raycastTarget = false; themeEmpty.preserveAspect = false;
                var irt = themeEmpty.rectTransform; irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            }
            // the theme's empty slots animate like the original: the pictures take turns every fraction of a second
            // every slot shows the same picture at the same moment, a few times a second: one canvas rebuild for the whole page, not one per slot
            if (themedFrames.Count > 1) themeFrame = (int) (Time.unscaledTime / 0.3f) % themedFrames.Count;
            var s0 = themedFrames.Count > 0 ? themedFrames[Mathf.Clamp(themeFrame, 0, themedFrames.Count - 1)] : null;
            if (s0 != null && themeEmpty.sprite != s0) themeEmpty.sprite = s0;
            themeEmpty.enabled = s0 != null;
            var trt = themeMask.rectTransform; themeMask.enabled = true;
            // the same size as the slot's own pane (what the theme's filled tiles also cover), centred: nothing of the built-in static peeks out above or below
            var gsz = ((RectTransform) glass).sizeDelta; if (gsz.sqrMagnitude < 4f) gsz = rectTransform.rect.size;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f); trt.pivot = new Vector2(0.5f, 0.5f); trt.anchoredPosition = Vector2.zero; trt.sizeDelta = gsz;
            // the theme's picture on top, except the chosen slot's ring, which stays over it
            bool ringTop = hoverShown && blueBorder != null;
            if (ringTop && blueBorder.transform.GetSiblingIndex() != transform.childCount - 1) blueBorder.transform.SetAsLastSibling();
            int maskAt = transform.childCount - (ringTop ? 2 : 1);
            if (trt.GetSiblingIndex() != maskAt) trt.SetSiblingIndex(maskAt);
            // the open "+ Add App" menu stays over the theme's picture and ring
            if (MenuOpen && menu.transform.parent == transform && menu.transform.GetSiblingIndex() != transform.childCount - 1) menu.transform.SetAsLastSibling();
            // no dark box behind "+ Add App" on a see-through themed slot (the theme paints it back, so it is switched off)
            var addT = MenuOpen ? menu.transform.Find("Buttons/Add") : null;
            if (addT != null) foreach (var bi in menu.GetComponentsInChildren<Graphic>(false)) if (bi.enabled && !bi.transform.IsChildOf(addT)) bi.enabled = false;
        }
        else if (themeEmpty != null && themeEmpty.enabled) { themeEmpty.enabled = false; themeMask.enabled = false; }
        // Glass 2026 slots are clean glass panes (the reference has no grain); the classic slot keeps its static
        if (staticImg != null && staticImg.enabled != (!lookGlass && paneShown)) staticImg.enabled = !lookGlass && paneShown;
        // every slot steps its static on the same frame (a shared 18-a-second tick): the menu redraws once per step, not on every frame;
        // slots that are off the screen don't step at all
        int tick = Mathf.FloorToInt(Time.unscaledTime / 0.055f);
        bool stepNow = tick != lastTick; if (stepNow) lastTick = tick;
        if (Time.unscaledTime >= visCheckAt) { visCheckAt = Time.unscaledTime + 0.5f; onScreen = OnScreen(); }
        if (staticImg != null && !lookGlass && stepNow && (onScreen || (GridController._instance != null && GridController._instance.isChangingGrid)))
        {
            nextStatic = Time.unscaledTime;
            float sa = lookGlass ? 0.36f + 0.04f * Mathf.Sin(Time.unscaledTime * 0.9f + staticPhase) : 0.85f + 0.10f * Mathf.Sin(Time.unscaledTime * 0.9f + staticPhase);
            staticImg.color = new Color(1f, 1f, 1f, sa);
            staticFrame = (staticFrame + UnityEngine.Random.Range(1, TVFx.NoiseFrames)) % TVFx.NoiseFrames; // never the same frame twice
            var uv = staticImg.uvRect; uv.x = UnityEngine.Random.value; uv.y = UnityEngine.Random.value; staticImg.uvRect = uv;
            staticImg.texture = TVFx.Noise(lookGlass)[staticFrame];
        }
        // every moving pane rebuilds the menu's canvas: only the page on screen animates
        var gc = GridController._instance;
        // the next/previous page's column peeking in at the screen edge animates too, so the menu never looks frozen at its edges
        bool shown = gc == null || gc.currentGrid == null || gc.currentGrid.number == gridNumber || gc.isChangingGrid || onScreen;
        if (linesImg != null && shown)
        {
            // the original slot's scan lines roll slowly down the glass, as on the Wii (Glass: a clearly moving band roll)
            // classic: the slow roll moves with the static's tick (same speed, no extra redraws); Glass keeps its smooth per-frame roll
            if (lookGlass || stepNow) { var lr = linesImg.uvRect; lr.y += (lookGlass ? Time.unscaledDeltaTime * 0.85f : 0.055f * 0.04f); if (lr.y > 1f) lr.y -= 1f; linesImg.uvRect = lr; }
        }
        if (lookGlass && shown) Shimmer();
        if (Time.unscaledTime < nextSweep) return;
        nextSweep = Time.unscaledTime + 0.4f;
        // same frame as the channel art beside it
        var grt = (RectTransform) glass;
        var want = new Vector2(rectTransform.rect.width * TVFx.FrameScale.x, rectTransform.rect.height * TVFx.FrameScale.y);
        if (!lookGlass) want = new Vector2(rectTransform.rect.width, rectTransform.rect.height * 1.27f);
        if ((grt.sizeDelta - want).sqrMagnitude > 1f) grt.sizeDelta = want;
        if (shadowRt != null && (shadowRt.sizeDelta - (want + TVGlass.ShadowPad)).sqrMagnitude > 1f) shadowRt.sizeDelta = want + TVGlass.ShadowPad;
        // Glass 2026 corners are drawn at the slot's on-screen aspect, the same frame as the channel tiles
        foreach (var im in new[] { rimImg, fillImg })
        {
            if (im == null) continue;
            var sp = lookGlass ? TVFx.Frame(TVFx.ScreenAspect(grt, want), 0f, 0f, false) : (TVMode.IsTV ? TVFx.Rounded(true) : Rounded());
            // Glass 2026: the rim is the same glass bezel the channel tiles wear, reaching RimPad outside the pane
            if (lookGlass && im == rimImg) sp = TVFx.Frame(TVFx.ScreenAspect(grt, want), TVGlass.RimPad / want.x, TVGlass.RimPad / want.y, true);
            if (im.sprite != sp) { im.sprite = sp; im.type = lookGlass ? Image.Type.Simple : Image.Type.Sliced; }
        }
        if (rimImg != null)
        {
            float o = lookGlass ? TVGlass.RimPad : 0f;
            rimImg.rectTransform.offsetMin = new Vector2(-o, -o); rimImg.rectTransform.offsetMax = new Vector2(o, o);
        }
        if (fillImg != null)
        {
            float i = lookGlass ? 1.5f : 2.5f; // Glass: the rim's thin cyan line shows just around the pane
            fillImg.rectTransform.offsetMin = new Vector2(i, i); fillImg.rectTransform.offsetMax = new Vector2(-i, -i);
        }
        // the animated static belongs to the phone build: keep every original layer off
        if (graphicsCache == null || Time.unscaledTime >= graphicsAt) { graphicsCache = GetComponentsInChildren<Graphic>(true); graphicsAt = Time.unscaledTime + 4f; }
        foreach (var g in graphicsCache)
        {
            if (g == null) continue;
            if (g.transform == glass || g.transform.IsChildOf(glass) || g.transform == shadowRt || (menu != null && g.transform.IsChildOf(menu.transform))) continue;
            // the hover ring (the theme's own, as on the channels) is kept: shown by ThemeHover when the slot is chosen
            if (blueBorder != null && g.transform.IsChildOf(blueBorder.transform)) { if (!g.enabled) g.enabled = true; continue; }
            if (g.gameObject != gameObject && g != themeEmpty && g != themeMask && g.enabled) g.enabled = false;
        }
    }

    // Glass 2026: the empty pane is alive: a diagonal light glides across it every few seconds, one slot after the
    // next like a wave over the grid, and the glass gently brightens as it passes
    private const float SweepCycle = 5.5f, SweepRun = 1.5f;
    private bool sweepResting;
    private void Shimmer()
    {
        if (streakImg == null) return;
        float t = (Time.unscaledTime + (position % 4) * 0.22f + (position / 4) * 0.12f + gridNumber * 0.5f) % SweepCycle;
        float p = Mathf.Clamp01(t / SweepRun);
        // between sweeps the light rests off the pane: nothing to redraw
        if (p >= 1f && sweepResting) return;
        sweepResting = p >= 1f;
        float s = p * p * (3f - 2f * p);
        var srt = streakImg.rectTransform;
        float x = Mathf.Lerp(-0.25f, 1.25f, s);
        srt.anchorMin = srt.anchorMax = new Vector2(x, 0.5f);
        float k = Mathf.Sin(p * Mathf.PI);
        streakImg.color = new Color(1f, 1f, 1f, 0.06f + 0.30f * k);
        if (frostImg != null) frostImg.color = new Color(0.78f, 0.88f, 1f, 0.07f + 0.06f * k);
    }

    private Transform glass;
    private RawImage streakImg; private RectTransform shadowRt;
    private Image rimImg, fillImg; private TMPro.TMP_Text markTmp; private RawImage glossImg; private bool lookGlass; private bool lookSet;
    private float nextSweep;

    // the Wii's empty slot: a rounded pane of animated grey static with rolling scan lines; Glass 2026 lays
    // frosted silver glass over it (a light frost, gloss, streak, rim and shadow) and keeps the static clearly visible
    private RawImage staticImg, depthImg, linesImg, frostImg;

    private float nextStatic;
    private int staticFrame;
    private float staticPhase;

    private void BuildGlass()
    {
        var root = new GameObject("TV Glass", typeof(RectTransform));
        glass = root.transform;
        glass.SetParent(transform, false);
        var rt = (RectTransform) glass;
        var src = (RectTransform) transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(src.rect.width, src.rect.height * 1.27f); // the real tile art is taller than the slot rect
        rt.localScale = Vector3.one;
        var rim = Pane(rt, "Rim", 0f, new Color(0.60f, 0.62f, 0.67f, 0.9f), true);
        // a no-op button makes the slot focusable with the remote
        rimImg = rim.GetComponent<Image>();
        var btn = rim.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        // holding OK on the slot opens its menu, as it does on a channel (the phone's hold-and-setup flow is too long on a remote)
        // while a channel is being moved (TVMove), a press here places it in this slot
        var hold = rim.gameObject.AddComponent<HoldPane>();
        hold.onHold = () => { if (TVMove.TryDrop(gridNumber, position)) return; if (!MenuOpen) ToggleMenu(); };
        hold.onTap = () => TVMove.TryDrop(gridNumber, position);
        // the pane itself is the mask, so the static can never leave the rounded shape
        var fill = Pane(rt, "Fill", 2.5f, new Color(0.78f, 0.79f, 0.81f, 1f), false);
        fillImg = fill.GetComponent<Image>();
        var mask = fill.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var sgo = new GameObject("Static", typeof(RectTransform), typeof(RawImage));
        var srt = (RectTransform) sgo.transform;
        srt.SetParent(fill, false);
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = srt.offsetMax = Vector2.zero;
        staticImg = sgo.GetComponent<RawImage>();
        staticImg.raycastTarget = false;
        staticImg.color = new Color(1f, 1f, 1f, 0.6f);
        staticFrame = UnityEngine.Random.Range(0, TVFx.NoiseFrames);
        staticImg.texture = TVFx.Noise()[staticFrame];
        staticPhase = UnityEngine.Random.value * 6.28f;

        // the original slot's own scan-line texture (channel_lines.png from the prefab) over the static
        var lgo = new GameObject("Lines", typeof(RectTransform), typeof(RawImage));
        var lrt = (RectTransform) lgo.transform;
        lrt.SetParent(fill, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        linesImg = lgo.GetComponent<RawImage>(); linesImg.raycastTarget = false;
        linesImg.texture = lines != null ? lines.texture : null;
        if (linesImg.texture == null) { Destroy(lgo); linesImg = null; }
        // the frost: a thin milky layer that makes the static read as seen through glass
        var fgo = new GameObject("Frost", typeof(RectTransform), typeof(RawImage));
        var frt = (RectTransform) fgo.transform;
        frt.SetParent(fill, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
        frostImg = fgo.GetComponent<RawImage>(); frostImg.raycastTarget = false; frostImg.texture = Texture2D.whiteTexture;

        var tgo = new GameObject("Mark", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        var trt = (RectTransform) tgo.transform;
        trt.SetParent(fill, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 10f); trt.offsetMax = new Vector2(-10f, -10f);
        var tmp = tgo.GetComponent<TMPro.TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 10f; tmp.fontSizeMax = 160f;
        tmp.color = new Color(1f, 1f, 1f, 0.24f);
        tmp.raycastTarget = false;
        markTmp = tmp;

        // liquid-glass cover: a soft diagonal streak across the static
        var sk = new GameObject("Streak", typeof(RectTransform), typeof(RawImage));
        var skr = (RectTransform) sk.transform;
        skr.SetParent(fill, false);
        skr.anchorMin = skr.anchorMax = new Vector2(0.38f, 0.5f);
        skr.sizeDelta = new Vector2(rt.sizeDelta.x * 0.30f, rt.sizeDelta.y * 1.9f);
        skr.localRotation = Quaternion.Euler(0f, 0f, 24f);
        streakImg = sk.GetComponent<RawImage>();
        streakImg.texture = TVGlass.Band(); streakImg.raycastTarget = false;
        // depth: the glass darkens a little toward its lower edge
        var dgo = new GameObject("Depth", typeof(RectTransform), typeof(RawImage));
        var drt = (RectTransform) dgo.transform;
        drt.SetParent(fill, false);
        drt.anchorMin = Vector2.zero; drt.anchorMax = new Vector2(1f, 0.6f);
        drt.offsetMin = drt.offsetMax = Vector2.zero;
        drt.localScale = new Vector3(1f, -1f, 1f);
        depthImg = dgo.GetComponent<RawImage>();
        depthImg.texture = TVFx.GlossTex(); depthImg.raycastTarget = false;
        shadowRt = TVGlass.MakeShadow(transform, rt.sizeDelta);
        // soft glass sheen over the top, fading out
        var ggo = new GameObject("Gloss", typeof(RectTransform), typeof(RawImage));
        var grt = (RectTransform) ggo.transform;
        grt.SetParent(fill, false);
        grt.anchorMin = new Vector2(0f, 0.35f); grt.anchorMax = Vector2.one;
        grt.offsetMin = grt.offsetMax = Vector2.zero;
        var gi = ggo.GetComponent<RawImage>();
        glossImg = gi;
        gi.texture = TVFx.GlossTex(); gi.color = new Color(1f, 1f, 1f, 0.0f); gi.raycastTarget = false;
    }


    // classic grey pane, or the frosted-glass one when Glass 2026 is on
    private void ApplyLook()
    {
        bool g = TVGlass.On;
        if (lookSet && g == lookGlass) return;
        lookSet = true; lookGlass = g;
        if (rimImg == null || fillImg == null) return;
        rimImg.color = g ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.60f, 0.62f, 0.67f, 0.9f);
        // silver glass base under the static (the static itself carries the slot, so the base is nearly opaque)
        // Glass: a see-through periwinkle pane (the lit wall behind shows through, as in the reference) with soft rolling
        // horizontal bands
        // measured against the reference: clearer and bluer than the old periwinkle, which read cloudy grey
        fillImg.color = g ? new Color(0.56f, 0.67f, 0.90f, 0.54f) : new Color(0.78f, 0.79f, 0.81f, 1f);
        if (linesImg != null)
        {
            linesImg.color = new Color(1f, 1f, 1f, g ? 1f : 0.6f);
            linesImg.texture = g ? TVFx.ScanBands() : lines.texture;
            linesImg.uvRect = new Rect(0f, linesImg.uvRect.y, 1f, g ? 6f : 1f); // ~40 px soft bands on the pane
        }
        if (frostImg != null) frostImg.color = new Color(0.78f, 0.88f, 1f, g ? 0.07f : 0f); // a breath of icy glass, the grain stays crisp
        if (depthImg != null) depthImg.color = new Color(0.16f, 0.24f, 0.40f, g ? 0.22f : 0f);
        // classic Wii: the empty slot carries a faint "Rii" in its static; Glass 2026 stays clean
        if (markTmp != null) { markTmp.text = g ? "" : "Rii"; markTmp.color = new Color(1f, 1f, 1f, g ? 0f : 0.34f); }
        if (glossImg != null) glossImg.color = new Color(1f, 1f, 1f, g ? 0.26f : 0.12f);
        if (streakImg != null) streakImg.color = new Color(1f, 1f, 1f, g ? 0.12f : 0f);
        if (streakImg != null && !g) streakImg.rectTransform.anchorMin = streakImg.rectTransform.anchorMax = new Vector2(0.38f, 0.5f);
        if (shadowRt != null) shadowRt.gameObject.SetActive(g);
    }

    // TV: the slot's menu (held OK, or Menu), a copy of a channel's Edit / Delete panel with one button, Add App, that
    // puts an installed app in this slot
    private GameObject menu;
    public GameObject Menu => menu;
    public bool MenuOpen => menu != null && menu.activeSelf;
    public void ToggleMenu()
    {
        if (!MenuOpen && (AppController.IsTransitioning() || PopupController.IsPopupOpen())) return;
        if (menu == null) BuildMenu();
        if (menu == null) return;
        // a theme's own empty-slot picture is drawn over the whole slot and covered the "+ Add App" button: the menu then sits
        // on top of the slot (same place and size)
        if (themedStatic && menu.transform.parent != transform)
        {
            menu.transform.SetParent(transform, true);
            // its dark backing keeps the slot's rounded corners (the pane's mask did that before)
            var bk = menu.GetComponentInChildren<Image>(true);

            if (glass != null) { var mrt = (RectTransform) menu.transform; var gsz = ((RectTransform) glass).sizeDelta; if (gsz.sqrMagnitude > 4f) { mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f); mrt.anchoredPosition = Vector2.zero; mrt.sizeDelta = gsz; } }
        }
        menu.transform.SetAsLastSibling();
        menu.SetActive(!menu.activeSelf);
}

    private void BuildMenu()
    {
        var gc = GridController._instance;
        var src = gc != null && gc.appPrefab != null ? gc.appPrefab.transform.Find("Mask/AppOptions") : null;
        if (src == null || fillImg == null) return;
        // inside the pane, which clips it to the slot's rounded shape as a channel's own mask does
        menu = Instantiate(src.gameObject, fillImg.transform, false);
        menu.SetActive(false);
        // covering the whole pane, its button in the middle
        var rt = (RectTransform) menu.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Button add = null;
        foreach (var b in menu.GetComponentsInChildren<Button>(true))
        {
            if (b.name == "Edit" && add == null) add = b;
            else Destroy(b.gameObject);
        }
        if (add == null) { Destroy(menu); menu = null; return; }
        add.name = "Add";
        // drawn in full even on a theme whose empty slots are see-through (it was hard to see)
        var mcg = menu.GetComponent<CanvasGroup>() ?? menu.AddComponent<CanvasGroup>(); mcg.ignoreParentGroups = true;
        add.onClick = new Button.ButtonClickedEvent(); // the copy's own listener belongs to the prefab's channel
        add.onClick.AddListener(() => { menu.SetActive(false); TVQuickAdd.Open(gridNumber, position); });
        var art = (RectTransform) add.transform;
        art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f); art.anchoredPosition = Vector2.zero;
        foreach (var tt in add.GetComponentsInChildren<TranslatedText>(true)) DestroyImmediate(tt);
        var label = add.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (label != null) label.text = "+ Add App";
        var pencil = add.transform.Find("Elements/Image");
        if (pencil != null) pencil.gameObject.SetActive(false);
    }

    // a press held on the pane (OK on the remote) for as long as a channel needs for its menu
    private class HoldPane : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action onHold, onTap;
        private float down = -1f;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => down = Time.unscaledTime;
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
        {
            if (down >= 0f) onTap?.Invoke(); // let go before the hold time: a plain press
            down = -1f;
        }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => down = -1f;
        private void Update()
        {
            if (down < 0f || Time.unscaledTime - down < 0.6f) return;
            down = -1f;
            onHold?.Invoke();
        }
    }

    private static Sprite Rounded() { return TVFx.Rounded(); }

    private RectTransform Pane(RectTransform parent, string name, float inset, Color c, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform) go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        var im = go.GetComponent<Image>();
        im.sprite = TVMode.IsTV && !TVGlass.On ? TVFx.Rounded(true) : Rounded();
        im.type = Image.Type.Sliced;
        im.pixelsPerUnitMultiplier = 1f;
        im.color = c;
        im.raycastTarget = raycast;
        return rt;
    }

    void Start()
    {
        ThemeController._instance.onThemeReload.AddListener(ReloadTextures);
        ReloadTextures();
        RestartAnimation();
    }

    private void OnDestroy()
    {
        ThemeController._instance.onThemeReload.RemoveListener(ReloadTextures);
    }

    private void ReloadTextures()
    {
        for (int i = 0; i < originalTextures.Count; i++)
        {
            Sprite newSprite = ThemeController.GetTexture(originalTextures[i].texture) ?? originalTextures[i];
            textures[i] = newSprite;
        }
        bool th = false;
        if (TVMode.IsTV && ThemeController._instance != null && !ThemeController._instance.IsDefaultLook())
            for (int i = 0; i < originalTextures.Count && !th; i++) th = textures[i] != originalTextures[i];
        themedStatic = th;
        // only the theme's own pictures are shown (never the built-in static between them); one picture stays still, several take turns
        themedFrames.Clear();
        if (th) for (int i = 0; i < originalTextures.Count; i++) if (textures[i] != originalTextures[i] && !themedFrames.Contains(textures[i])) themedFrames.Add(textures[i]);
        themeFrame = 0;
    }

    private int lastTick = -1; private float visCheckAt; private bool onScreen = true; private Graphic[] graphicsCache; private float graphicsAt; private Canvas rootCanvas;
    private static readonly Vector3[] visCorners = new Vector3[4];
    // true while any part of the slot is inside the screen (the next page's first column peeks in at the right edge)
    private bool OnScreen()
    {
        if (rootCanvas == null) { var c = GetComponentInParent<Canvas>(); rootCanvas = c != null ? c.rootCanvas : null; }
        if (rootCanvas == null) return true;
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        rectTransform.GetWorldCorners(visCorners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, visCorners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, visCorners[2]);
        return Mathf.Max(a.x, b.x) > 0f && Mathf.Min(a.x, b.x) < Screen.width && Mathf.Max(a.y, b.y) > 0f && Mathf.Min(a.y, b.y) < Screen.height;
    }
    private bool themedStatic; private readonly List<Sprite> themedFrames = new List<Sprite>(); private Image themeEmpty, themeMask; private float nextThemeFrame; private int themeFrame;
    
    public void Resize(float size)
    {
        float gap = TVMode.IsTV && !TVGlass.On ? TVTune.Get("empty", 0.965f) : 1f; // empty slots are drawn edge to edge: a touch smaller leaves the gap the Wii has
        transform.localScale = new Vector3(size * TVMode.TileStretch.x * gap, size * TVMode.TileStretch.y * gap, 0);
    }

    public void RestartAnimation()
    {
        StopAnimations();
        screenAnim = StartCoroutine(StartScreenAnimation());
        linesAnim = StartCoroutine(StartScreenLines());
        if (TVMode.IsTV)
        {
            // the Wii's empty slots are calm, flat and faint: no animated static
            StopAnimations();
            if (screen != null)
            {
                var c = screen.color;
                c.a = 0.15f;
                screen.color = c;
            }
        }
    }

    public void StopAnimations()
    {
        try
        {
            if (screenAnim != null)
            {
                StopCoroutine(screenAnim);
            }

            if (linesAnim != null)
            {
                StopCoroutine(linesAnim);
            }
        }
        catch (Exception e)
        {
            // ignored
        }
    }

    private IEnumerator StartScreenAnimation()
    {
        while(true)
        {
            yield return new WaitForSeconds(screenDelay);
            currentTexture++;
            if (currentTexture >= textures.Length)
            {
                currentTexture = 0;
            }
            screen.sprite = textures[currentTexture];
        }
    }
    
    private IEnumerator StartScreenLines()
    {
        while(true)
        {
            yield return new WaitForSeconds(linesDelay);
            Rect currentRect = lines.uvRect;
            currentRect.y += Time.deltaTime * linesValue;
            lines.uvRect = currentRect;
        }
    }
}
