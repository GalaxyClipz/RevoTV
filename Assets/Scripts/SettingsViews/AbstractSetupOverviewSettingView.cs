using Animations;
using Misc;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SettingsViews
{
    public abstract class AbstractSetupOverviewSettingView : AbstractSetupSettingView
    {
        
        public AbstractSetupOverviewSettingView(SettingsController.SETTINGSMENU menu) : base(menu)
        {
        }

        public override void Show(Channel channel)
        {
            base.Show(channel);
            OptionsSpawner.SummonVoid(_instance.optionsHolder, 35f);
        }

        protected void SpawnDefaultOptions()
        {
            OptionsSpawner.SummonVoid(_instance.optionsHolder, 35f);
            OptionsSpawner.SummonTitle(_instance.optionsHolder, "settings.target.generalargs", true);
            Option id = Option.Create("settings.target.tag", () => currentChannel.GetTag(), x => currentChannel.SetTag(x));
            OptionsSpawner.SetupOption(id, _instance.optionsHolder);
            
            SpawnAnim(currentChannel, _instance.iconSourcePrefab, CHANNELTYPE.ICON);
            // the Forecast Channel's preview and its sound are built in (TVWeatherPreview): only its tile picture can change
            if (!(TVMode.IsTV && TVTiles.IsBuiltInPreview(currentChannel)))
            {
                SpawnAnim(currentChannel, _instance.bannerSourcePrefab, CHANNELTYPE.BANNER);
                SpawnPreviewSound();
            }
        }

        // TV: whether this channel's preview plays a sound (sounds are added from the Channel Server page, a TV has no file picker)
        private void SpawnPreviewSound()
        {
            if (!TVMode.IsTV || currentChannel == null || !currentChannel.isFullySetup) return;
            int grid = currentChannel.gridNumber, pos = currentChannel.position;
            bool has = TVTiles.HasSound(grid, pos);
            var choices = new System.Collections.Generic.List<string> { has ? "Sound is on" : "No sound" };
            string removeText = "Remove the sound", serverText = "Add a sound from the Channel Server page";
            if (has) choices.Add(removeText);
            // sound files already on the TV (Download, Music, Movies, Documents folders) can be added right here
            var files = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var dir in new[] { "Download", "Music", "Movies", "Documents" })
            {
                try
                {
                    string root = "/sdcard/" + dir;
                    if (!System.IO.Directory.Exists(root)) continue;
                    foreach (var f in System.IO.Directory.GetFiles(root))
                    {
                        string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".mp3" && ext != ".ogg" && ext != ".wav") continue;
                        var len = new System.IO.FileInfo(f).Length;
                        if (len < 1024 || len > 12 * 1024 * 1024 || files.Count >= 10) continue;
                        string label = "Add: " + System.IO.Path.GetFileName(f);
                        if (!files.ContainsKey(label)) { files[label] = f; choices.Add(label); }
                    }
                }
                catch (System.Exception) { }
            }
            choices.Add(serverText);
            Option<string> sound = Option.Create("settings.target.previewsound", () => choices[0], x =>
            {
                if (has && x == removeText)
                {
                    TVTiles.SetSound(grid, pos, null, null);
                    PopupController.ShowPopup("settings.target.soundremoved");
                }
                else if (x == serverText) TVChannelServer.ShowFromSettings();
                else if (files.TryGetValue(x, out var path))
                {
                    string err = null;
                    try { err = TVTiles.SetSound(grid, pos, System.IO.File.ReadAllBytes(path), path); }
                    catch (System.Exception) { err = "That file couldn't be read"; }
                    if (err == null) PopupController.ShowPopup("settings.target.soundadded", PopupController.ClosePopup, true, new[] { System.IO.Path.GetFileName(path) });
                    else PopupController.ShowPopup("settings.target.soundfail", PopupController.ClosePopup, true, new[] { err });
                }
            });
            OptionsSpawner.SummonOption_DropdownString(sound, _instance.optionsHolder, choices);
        }
        
        private void SpawnAnim(Channel channel, GameObject prefab, CHANNELTYPE type) {
            GameObject banner = Spawn(prefab);
            ChannelAnimation anim = channel.GetAnimation(type);
            string name = anim?.name;
            if (string.IsNullOrEmpty(name))
            {
                name = channel.GetAnimationName(type);
                if (!string.IsNullOrEmpty(name))
                {
                    // we try to load its basic info
                    AnimationBasicInfo info = SaveManager.GetBasicInfo(true, name);
                    if (info != null)
                    {
                        name = info.animationName;
                    }
                }
            }
            
            // if we didn't find anything, we set it to None
            if (string.IsNullOrEmpty(name))
            {
                name = "None";
            }
            
            banner.transform.Find("NameAndChange").Find("Name").GetComponent<TextMeshProUGUI>().text = name;
            banner.transform.Find("NameAndChange").Find("Button").GetComponent<Button>().onClick.AddListener(() =>
            {
                _instance.setupAnimSettingView.Show(channel, type);
            });
        }

        protected GameObject Spawn(GameObject prefab)
        {
            return GameObject.Instantiate(prefab, _instance.optionsHolder);
        }

        public override void ClickedBack()
        {
            BackOrClose();
        }

        protected void BackOrClose()
        {
            // here we decide what we need to do
            if (currentChannel.isFullySetup)
            {
                // the channel was already created, this was an edit. let's close it
                _instance.ExitSettings(true, false);
            }
            else
            {
                // this was a new channel! let's go back to the types overview
                _instance.setupTypeSettingView.Show(currentChannel);
            }
        }
    }
}