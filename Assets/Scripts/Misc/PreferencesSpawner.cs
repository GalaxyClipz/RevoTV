using System;
using System.Collections.Generic;
using System.Globalization;
using Data;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;
using Object = System.Object;

namespace Misc
{
    public static class PreferencesSpawner
    {
        public static void SpawnVoid(float height)
        {
            GameObject spawned = new GameObject("Void_" + height, typeof(RectTransform));
            spawned.transform.SetParent(SettingsController._instance.optionsHolder);
            spawned.GetComponent<RectTransform>().sizeDelta = new Vector2(100, height);
        }

        public static void SpawnPreference_Bool(string buttonTitle, string description, UnityBoolPreference preference,
            UnityAction callback = null)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferencePrefab, SettingsController._instance.optionsHolder);

            // on a TV the whole row is the switch (a remote has no separate checkbox click); phones keep the info popup
            Button rowButton = prefab.GetComponent<Button>();
            if (!TVMode.IsTV) rowButton.onClick.AddListener(() => { PopupController.ShowPopup(description); });

            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject
                .GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(buttonTitle);

            StaticUtils.ChangeBooleanImage(prefab, preference.GetBool());

            UnityAction flip = () =>
            {
                bool newBool = !preference.GetBool();
                preference.SetBool(newBool);

                StaticUtils.ChangeBooleanImage(prefab, newBool);

                callback?.Invoke();
            };
            prefab.transform.Find("Elements").Find("ImageHolder").GetComponent<Button>().onClick.AddListener(flip);
            if (TVMode.IsTV) rowButton.onClick.AddListener(flip);
        }

        public static void SpawnPreference_Int(string buttonTitle, string description, UnityIntPreference preference, int min,
            int max, UnityAction callback = null)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferenceIntPrefab, SettingsController._instance.optionsHolder);

            prefab.GetComponent<Button>().onClick.AddListener(() => { PopupController.ShowPopup(description); });

            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject
                .GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(buttonTitle);

            int currentValue = preference.GetInt();

            TMP_InputField inputField = prefab.transform.Find("Elements").Find("Input").GetComponent<TMP_InputField>();
            inputField.text = currentValue.ToString();
            inputField.GetComponent<ThemedInputField>().UpdateElement();

            inputField.onEndEdit.AddListener(edit =>
            {
                int newInt = preference.GetDefaultInt();
                try
                {
                    newInt = int.Parse(inputField.text);
                }
                catch (Exception ignored)
                {
                    // ignored
                }

                if (newInt < min)
                {
                    newInt = min;
                }
                else if (newInt > max)
                {
                    newInt = max;
                }

                preference.SetInt(newInt);
                inputField.text = newInt.ToString();
                callback?.Invoke();
            });
        }

        public static void SpawnPreference_Float(string buttonTitle, string description, UnityFloatPreference preference,
            float min, float max, UnityAction callback = null)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferenceFloatPrefab, SettingsController._instance.optionsHolder);

            prefab.GetComponent<Button>().onClick.AddListener(() => { PopupController.ShowPopup(description); });

            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject
                .GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(buttonTitle);

            float currentValue = preference.GetFloat();

            TMP_InputField inputField = prefab.transform.Find("Elements").Find("Input").GetComponent<TMP_InputField>();
            inputField.text = currentValue.ToString();
            inputField.contentType = PREFS.DecimalAsString.GetBool() ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.DecimalNumber;
            inputField.GetComponent<ThemedInputField>().UpdateElement();

            inputField.onEndEdit.AddListener(edit =>
            {
                float newInt = preference.GetDefaultFloat();
                try
                {
                    newInt = float.Parse(inputField.text);
                }
                catch (Exception ignored)
                {
                    // ignored
                }

                if (newInt < min)
                {
                    newInt = min;
                }
                else if (newInt > max)
                {
                    newInt = max;
                }

                preference.SetFloat(newInt);

                inputField.text = newInt.ToString();

                callback?.Invoke();
            });
        }

        public static void SpawnPreference_Language()
        {
            List<string> valueList = new List<string>();
            string currentName = TextController._instance.chosenLanguage.ToString();
            foreach (string lang in TextController._instance.availableLanguages.Keys)
            {
                valueList.Add(lang);
            }

            SpawnPreference_Dropdown("preferences.language.title", valueList, currentName,
                () => { PopupController.ShowPopup("preferences.language.description"); },
                () =>
                {
                    PopupController.ShowPopup("popup.updatetranslations",
                        okCallback: () => { TextController._instance.InitializeLang(); },
                        replacementArray: new[] { TextController._instance.currentVersion.ToString() });
                },
                (value, originalIndex, dropdown) =>
                {
                    PopupController.ShowPopup("popup.changelanguage",
                        () =>
                        {
                            TextController._instance.SetNewLanguage(value);
                            SettingsController._instance.preferencesSettingView.Show();
                            PopupController.ClosePopup();
                        }, () =>
                        {
                            dropdown.SetValueWithoutNotify(originalIndex);
                            PopupController.ClosePopup();
                        },
                        new[] { value });
                });
        }
        
        public static void SpawnPreference_GridSize()
        {
            List<string> valueList = new List<string>();
            string currentName = "";
            bool isHorizontal = PREFS.AllowHorizontal.GetBool();
            Dictionary<string, GRIDSIZE> valueMap = new Dictionary<string, GRIDSIZE>();
            GRIDSIZE chosen = GridController._instance.GetChosenGridSize();
            foreach (GRIDSIZE size in Enum.GetValues(typeof(GRIDSIZE)))
            {
                string verticalX = GridController._instance.CalculateVerticalXApps(size).ToString();
                string verticalY = GridController._instance.CalculateVerticalYApps(size).ToString();
                string name = TextController.GetTranslation("preferences.gridsize." + size.ToString().ToLowerInvariant(),
                    isHorizontal ? new [] {verticalY, verticalX} : new []{verticalX, verticalY});
                valueList.Add(name);
                valueMap[name] = size;
                if (size == chosen)
                {
                    currentName = name;
                }
            }

            SpawnPreference_Dropdown("preferences.gridsize.title", valueList, currentName,
                () => { PopupController.ShowPopup("preferences.gridsize.description"); },
                null,
                (value, originalIndex, dropdown) =>
                {
                    GRIDSIZE chosenSize = valueMap[value];
                    PREFS.GridSize.SetString(chosenSize.ToString());
                    if (chosenSize != GRIDSIZE.CUSTOM)
                    {
                        int verticalX = GridController._instance.CalculateVerticalXApps(chosenSize);
                        int verticalY = GridController._instance.CalculateVerticalYApps(chosenSize);
                        GridController._instance.SetAppNumbers(verticalX, verticalY);
                        PopupController.ShowPopup("popup.changedgridsize");
                    }
                    SettingsController._instance.preferencesSettingView.Show();
                });
        }

        public static void SpawnPreference_Enum<T>(string title, string currentValue, UnityAction onClick, UnityAction onLongClick, Action<string, int, TMP_Dropdown> onSelect)
        {
            // We add the values
            List<string> valueList = new List<string>();
            foreach (Object value in Enum.GetValues(typeof(T)))
            {
                valueList.Add(value.ToString());
            }
            
            SpawnPreference_Dropdown(title, valueList, currentValue, onClick, onLongClick, onSelect);
        }

        public static void SpawnPreference_Dropdown(string title, List<string> values, string currentValue, UnityAction onClick, UnityAction onLongClick, Action<string, int, TMP_Dropdown> onSelect)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferenceDropdownPrefab, SettingsController._instance.optionsHolder);
            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject.GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(title);
            prefab.GetComponent<Button>().onClick.AddListener(onClick);
            prefab.GetComponent<LongClickButton>().onLongClick.AddListener(onLongClick);

            TMP_Dropdown dropdown = prefab.transform.Find("Elements").Find("Dropdown").gameObject.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            dropdown.GetComponent<ThemedDropdown>().UpdateElement();
            dropdown.AddOptions(values);

            // We set the default to the current value
            int originalValue = 0;
            for (int i = 0; i < dropdown.options.Count; i++)
            {
                TMP_Dropdown.OptionData data = dropdown.options[i];
                if (data.text.Equals(currentValue))
                {
                    originalValue = i;
                    break;
                }
            }

            dropdown.SetValueWithoutNotify(originalValue);

            // We assign the setter
            dropdown.onValueChanged.AddListener(index =>
            {
                string value = dropdown.options[index].text;
                onSelect.Invoke(value, originalValue, dropdown); // we let us decide whether to change it or not in the override
            });
        }

        // TV: a drop-down list is fiddly with a remote. The last spawned drop-down becomes one button: each OK steps to the next
        // value (1, 2, 3, 4, 1...), shown in place and saved straight away
        public static void MakeLastDropdownCycle(UnityAction explain = null)
        {
            var holder = SettingsController._instance.optionsHolder;
            if (holder == null || holder.childCount == 0) return;
            var row = holder.GetChild(holder.childCount - 1);
            var dd = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dd == null || dd.options.Count < 2) return;
            dd.enabled = false; // no list pops open
            if (dd.targetGraphic != null) dd.targetGraphic.raycastTarget = false; // the whole row is the button
            UnityAction step = () =>
            {
                dd.value = (dd.value + 1) % dd.options.Count; // fires the same save as picking it from the list
                dd.RefreshShownValue();
            };
            // OK on the row (where the light lands) steps to the next value; holding OK still explains it
            var rowButton = row.GetComponent<Button>();
            if (rowButton != null)
            {
                rowButton.onClick = new Button.ButtonClickedEvent();
                rowButton.onClick.AddListener(step);
                var lc = row.GetComponent<LongClickButton>();
                if (lc != null && explain != null) { lc.onLongClick.RemoveAllListeners(); lc.onLongClick.AddListener(explain); }
            }
        }

        public static void SpawnPreference_FloatSlider(string buttonTitle, string description,
            UnityFloatPreference preference, int min = 0, int max = 1, UnityAction callback = null)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferenceFloatSliderPrefab, SettingsController._instance.optionsHolder);

            prefab.transform.Find("Elements").Find("TitleHolder").GetComponent<Button>().onClick.AddListener(() =>
            {
                PopupController.ShowPopup(description);
            });

            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject
                .GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(buttonTitle);

            float currentValue = preference.GetFloat();

            Slider slider = prefab.transform.Find("Elements").Find("SliderHolder").Find("Slider")
                .GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = currentValue;

            slider.onValueChanged.AddListener(edit =>
            {
                float newFloat = slider.value;
                preference.SetFloat(newFloat);

                callback?.Invoke();
            });
        }

        public static void SpawnPreference_Void(string buttonTitle, UnityAction callback)
        {
            GameObject prefab = GameObject.Instantiate(SettingsController._instance.preferencePrefab, SettingsController._instance.optionsHolder);
            prefab.transform.Find("Elements").Find("TitleHolder").Find("Title").gameObject
                .GetComponent<TextMeshProUGUI>().text = TextController.GetTranslation(buttonTitle);
            GameObject.Destroy(prefab.transform.Find("Elements").Find("ImageHolder").gameObject);
            prefab.GetComponent<Button>().onClick.AddListener(callback);
        }
    }
}