using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class WarningController : MonoBehaviour
{
    public TextMeshProUGUI pressText;
    private CanvasGroup pressGroup;
    private bool canStart = false;
    public static WarningController _instance;
    public Slider progressBar;
    public TextMeshProUGUI channelNumber;
    public TextMeshProUGUI taskDescription;
    private bool isLoading = false;
    private bool pressedEarly;
    public bool debugLockScreen = false;
    public Image viewBlocker;

    private void Awake()
    {
        _instance = this;
        viewBlocker.gameObject.SetActive(true);
        pressGroup = pressText.GetComponent<CanvasGroup>();
    }

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(_Start());
    }

    private IEnumerator _Start()
    {
        while (!TextController._instance.HasLoadedTranslations() || !ThemeController._instance.HasLoadedTheme())
        {
            yield return new WaitForSeconds(0.1f);
        }
        pressGroup.alpha = 0f;
        viewBlocker.gameObject.SetActive(false);
        
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.25f); // this used to be 1.25f, why..?
        canStart = true;
        if (pressedEarly && !PREFS.LoadChannelsAutomatically.GetBool()) { ClickedWarning(); yield break; }
        
        if (PREFS.LoadChannelsAutomatically.GetBool())
        {
            ClickedWarning();
        }
        else
        {
            DOTween.Sequence()
                .Append(pressGroup.DOFade(1, 0.65f).SetLoops(2, LoopType.Yoyo))
                .AppendInterval(0.15f)
                .SetLoops(-1);
        }
    }

    /*private void Update()
    {
        if(RInput.GetMouseButtonDown(0)) {
            ClickedWarning();
        }
    }*/

    // coming back from the Photo Channel after Android closed Revo: straight on to the menu, nobody has to press A again
    public void ContinueWhenReady()
    {
        StartCoroutine(_ContinueWhenReady());
    }

    private IEnumerator _ContinueWhenReady()
    {
        while (!canStart) yield return new WaitForSeconds(0.2f);
        ClickedWarning();
    }

    public void HoldWarning()
    {
        PopupController.ShowPopup("popup.ultralowmemory", () =>
        {
            AppController._instance.SetUltraLowMemoryMode(true);
            GridController._instance.ResetGridSize();
            ClickedWarning();
            PopupController.ClosePopup();
        }, () =>
        {
            ClickedWarning();
            PopupController.ClosePopup();
        });
    }

    public void ClickedWarning()
    {
        if (AppController._instance.finishedLoading)
        {
            // we just close it
            pressText.gameObject.SetActive(false);
            AppController._instance.warningView.SetActive(false);
        }
        else
        {
            if (!isLoading && !canStart && !debugLockScreen && TVMode.IsTV && Time.realtimeSinceStartup > 3f) pressedEarly = true; // still getting ready: go on as soon as it can
            if (!isLoading && canStart && !debugLockScreen)
            {
                isLoading = true;
                pressText.gameObject.SetActive(false);
                if (PREFS.ShowLoadingBar.GetBool() && !TVMode.IsTV) // TV: loading stays hidden behind the fade
                {
                    progressBar.gameObject.SetActive(true);
                }

                if (PREFS.CrashedDuringLoad.GetBool() && PREFS.LoadBannersAtBoot.GetBool())
                {
                    PopupController.ShowPopup("popup.detectedcrash", SaveManager.LoadAllChannels);
                }
                else
                {
                    SaveManager.LoadAllChannels();
                }
            }
        }
    }

    public void Disappear()
    {
        StartCoroutine(_Disappear());
    }

    private IEnumerator _Disappear()
    {
        pressText.gameObject.SetActive(false);
        FadeController._instance.FadeInAndOut(1f,1f,1f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_WARNING);
        yield return new WaitForSeconds(1.25f * FadeController.GetSpeedMultiplier());
        AppController._instance.ShowMainMenu();
        AppController._instance.warningView.SetActive(false);
        yield return new WaitForSeconds(0.35f * FadeController.GetSpeedMultiplier());
        AppController._instance.StartMainMenu();
    }

    public void Show()
    {
        pressText.gameObject.SetActive(true);
        AppController._instance.warningView.SetActive(true);
    }
}
