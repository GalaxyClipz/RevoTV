using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Serialization;
using Animations;
using Coffee.UISoftMask;
using DG.Tweening;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TriInspector;
using UnityEngine;
using UnityEngine.UI;

public class ChannelController : MonoBehaviour
{
    public Channel currentChannel;
    public static ChannelController _instance;
    private bool canSkipChannel = false;

    public GameObject channelView;
    public GameObject channelObjects;
    public Image blackBlackground;
    public Image blackBorder;
    public CanvasGroup canvasGroup;
    public ArrowController channelLeftArrow;
    public ArrowController channelRightArrow;

    [Title("Temp Control")]
    public bool changeBorderColor;
    
    private Vector3 defaultChannelScale = new Vector3(0.428f, 0.183f, 1f);

    public Dictionary<CHANNELTYPE, List<ChannelAnimation>> loadedAnimationsList = new Dictionary<CHANNELTYPE, List<ChannelAnimation>>();
    public List<ChannelAnimation> tempAnimations = new List<ChannelAnimation>();
    public List<Channel> loadedChannels = new List<Channel>();

    private void Awake()
    {
        _instance = this;
        loadedAnimationsList.Add(CHANNELTYPE.ICON, new List<ChannelAnimation>());
        loadedAnimationsList.Add(CHANNELTYPE.BANNER, new List<ChannelAnimation>());
    }

    private void Start()
    {
        RefreshChannelObjectsSize();
    }

    private void Update()
    {
        if(RInput.GetMouseButtonDown(0))
        {
            CheckSkipChannel();
        }
    }

    public void OnHoldEmptyChannel(int gridNumber, int position)
    {
        if (!SwipeController._instance.shouldCheckHoldEmptyChannel)
        {
            return;
        }
        
        PopupController.ShowPopup("popup.addchannel", () =>
            {
                Channel channel = new Channel()
                {
                    gridNumber = gridNumber,
                    position = position,
                    isFullySetup = false
                };
                PopupController.ClosePopup();
                SettingsController._instance.setupTypeSettingView.Show(channel);
            },
            PopupController.ClosePopup);
    }

    public void InitializeNewChannel(Channel channel)
    {
        channel.iconAnimation?.linkedChannels.Add(channel);
        channel.bannerAnimation?.linkedChannels.Add(channel);
        channel.CreateID();
        
        channel.Save();
        SetupChannel(channel);
    }

    public void OpenChannel(AppHandler channel)
    {
        if (AppController.IsTransitioning() || GridController._instance.isChangingGrid || PopupController.IsPopupOpen())
        {
            return;
        }

        canSkipChannel = true;
        StartCoroutine(_OpenChannel(channel));
    }

    private IEnumerator _OpenChannel(AppHandler appHandler)
    {
        bool shouldOpenDirectly = PREFS.OpenDirectly.GetBool();
        if (shouldOpenDirectly)
        {
            Debug.Log("Skipping channel!");
            AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_CHANNEL);
            appHandler.channel.Execute();
            canSkipChannel = false;
            yield break;
        }

        AppController.SetTransitioning(true);
        CameraController.SetAutorotationStatus(false); // we don't allow the camera to rotate anymore until we finish the anim
        currentChannel = appHandler.channel;

        yield return new WaitForSeconds(0.1f);
        
        GridController._instance.HideArrows();
        ChannelAnimation bannerAnimation = currentChannel.GetBannerAnimation();
        yield return new WaitUntil(() => bannerAnimation is null || bannerAnimation.FinishedLoading());
        // if canSkipChannel is false, we leave. we don't need this anymore
        if (!canSkipChannel)
        {
            bannerAnimation?.Unload();
            yield break;
        }
        
        AnimationController._instance.ChangeCurrentAnimation(bannerAnimation);

        channelView.SetActive(true);
        TVLayout.FitChannelView(channelObjects, channelLeftArrow, channelRightArrow);
        Vector2 center = currentChannel.appHandler.GetCenter();
        channelObjects.transform.position = new Vector3(center.x, center.y, 0);
        // on a TV the preview grows out of the tile itself (it starts exactly the tile's size), like the Wii: before, it started as a
        // bigger card than the tile while the tile under it was squeezed thin (the phone layout's portrait shape)
        if (TvZoom()) channelObjects.transform.localScale = TileFitScale();

        Ease defaultEase = Ease.InQuad;
        float duration = 0.55f;
        
        AudioController.MuteBackgroundAudio();
        
        StartCoroutine(PlayOpenSound());
        yield return new WaitForSeconds(0.15f);

        // if canSkipChannel is false, we leave. we don't need this anymore
        if (!canSkipChannel)
        {
            yield break;
        }
        canSkipChannel = false;
        
        StartCoroutine(ZoomProbe("in", duration));
        CameraController._instance.ZoomIn(currentChannel.appHandler,duration);
        float size = CameraController.GetChannelSize(0.18f) * GridController._instance.GetChannelSizeMultiplier();
        channelObjects.transform.DOScale(new Vector3(size, size, 1f),duration).SetEase(defaultEase);
        float newValue = 0.425f * GridController._instance.GetChannelSizeMultiplier();
        if (!TvZoom()) currentChannel.appHandler.transform.DOScaleX(newValue, duration).SetEase(defaultEase);

        DOTween.ToAlpha(() => blackBlackground.color, x => blackBlackground.color = x, 1, duration).SetEase(defaultEase);
        //DOTween.ToAlpha(() => blackBorder.color, x => blackBorder.color = x, 1, duration).SetEase(defaultEase);
        DOTween.To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, 1, duration).SetEase(defaultEase);

        AnimationController._instance.FreezeChannelTimer();
        currentChannel.bannerAnimation?.Stop();
        //AnimationController._instance.RestartChannelAnimation();
        
        yield return new WaitForSeconds(duration - 0.1f);
        AnimationController._instance.UnfreezeChannelTimer();
        currentChannel.bannerAnimation?.PlayAudio();
        channelLeftArrow.Show();
        channelRightArrow.Show();

        yield return new WaitForSeconds(0.1f);
        blackBorder.gameObject.SetActive(true);
        AppController.SetTransitioning(false);
    }

    // a channel's zoom in / out: how many frames it got and what the slow ones spent their time on (one log line each)
    private IEnumerator ZoomProbe(string what, float duration)
    {
        if (!TVMode.IsTV) yield break;
        float t0 = Time.unscaledTime; int f0 = Time.frameCount;
        TVFrameProbe.Report = ""; TVFrameProbe.ProbeStart = t0; TVFrameProbe.ProbeUntil = t0 + duration;
        yield return new WaitForSecondsRealtime(duration + 0.35f);
        float t = Time.unscaledTime - t0; int n = Time.frameCount - f0;
        Debug.Log("[Zoom] " + what + ": " + n + " frames in " + Mathf.RoundToInt(t * 1000f) + " ms (" + (n / t).ToString("0") + " fps) || slow frames" + TVFrameProbe.Report);
        TVFrameProbe.Report = "";
    }

    private void CheckSkipChannel()
    {
        if (currentChannel != null && canSkipChannel && TVMode.UseTouch && SwipeController._instance.shouldCheckDoubleClick)
        {
            Vector2 worldPos = CameraController._instance.GetWorldPosition(RInput.mousePosition);
            if (StaticUtils.IsInsideTransform(currentChannel.appHandler.rectTransform, worldPos, 5))
            {
                Debug.Log("Skipping channel!");
                canSkipChannel = false;
                StartCoroutine(SkipChannel());
            }
        }
    }

    private IEnumerator SkipChannel()
    {
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_CHANNEL);
        yield return new WaitForSeconds(0.2f);
        currentChannel.Execute();
        StartCoroutine(_CloseChannel(0f, false));
    }

    private IEnumerator PlayOpenSound()
    {
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_CHANNEL);
        yield return new WaitForSeconds(0.2f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.OPEN_CHANNEL);
    }

    public void ChangeChannel(bool higher)
    {
        if (currentChannel == null || AppController.IsTransitioning())
        {
            return;
        }
        
        AppController.SetTransitioning(true);

        // First we find the next channel
        Channel nextChannel;
        if (higher)
        {
            nextChannel = FindNextChannel(currentChannel.gridNumber, currentChannel.position);
            if (nextChannel == null)
            {
                nextChannel = FindNextChannel(0, 0);
            }
        }
        else
        {
            nextChannel = FindPreviousChannel(currentChannel.gridNumber, currentChannel.position);
            if (nextChannel == null)
            {
                nextChannel = FindPreviousChannel(GridController._instance.GetNumberOfGrids()-1, GetMaxChannelPos());
            }
        }

        // Then we open it
        float size = GridController._instance.GetChannelSizeMultiplier();
        currentChannel.appHandler.transform.localScale = HomeTileScale();

        lastHigher = higher;
        currentChannel = nextChannel;
        if (!TvZoom()) nextChannel.appHandler.transform.localScale = new Vector3(0.425f * size, size, size);
        StartCoroutine(_ChangeChannel(nextChannel));
    }

    private bool lastHigher;

    private IEnumerator _ChangeChannel(Channel nextChannel)
    {
        ChannelAnimation channelAnimation = nextChannel.GetBannerAnimation();
        yield return new WaitUntil(() => channelAnimation is null || channelAnimation.FinishedLoading());
        AnimationController._instance.ChangeCurrentAnimation(channelAnimation);
        
        GridController._instance.ChangeGrid(nextChannel.gridNumber, false, 0f);
        
        Vector2 center = nextChannel.appHandler.GetCenter();
        channelObjects.transform.position = new Vector3(center.x, center.y, 0);
        CameraController._instance.transform.position = new Vector3(center.x, center.y, -180f);
        AnimationController._instance.RestartChannelAnimation();
        AppController.SetTransitioning(false);

        if (TVMode.IsTV)
        {
            // Wii-style: the next channel glides in from the side
            var t = channelObjects.transform;
            float w = ((RectTransform) t).rect.width * t.lossyScale.x * 0.6f;
            t.position = new Vector3(center.x + (lastHigher ? w : -w), center.y, 0);
            DG.Tweening.ShortcutExtensions.DOMoveX(t, center.x, 0.26f).SetEase(DG.Tweening.Ease.OutQuart);
        }
    }

    private Channel FindNextChannel(int initialGrid, int position)
    {
        for (int grid = initialGrid; grid < GridController._instance.GetNumberOfGrids(); grid++)
        {
            /*int initialY = grid == initialGrid ? position / GridController._instance.appsPerX : 0;

            for (int y = initialY; y <= GetMaxChannelY(); y++)
            {
                int initialX = y == initialY && grid == initialGrid ? channelX : 0;
                for (int x = initialX; x <= GetMaxChannelX(); x++)
                {
                    Channel channel = FindChannel(grid, x, y);
                    if (channel != null && !channel.Equals(currentChannel))
                    {
                        return channel;
                    }
                }
            }*/

            int initialPos = grid == initialGrid ? position : 0;

            for (int pos = initialPos; pos <= GetMaxChannelPos(); pos++)
            {
                Channel channel = FindChannel(grid, pos);
                if (channel != null && !channel.Equals(currentChannel))
                {
                    return channel;
                }
            }
        }

        return null;
    }

    private Channel FindPreviousChannel(int initialGrid, int position)
    {
        for (int grid = initialGrid; grid >= 0; grid--)
        {
            int initialPos = grid == initialGrid ? position : GetMaxChannelPos();

            for (int pos = initialPos; pos >= 0; pos--)
            {
                Channel channel = FindChannel(grid, pos);
                if (channel != null && !channel.Equals(currentChannel))
                {
                    return channel;
                }
            }
        }

        return null;
    }

    private int GetMaxChannelPos()
    {
        return (GridController._instance.GetAppsPerX() * GridController._instance.GetAppsPerY()) -1;
    }

    private Channel FindChannel(int grid, int position)
    {
        foreach (Channel channel in loadedChannels)
        {
            if (channel.gridNumber == grid && channel.position == position)
            {
                return channel;
            }
        }
        return null;
    }

    public void CloseChannel()
    {
        if (currentChannel == null || AppController.IsTransitioning())
        {
            return;
        }

        AppController.SetTransitioning(true);
        StartCoroutine(_CloseChannel(0.5f));
    }

    private IEnumerator _CloseChannel(float duration, bool playLeaveSound = true)
    {
        if (duration > 0f) StartCoroutine(ZoomProbe("out", duration));
        CameraController._instance.ZoomOut(duration);
        blackBorder.gameObject.SetActive(false);
        if (playLeaveSound)
        {
            AudioController.PlaySoundEffect(AudioLibrary._instance.LEAVE_CHANNEL);
        }

        if (changeBorderColor)
        {
            currentChannel.appHandler.border.DOColor(Color.white, duration);
        }
        
        // on a TV it shrinks back into the tile's own shape (then is put back to its usual size once hidden)
        bool tvZoom = TvZoom();
        channelObjects.transform.DOScale(tvZoom ? TileFitScale() : GetDefaultChannelObjectsSize(), duration);
        // straight back to the size the home grid keeps it at (classic TV tiles have their own proportions), so it never shows a wrong shape first
        currentChannel.appHandler.transform.DOScale(HomeTileScale(), duration);

        DOTween.ToAlpha(() => blackBlackground.color, x => blackBlackground.color = x, 0, duration);
        //DOTween.ToAlpha(() => blackBorder.color, x => blackBorder.color = x, 0, duration);
        DOTween.To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, 0, duration);
        
        AudioController.StopMainAudio();
        AudioController.UnmuteBackgroundAudio();

        yield return new WaitForSeconds(duration);
        channelView.SetActive(false);
        if (tvZoom) { channelObjects.transform.DOKill(); RefreshChannelObjectsSize(); }
        currentChannel = null;
        CameraController.SetAutorotationStatus(CameraController._instance.isHorizontalAllowed);
        AnimationController._instance.ChangeCurrentAnimation(null);
        GridController._instance.ShowArrows();
        AppController.SetTransitioning(false);
    }

    private static bool TvZoom() => TVMode.IsTV && !CameraController.IsPortrait();

    // the preview's scale that makes it exactly as wide as the open channel's tile on the home grid (its height then matches too:
    // the preview is the screen's shape and a tile is almost the same shape)
    private Vector3 TileFitScale()
    {
        var rt = (RectTransform) channelObjects.transform;
        var tile = currentChannel != null && currentChannel.appHandler != null ? currentChannel.appHandler.rectTransform : null;
        if (tile == null) return GetDefaultChannelObjectsSize();
        float tw = tile.rect.width * Mathf.Abs(tile.lossyScale.x), pw = rt.rect.width * Mathf.Abs(rt.lossyScale.x);
        if (tw <= 0f || pw <= 0f || rt.localScale.x <= 0f) return GetDefaultChannelObjectsSize();
        float k = rt.localScale.x * tw / pw;
        Debug.Log("[Zoom] tile " + Mathf.RoundToInt(tw) + " wide, preview " + Mathf.RoundToInt(pw) + " -> scale " + k.ToString("0.000"));
        return new Vector3(k, k, 1f);
    }

    // the scale a channel tile has on the home grid (the same numbers Grid.ResizeGrid and TVNav.EnforceTileSizes use)
    public static Vector3 HomeTileScale()
    {
        float s = GridController._instance.GetChannelSizeMultiplier();
        var v = new Vector3(s * TVMode.TileStretch.x, s * TVMode.TileStretch.y, 0f);
        if (TVMode.IsTV && !TVGlass.On && !CameraController.IsPortrait())
            v = Vector3.Scale(v, TVMode.TileFill);
        return v;
    }

    public void RefreshChannelObjectsSize()
    {
        channelObjects.transform.localScale = GetDefaultChannelObjectsSize();
    }

    public Vector3 GetDefaultChannelObjectsSize()
    {
        return defaultChannelScale * ((CameraController.IsPortrait() ? 1f : 0.9f) * GridController._instance.GetChannelSizeMultiplier());
    }

    public void StartChannel()
    {
        if (currentChannel == null || AppController.IsTransitioning())
        {
            return;
        }
        AppController.SetTransitioning(true);
        StartCoroutine(_StartChannel());
    }

    private IEnumerator _StartChannel()
    {
        // We play the start audio. Since we added the "start channel" sound a bit later, some themes might not have implemented it yet
        // So first we try to get the custom START_CHANNEl sound, and if not possible, then we play the CLICK one
        AudioClip start = ThemeController.GetAudio(AudioLibrary._instance.START_CHANNEL) ?? AudioLibrary._instance.CLICK_BUTTON;
        AudioController.PlaySoundEffect(start);

        // Now we open the app
        Channel channelToOpen = currentChannel;
        FadeController._instance.FadeIn(0.7f);
        yield return new WaitForSeconds(0.9f * FadeController.GetSpeedMultiplier());
        StartCoroutine(_CloseChannel(0.1f, false));
        yield return new WaitForSeconds(0.35f * FadeController.GetSpeedMultiplier());
        channelToOpen.Execute();
        yield return new WaitForSeconds(0.25f * FadeController.GetSpeedMultiplier());
        FadeController._instance.FadeOut(0.1f);
        AppController.SetTransitioning(false);
    }

    public void RefreshIconAnimations()
    {
        foreach (Channel channel in loadedChannels)
        {
            channel.iconAnimation?.Stop();
        }
    }

    public void CoverChannels()
    {
        foreach (Channel channel in loadedChannels)
        {
            channel.Cover();
        }
    }

    public void UncoverChannels()
    {
        foreach (Channel channel in loadedChannels)
        {
            channel.Uncover();
        }
    }
    
    public void ImportTempChannelAnimation(string animationId, CHANNELTYPE type)
    {
        string path;
        if (type == CHANNELTYPE.ICON)
        {
            path = SaveManager.TEMP_ANIMATIONS + $"temp_icon_{animationId}.zip";
        }
        else
        {
            path = SaveManager.TEMP_ANIMATIONS + $"temp_banner_{animationId}.zip";
        }
        
        if (File.Exists(path))
        {
            // we update our loaded animation, if it's even loaded
            ChannelAnimation channelAnimation = GetAnimationInList(tempAnimations, FileManager.GetFileName(path), true);
            if (channelAnimation != null)
            {
                tempAnimations.Remove(channelAnimation);
                channelAnimation.isTemp = false;
                string importedName = channelAnimation.importedFileName;
                channelAnimation.importedFileName = importedName.StartsWith("temp_") ? importedName.Remove(0, 5) : importedName;
            }

            // we move the file
            string newDirectory = SaveManager.GetChannelSaveFolder(type) + FileManager.GetFileName(path).Replace("temp_", "");
            bool shouldRefresh = false;
            if (File.Exists(newDirectory))
            {
                shouldRefresh = true;
                File.Delete(newDirectory);
            }
            File.Move(path,newDirectory);
            
            if (shouldRefresh)
            {
                // Anim was already present, we need to refresh it
                RefreshAnimation(FileManager.GetFileName(newDirectory),false, type);
            }
        }
    }

    public void RefreshAnimation(string fileName, bool isTemp, CHANNELTYPE type)
    {
        // This method takes care of refreshing a loaded animation for a newly downloaded one
        ChannelAnimation loadedAnim = !isTemp ? GetLoadedChannelAnimation(fileName) : GetTempChannelAnimation(fileName);

        if (loadedAnim != null)
        {
            // we save the channels it was linked to
            List<Channel> channels = new List<Channel>();
            if (!isTemp)
            {
                string animId = loadedAnim.onlineInfo?.animationId;
                foreach (Channel channel in loadedChannels)
                {
                    if (type == CHANNELTYPE.ICON)
                    {
                        if (channel.iconAnimation?.onlineInfo != null && channel.iconAnimation.onlineInfo.animationId.Equals(animId))
                        {
                            channels.Add(channel);
                        }
                    }
                    else
                    {
                        if (channel.GetBannerAnimation()?.onlineInfo != null && channel.GetBannerAnimation().onlineInfo.animationId.Equals(animId))
                        {
                            channels.Add(channel);
                        }
                    }
                }
            }

            // We delete and recreate the animation
            DeleteChannelAnimation(loadedAnim,false);
            ChannelAnimation newAnim = LoadChannelAnimation(fileName, isTemp);
            
            // And we link it back to the proper channels
            foreach (Channel channel in channels)
            {
                channel.SetAnimation(newAnim);
            }
        }
    }

    public List<AnimationBasicInfo> GetAllChannelInfo(CHANNELTYPE channeltype)
    {
        string[] newPaths = Directory.GetFiles(SaveManager.GetChannelSaveFolder(channeltype), "*.zip");
        List<AnimationBasicInfo> list = new List<AnimationBasicInfo>();
        foreach (string path in newPaths)
        {
            AnimationBasicInfo basicInfo = SaveManager.GetBasicInfo(true, FileManager.GetFileName(path));
            if (basicInfo != null)
            {
                list.Add(basicInfo);
            }
        }

        return list;
    }
    
    public List<AnimationBasicInfo> GetAllThemeInfo()
    {
        string[] newPaths = Directory.GetFiles(SaveManager.SAVE_FOLDER_THEMES, "*.zip");
        List<AnimationBasicInfo> list = new List<AnimationBasicInfo>();
        foreach (string path in newPaths)
        {
            // a theme file's name and date are read once (opening every zip again made the theme list take a long while)
            string key = path + "|" + new FileInfo(path).Length;
            if (!themeInfoCache.TryGetValue(key, out AnimationBasicInfo basicInfo))
            {
                basicInfo = SaveManager.GetBasicInfo(false, FileManager.GetFileName(path));
                if (basicInfo != null) themeInfoCache[key] = basicInfo;
            }
            if (basicInfo != null)
            {
                list.Add(basicInfo);
            }
        }

        return list;
    }
    private readonly Dictionary<string, AnimationBasicInfo> themeInfoCache = new Dictionary<string, AnimationBasicInfo>();

    public List<ChannelAnimation> GetAllChannelAnimations(CHANNELTYPE channeltype)
    {
        SaveManager.LoadAllChannelAnimations(channeltype);
        if (loadedAnimationsList.ContainsKey(channeltype))
        {
            return loadedAnimationsList[channeltype];
        }
        return new List<ChannelAnimation>();
    }
    
    public List<ChannelAnimation> GetAllLoadedChannelAnimations()
    {
        List<ChannelAnimation> loadedList = new List<ChannelAnimation>();
        foreach (List<ChannelAnimation> list in loadedAnimationsList.Values)
        {
            loadedList.AddRange(list);
        }
        return loadedList;
    }
    
    public List<string> GetUsedChannelAnimations()
    {
        List<string> list = new List<string>();
        foreach (Channel channel in loadedChannels)
        {
            if (string.IsNullOrEmpty(channel.iconName) && !list.Contains(channel.iconName))
            {
                list.Add(channel.iconName);
            }
            if (string.IsNullOrEmpty(channel.bannerName) && !list.Contains(channel.bannerName))
            {
                list.Add(channel.bannerName);
            }
        }

        return list;
    }

    public List<OnlineInfo> GetAllOnlineInfos()
    {
        // NEW ANIMATIONS:
        List<string> paths = new List<string>();
        paths.AddRange(Directory.GetFiles(SaveManager.SAVE_FOLDER_ANIMATIONS_ICONS, "*.zip"));
        paths.AddRange(Directory.GetFiles(SaveManager.SAVE_FOLDER_ANIMATIONS_BANNERS, "*.zip"));

        List<OnlineInfo> infos = new List<OnlineInfo>();
        foreach (string path in paths)
        {
            OnlineInfo info = GetOnlineInfo(FileManager.GetFileName(path), true, false);
            if (info != null)
            {
                infos.Add(info);
            }
        }
        return infos;
    }

    public OnlineInfo GetOnlineInfo(string fileName, bool includeFiles, bool includeTemp)
    {
        // We try to find it in the temp ones, if available
        if (includeTemp)
        {
            // we search in the loaded anims
            ChannelAnimation tempAnim = GetTempChannelAnimation(fileName);
            if (tempAnim != null)
            {
                return tempAnim.onlineInfo;
            }

            // we try to load it
            string path = SaveManager.TEMP_ANIMATIONS + fileName;
            if (File.Exists(path))
            {
                return SaveManager.GenerateOnlineInfo(path);
            }
        }
        
        // if no temp found, we try to find it in the loaded files
        if (includeFiles)
        {
            ChannelAnimation loadedAnim = GetLoadedChannelAnimation(fileName);
            if (loadedAnim != null)
            {
                return loadedAnim.onlineInfo;
            }
        }

        // Otherwise, we find the zip, and extract exclusively to read the online info and return it
        string initialPath = fileName.StartsWith("icon_")
            ? SaveManager.SAVE_FOLDER_ANIMATIONS_ICONS
            : SaveManager.SAVE_FOLDER_ANIMATIONS_BANNERS;
        string zipPath = initialPath + fileName;
        return SaveManager.GenerateOnlineInfo(zipPath);
    }

    public ChannelAnimation GetOrLoadChannelAnimation(string fileName, bool includeFiles = true, bool includeTemp = false)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }
        
        Debug.Log("Checking " + fileName);
        // First we try to find it in the temp files
        if (includeTemp)
        {
            ChannelAnimation channelAnimation = GetTempChannelAnimation(fileName);
            if (channelAnimation != null)
            {
                return channelAnimation;
            }
            Debug.Log("Loading temp " + fileName + " from disk.");
            ChannelAnimation anim = LoadChannelAnimation(fileName, true);
            if (anim != null)
            {
                return anim;
            }
        }
        
        // Then we try to find it in the loaded files
        if (includeFiles)
        {
            ChannelAnimation channelAnimation = GetLoadedChannelAnimation(fileName);
            if (channelAnimation != null)
            {
                return channelAnimation;
            }
            Debug.Log("Loading " + fileName + " from disk.");
            ChannelAnimation anim = LoadChannelAnimation(fileName);
            if (anim != null)
            {
                return anim;
            }
        }

        // If we reach this, it wasn't found anywhere
        return null;
    }

    public ChannelAnimation GetLoadedChannelAnimation(string fileName)
    {
        List<ChannelAnimation> list;
        if (fileName.StartsWith("icon_"))
        {
            list = loadedAnimationsList[CHANNELTYPE.ICON];
        }
        else
        {
            list = loadedAnimationsList[CHANNELTYPE.BANNER];
        }
        return GetAnimationInList(list, fileName);
    }

    public ChannelAnimation GetTempChannelAnimation(string fileName)
    {
        return GetAnimationInList(tempAnimations, fileName, true);
    }

    private ChannelAnimation GetAnimationInList(List<ChannelAnimation> list, string fileName, bool isTemp = false)
    {
        foreach (ChannelAnimation anim in list)
        {
            if (anim.GetImportedFileName().Equals(fileName)
                || (isTemp && anim.GetImportedFileName().Replace("temp_","").Equals(fileName.Replace("temp_",""))))
            {
                return anim;
            }
        }
        return null;
    }
    
    private ChannelAnimation LoadChannelAnimation(string fileName, bool isTemp = false)
    {
        ChannelAnimation channelAnimation = SaveManager.LoadChannelAnimation(fileName, isTemp);
        if (channelAnimation != null)
        {
            AddAnimationToList(channelAnimation);
        }
        return channelAnimation;
    }

    public void LoadChannelAnimation(ChannelAnimation animation)
    {
        AddAnimationToList(animation);
    }

    private void AddAnimationToList(ChannelAnimation channelAnimation)
    {
        if (channelAnimation.isTemp)
        {
            tempAnimations.Add(channelAnimation);
        }
        else
        {
            List<ChannelAnimation> list = loadedAnimationsList[channelAnimation.type];
            list.Add(channelAnimation);
            loadedAnimationsList[channelAnimation.type] = list.OrderBy(anim => anim.name).ToList();
        }
    }

    public void ConvertChannelAnimation(ChannelAnimation anim)
    {
        // First we clone the anim
        string newName = CloneChannelAnimation(anim);
        
        // Now we move it to the proper folder
        string finalPath = SaveManager.ConvertChannelAnimation(newName);
        
        // Finally, we load it, change the type and save
        ChannelAnimation newAnim = LoadChannelAnimation(finalPath);
        newAnim.SetChannelType(newAnim.type == CHANNELTYPE.ICON ? CHANNELTYPE.BANNER : CHANNELTYPE.ICON);
        newAnim.Save();
    }

    public string CloneChannelAnimation(ChannelAnimation animation)
    {
        // We save its previous name and ID
        string name = animation.name;
        string importedName = animation.GetImportedFileName();
        long id = animation.id;
        string forkId = animation.forkId;

        // We generate new ones
        animation.requestedNewName = name + " COPY";
        animation.forkId = animation.onlineInfo?.animationId;
        animation.CreateID();
        animation.Save(true);
        string newName = animation.GetImportedFileName();
        
        // We return it to its previous values
        animation.requestedNewName = null;
        animation.name = name;
        animation.importedFileName = importedName;
        animation.id = id;
        animation.forkId = forkId;
        
        // And we return the clone's name
        return newName;
    }

    public void DeleteChannelAnimation(ChannelAnimation anim, bool deleteFile = true)
    {
        if (anim is null)
        {
            return;
        }
        
        anim.Unload();

        if (deleteFile)
        {
            // Then we want it fully gone, not just removed from the lists.
            SaveManager.DeleteChannelAnimationFile(anim);

            // And we remove it from the linked channels
            foreach (Channel channel in anim.linkedChannels)
            {
                if (anim.type == CHANNELTYPE.ICON && anim.Equals(channel.iconAnimation))
                {
                    channel.SetIconAnimation(null);
                }
                else if(anim.type == CHANNELTYPE.BANNER && anim.Equals(channel.GetBannerAnimation()))
                {
                    channel.SetBannerAnimation(null);
                }
            }
            anim.linkedChannels.Clear();
        }
    }

    public void UnlistAnimation(ChannelAnimation anim)
    {
        if (anim.isTemp)
        {
            tempAnimations.Remove(anim);
        }
        else
        {
            loadedAnimationsList[anim.type].Remove(anim);
        }
    }

    public void DeleteChannel(Channel channel)
    {
        channel.UnlinkAnimations();
        loadedChannels.Remove(channel);
        SaveManager.DeleteChannel(channel);
    }

    public void SetupChannel(Channel channel)
    {
        Channel alreadyChannel = FindChannel(channel.gridNumber, channel.position);
        if (alreadyChannel == null)
        { 
            bool addedChannel = GridController._instance.InsertChannel(channel);
            if (addedChannel)
            {
                loadedChannels.Add(channel);
            }
        }
        else
        {
            Debug.Log($"There are 2 (or more!) channels in the position:\nGrid={channel.gridNumber}, Position={channel.position}");
        }
    }
}
