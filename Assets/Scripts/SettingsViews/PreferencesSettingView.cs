using Data;
using Misc;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SettingsViews
{
    public class PreferencesSettingView : SettingsView
    {
        public PreferencesSettingView() : base(SettingsController.SETTINGSMENU.PREFERENCES,
            "settings.title.preferences", ThemeColor.SettingsBackgroundDark)
        {
        }

        public override void Show()
        {
            base.Show();
            EnableMainGameObject(_instance.selectionMenu);
            AppController._instance.versionText.gameObject.SetActive(true);
            SetupPreferences();
        }

        public override void ClickedBack()
        {
            _instance.generalSettingView.Show();
        }

        private void SetupPreferences()
        {
            SetContentPivot(1f);
            PreferencesSpawner.SpawnVoid(55f);
            if (TVMode.IsTV)
            {
                PreferencesSpawner.SpawnPreference_Void("preferences.normallook.title", TVThemes.AskNormalLook);
                PreferencesSpawner.SpawnPreference_Void("preferences.resetall.title",
                    () => PopupController.ShowPopup("popup.resetall", TVReset.Run, PopupController.ClosePopup));
                PreferencesSpawner.SpawnVoid(40f);
            }

            if (Application.isMobilePlatform)
            {
                // we spawn the launcher button (a TV gets the "Home button opens Revo" switch further down instead)
                #if !UNITY_IOS
                bool launcher = PREFS.IsLauncherVersion.GetBool();
                if (!TVMode.IsTV) {
                GameObject prefab = GameObject.Instantiate(launcher ? _instance.settingsRemoveAppLauncherPrefab : _instance.settingsSetAppLauncherPrefab, _instance.optionsHolder);
                prefab.GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (!launcher)
                    {
                        PopupController.ShowPopup("popup.shouldlauncher",
                            () =>
                            {
                                AndroidLinker._instance.SetLauncherVersion(true);
                                AndroidLinker._instance.OpenPhoneLauncherSettings();
                            }, PopupController.ClosePopup);
                    }
                    else
                    {
                        PopupController.ShowPopup("popup.shouldremovelauncher", () =>
                        {
                            AndroidLinker._instance.SetLauncherVersion(false);
                            PopupController.ShowPopup("popup.disabledlauncher", () =>
                            {
                                AndroidLinker._instance.OpenPhoneLauncherSettings();
                            }, () =>
                            {
                                // if we don't want to go to android's settings, we refresh the menu so it shows the button again
                                // todo test
                                Show();
                                PopupController.ClosePopup();
                            });
                        }, PopupController.ClosePopup);
                    }
                });
                }
                #endif
                
                #if !UNITY_IOS
                // settings button
                GameObject settingPrefab = GameObject.Instantiate(_instance.settingsPreferenceAppPrefab, _instance.optionsHolder);
                settingPrefab.GetComponent<Button>().onClick.AddListener(() =>
                {
                    AndroidLinker._instance.OpenPhoneGeneralSettings();
                });
                
                // restart app button
                GameObject restart = GameObject.Instantiate(_instance.settingsPreferenceRestart, _instance.optionsHolder);
                restart.GetComponent<Button>().onClick.AddListener(() =>
                {
                    PopupController.ShowPopup("popup.restartapp", () =>
                    {
                        AndroidLinker._instance.RestartApp();
                    }, PopupController.ClosePopup);
                });
                #endif
            }
            else
            {
                // We spawn the Wiimote preferences
                GameObject wiimotePrefab = GameObject.Instantiate(_instance.settingsPreferenceWiimoteMenu, _instance.optionsHolder);
                wiimotePrefab.GetComponent<Button>().onClick.AddListener(() =>
                {
                    _instance.preferencesWiimoteSettingView.Show();
                });
            }
            
            // gestures. hopefully that way they'll learn how to lift channels
            if (!TVMode.IsTV) // no pointer on a TV, so no gestures
            {
                GameObject gestures = GameObject.Instantiate(_instance.settingsPreferenceGesturesMenu, _instance.optionsHolder);
                gestures.GetComponent<Button>().onClick.AddListener(() =>
                {
                    _instance.preferencesGesturesSettingView.Show();
                });
            }

            PreferencesSpawner.SpawnVoid(90f);
            PreferencesSpawner.SpawnPreference_Bool("preferences.12hformat.title",
                "preferences.12hformat.description",
                PREFS.Is12Format, Clock.CheckPeriodText);
            PreferencesSpawner.SpawnPreference_Bool("preferences.americandate.title",
                "preferences.americandate.description",
                PREFS.IsAmericanDate, Date.RefreshAllDates);
            if (TVMode.IsTV)
            {
                // Android TV / projector options
                PreferencesSpawner.SpawnPreference_Void("preferences.channelserver.title", TVChannelServer.ShowFromSettings);
                PreferencesSpawner.SpawnPreference_Void("preferences.tvupdate.title", TVUpdate.Check);
                PreferencesSpawner.SpawnPreference_Bool("preferences.tvhome.title",
                    "preferences.tvhome.description",
                    PREFS.TvHomeButton, () => TVHome.Apply(PREFS.TvHomeButton.GetBool()));
                if (!PREFS.TvCoolIdle.GetBool()) PREFS.TvCoolIdle.SetBool(true); // saving power when idle is always on
PreferencesSpawner.SpawnPreference_Dropdown("preferences.tvres.title",
                    new System.Collections.Generic.List<string> { "Smooth (540p)", "Balanced (720p)", "Sharp (native)" },
                    PREFS.TvRenderHeight.GetInt() == 0 ? "Sharp (native)" : PREFS.TvRenderHeight.GetInt() >= 720 ? "Balanced (720p)" : "Smooth (540p)",
                    () => PopupController.ShowPopup("preferences.tvres.description"),
                    () => { },
                    (value, originalIndex, dropdown) =>
                    {
                        PREFS.TvRenderHeight.SetInt(value.StartsWith("Sharp") ? 0 : value.StartsWith("Balanced") ? 720 : 540);
                        TVPerf.Apply();
                    });
                PreferencesSpawner.SpawnPreference_Bool("preferences.glass2026.title",
                    "preferences.glass2026.description",
                    PREFS.Glass2026, () =>
                    {
                        // asked first (a press by accident used to restart Revo straight away); No puts the switch back
                        bool on = PREFS.Glass2026.GetBool();
                        PREFS.Glass2026.SetBool(!on); // the look stays as it is while the question is up
                        PopupController.ShowPopup(on ? "tv.glass.confirmon" : "tv.glass.confirmoff", () =>
                        {
                            PREFS.Glass2026.SetBool(on);
                            // the two looks are drawn differently from the very start: switching takes a clean restart (a live switch left half of each)
                            PopupController.ShowPopup("tv.glass.restart", () => AndroidLinker._instance.RestartApp());
                            DG.Tweening.DOVirtual.DelayedCall(1.8f, () => AndroidLinker._instance.RestartApp());
                        }, () =>
                        {
                            PREFS.Glass2026.SetBool(!on);
                            PopupController.ClosePopup();
                            string title = TextController.GetTranslation("preferences.glass2026.title");
                            foreach (var t in SettingsController._instance.optionsHolder.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                                if (t.text == title) { var row = t.transform.parent.parent.parent.gameObject; StaticUtils.ChangeBooleanImage(row, !on); break; }
                        });
                    });
                PreferencesSpawner.SpawnPreference_Bool("preferences.hdmibutton.title",
                    "preferences.hdmibutton.description",
                    PREFS.HdmiButton);
                PreferencesSpawner.SpawnPreference_Dropdown("preferences.hdmiport.title",
                    new System.Collections.Generic.List<string> { "HDMI 1", "HDMI 2", "HDMI 3", "HDMI 4" },
                    "HDMI " + PREFS.HdmiPort.GetInt(),
                    () => PopupController.ShowPopup("preferences.hdmiport.description"),
                    () => { },
                    (value, originalIndex, dropdown) => PREFS.HdmiPort.SetInt(int.Parse(value.Replace("HDMI ", ""))));
                PreferencesSpawner.MakeLastDropdownCycle(() => PopupController.ShowPopup("preferences.hdmiport.description")); // one OK press = next HDMI input
                PreferencesSpawner.SpawnPreference_Bool("preferences.holidays.title", "preferences.holidays.description", PREFS.TvHolidays);
                PreferencesSpawner.SpawnPreference_Bool("preferences.glint.title", "preferences.glint.description", PREFS.TvScreenGlint);
                // hover light colour: each OK press = the next colour, seen at once on the light itself (2026 keeps its own look)
                if (!TVGlass.On)
                {
                    var glowNames = new System.Collections.Generic.List<string>();
                    foreach (var gp in TVGlowColor.Presets) glowNames.Add(gp.hex == "auto" ? "Normal (built-in / theme's own)" : "<color=" + gp.hex + ">" + gp.name + "</color>");
                    string glowCur = TVGlowColor.Current; int ci = TVGlowColor.Presets.FindIndex(gp => string.Equals(gp.hex, glowCur, System.StringComparison.OrdinalIgnoreCase));
                    if (ci < 0) { glowNames.Add("<color=" + glowCur + ">" + TVGlowColor.NameOf(glowCur) + "</color>"); ci = glowNames.Count - 1; }
                    string customHex = ci >= TVGlowColor.Presets.Count ? glowCur : null;
                    PreferencesSpawner.SpawnPreference_Dropdown("preferences.glowcolor.title", glowNames, glowNames[ci],
                        () => PopupController.ShowPopup("preferences.glowcolor.description"), () => { },
                        (gv, gi, gd) => { int k = gd.value; TVGlowColor.Set(k < TVGlowColor.Presets.Count ? TVGlowColor.Presets[k].hex : customHex); });
                    PreferencesSpawner.MakeLastDropdownCycle(() => PopupController.ShowPopup("preferences.glowcolor.description"));
                }
            }
            PreferencesSpawner.SpawnPreference_Language();
            // on a TV these three are fixed: SD card always shown, channels never auto-loaded
            if (TVMode.IsTV)
            {
                if (!PREFS.ShowSdCard.GetBool()) PREFS.ShowSdCard.SetBool(true);
                if (PREFS.LoadChannelsAutomatically.GetBool()) PREFS.LoadChannelsAutomatically.SetBool(false);
            }
            else
            {
            PreferencesSpawner.SpawnPreference_Bool("preferences.sdcard.title",
                "preferences.sdcard.description",
                PREFS.ShowSdCard, () => { GridController._instance.RefreshSdCard(); });
            PreferencesSpawner.SpawnPreference_Bool("preferences.autoloadanims.title",
                "preferences.autoloadanims.description",
                PREFS.LoadChannelsAutomatically);
            }
            
            // phone-only options (grid size, horizontal mode, banners at boot, fps limit): a TV keeps its own tuned values
            if (!TVMode.IsTV)
            {
            PreferencesSpawner.SpawnVoid(60f);
            PreferencesSpawner.SpawnPreference_GridSize();
            bool isCustom = GridController._instance.GetChosenGridSize() == GRIDSIZE.CUSTOM;
            if (isCustom)
            {
                bool vertical = !PREFS.AllowHorizontal.GetBool();
                PreferencesSpawner.SpawnPreference_Int("preferences.gridsizehorizontal.title",
                    "preferences.gridsizehorizontal.description",
                    vertical ? PREFS.NumberAppsX : PREFS.NumberAppsXHorizontal, 1, 12,
                    () => GridController._instance.SetAppNumbersFromPrefs());
                PreferencesSpawner.SpawnPreference_Int("preferences.gridsizevertical.title",
                    "preferences.gridsizevertical.description",
                    vertical ? PREFS.NumberAppsY : PREFS.NumberAppsYHorizontal, 1, 12,
                    () => GridController._instance.SetAppNumbersFromPrefs());
            }
            PreferencesSpawner.SpawnVoid(60f);
            
            PreferencesSpawner.SpawnPreference_Bool("preferences.horizontal.title",
                "preferences.horizontal.description",
                PREFS.AllowHorizontal, () =>
                {
                    if (PREFS.AllowHorizontal.GetBool())
                    {
                        PopupController.ShowPopup("popup.warninghorizontal");
                    }
                });
            PreferencesSpawner.SpawnPreference_Bool("preferences.loadbannersatboot.title",
                "preferences.loadbannersatboot.description",
                PREFS.LoadBannersAtBoot, () =>
                {
                    if (PREFS.LoadBannersAtBoot.GetBool())
                    {
                        PopupController.ShowPopup("popup.warningloadbanners");
                    }
                });
            PreferencesSpawner.SpawnPreference_Int("preferences.fpslimit.title",
                "preferences.fpslimit.description",
                PREFS.TargetFramerate, 10, 240,
                () => AppController._instance.RefreshFramerate());
            PreferencesSpawner.SpawnPreference_Bool("preferences.decimalstring.title",
                "preferences.decimalstring.description",
                PREFS.DecimalAsString);
            }
            
            #if !UNITY_IOS
            if (!TVMode.IsTV)
            PreferencesSpawner.SpawnPreference_Bool("preferences.standalonebrowser.title",
                "preferences.standalonebrowser.description",
                PREFS.StandaloneBrowser);
            #endif
            
            PreferencesSpawner.SpawnPreference_FloatSlider("preferences.musicvolume.title",
                "preferences.musicvolume.description",
                PREFS.MusicVolume, 0, 1,
                AudioController.RefreshBackgroundVolume);
            PreferencesSpawner.SpawnPreference_FloatSlider("preferences.sfxvolume.title",
                "preferences.sfxvolume.description",
                PREFS.SfxVolume, 0, 1,
                AudioController.RefreshSfxVolume);

            if (!Application.isMobilePlatform)
            {
                // We add a fullscreen option for Windows
                PreferencesSpawner.SpawnPreference_Bool("preferences.fullscreen.title",
                    "preferences.fullscreen.description",
                    PREFS.FullScreen, () => { AppController._instance.RefreshCanvasSize(); });
                PreferencesSpawner.SpawnPreference_Float("preferences.scale.title",
                    "preferences.scale.description",
                    PREFS.Scale, 0.25f, 2f, () => { AppController._instance.RefreshCanvasSize(); });
            }
            else
            {
                // We allow Android users to show the pointer
                if (!TVMode.IsTV)
                PreferencesSpawner.SpawnPreference_Bool("preferences.showcursor.title",
                    "preferences.showcursor.description",
                    PREFS.ShowCursor, InputController._instance.RefreshPointerVisibility);
                // We allow showing the navigation bar
                if (!TVMode.IsTV)
                PreferencesSpawner.SpawnPreference_Bool("preferences.navigationbar.title",
                    "preferences.navigationbar.description",
                    PREFS.ShowNavigationBar, () => { AppController._instance.RefreshAndroidFullscreen(); });
            }

            PreferencesSpawner.SpawnPreference_Bool("preferences.autoapps.title",
                "preferences.autoapps.description",
                PREFS.OpenDirectly);
            if (!TVMode.IsTV)
            PreferencesSpawner.SpawnPreference_Int("preferences.numberofpages.title",
                "preferences.numberofpages.description",
                PREFS.NumberOfPages, 1, 20,
                () => GridController._instance.RefreshGrids());
            if (!TVMode.IsTV)
            PreferencesSpawner.SpawnPreference_Float("preferences.fadeout.title",
                "preferences.fadeout.description",
                PREFS.FadeoutSpeed, 0.75f, 6f, () => { FadeController._instance.RefreshSpeed(); });

            PreferencesSpawner.SpawnVoid(90f);
            if (!TVMode.IsTV) { // file pickers and store links are phone things
            PreferencesSpawner.SpawnPreference_Void("preferences.dataimport.title", () =>
            {
                PopupController.ShowPopup("popup.dataimport",
                    () => { _instance.RequestImportData(); }, PopupController.ClosePopup);
            });
            PreferencesSpawner.SpawnPreference_Void("preferences.dataexport.title", () =>
            {
                PopupController.ShowPopup("popup.dataexport",
                    () => { _instance.RequestExportData(); }, PopupController.ClosePopup);
            });
            
            #if !UNITY_IOS
            PreferencesSpawner.SpawnPreference_Void("preferences.reloaddb.title", () =>
            {
                PopupController.ShowPopup("popup.updatetitles", () =>
                {
                    MetadataController._instance.UpdateTitles();
                }, PopupController.ClosePopup);
            });
            #endif
            }

            PreferencesSpawner.SpawnVoid(90f);
            if (!TVMode.IsTV) {
            GameObject ratePrefab = GameObject.Instantiate(_instance.settingsPreferenceQuickReview, _instance.optionsHolder);
            ratePrefab.GetComponent<Button>().onClick.AddListener(() => { AndroidLinker._instance.OpenQuickReview(); });
            GameObject discordPrefab = GameObject.Instantiate(_instance.settingsPreferenceDiscord, _instance.optionsHolder);
            discordPrefab.GetComponent<Button>().onClick.AddListener(() =>
            {
                Application.OpenURL("https://discord.gg/xjrEMS9QY4");
            });
            PreferencesSpawner.SpawnPreference_Void("preferences.website.title",
                () => { Application.OpenURL("https://karuzohikari.com"); });
            }
            PreferencesSpawner.SpawnPreference_Void("preferences.credits.title",
                () => { PopupController.ShowPopup("popup.credits"); });
            PreferencesSpawner.SpawnPreference_Void("preferences.licenses.title",
                () => { PopupController.ShowPopup("popup.licenses"); });
            if (!TVMode.IsTV)
            PreferencesSpawner.SpawnPreference_Void("preferences.deleteaccount.title",
                () => { Application.OpenURL("https://karuzohikari.com/RevoLauncherDeleteAccount"); });
            PreferencesSpawner.SpawnVoid(30f);
            if (TVMode.IsTV) _instance.ScrollToTopSoon();
        }
    }
}