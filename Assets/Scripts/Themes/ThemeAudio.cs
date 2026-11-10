using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public class ThemeAudio
{
    public string audioClipName;
    public string audioFileName;
    public string filePath;
    [JsonIgnore] public AudioClip audioClip;
    [JsonIgnore] public bool finishedLoading;
    
    private Theme theme;

    public void LinkTheme(Theme theme)
    {
        this.theme = theme;
    }

    public void LoadAudio(string extractedPath)
    {
        // Now we load it, whether it's a video, gif or image
        filePath = extractedPath + Path.DirectorySeparatorChar + audioFileName;
        // a TV skips long theme songs: decoding a few minutes of audio into memory is what froze small projectors
        if (TVMode.IsTV && !string.IsNullOrEmpty(filePath) && File.Exists(filePath) && new FileInfo(filePath).Length > 1200 * 1024)
        {
            // the theme opens without it; the song is streamed from the file a little later (never decoded whole into memory)
            Debug.Log("[Theme] long audio file on TV, streaming it later: " + audioFileName);
            finishedLoading = true;
            string p = filePath;
            SyncContext.RunOnUnityThread(() => AudioController._instance.StartCoroutine(LoadLater(p)));
            return;
        }
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            SyncContext.RunOnUnityThread(() =>
            {
                AudioController.LoadAudio(filePath, audio =>
                {
                    if (audio != null)
                    {
                        audioClip = audio;
                        audioClip.name = audioClipName;
                    }
                    finishedLoading = true;
                });
            });
        }
        else
        {
            Debug.Log($"Audio file at {filePath} doesn't exist!");
            finishedLoading = true;
        }
    }

    private IEnumerator LoadLater(string path)
    {
        // soon after the menu is up (40 s later, the switch was heard as the music cutting out; a theme's own menu song was
        // only heard after leaving a channel)
        var app = AppController._instance;
        float until = Time.unscaledTime + 60f;
        while (Time.unscaledTime < until && (app == null || app.mainView == null || !app.mainView.activeInHierarchy || app.warningView.activeInHierarchy)) yield return new WaitForSecondsRealtime(0.5f);
        yield return new WaitForSecondsRealtime(2f);
        AudioController.LoadAudio(path, audio =>
        {
            if (audio == null) return;
            audioClip = audio;
            audioClip.name = audioClipName;
            Debug.Log("[Theme] streaming audio ready: " + audioFileName);
            if (AudioController.PlayingMenuMusic) { Debug.Log("[Music] theme song ready: menu music switches to it"); AudioController.RestartBackgroundAudio(); }
            else Reload();
        }, true, true);
    }

    public void SaveAudio(string extractedPath)
    {
        string newPath = extractedPath + "/" + audioFileName;
        if (!File.Exists(newPath))
        {
            FileManager.CopyFile(filePath, newPath, false);
        }
        
        filePath = theme.MoveToDeserializedFolder(filePath) ?? filePath;
    }

    public void Reload()
    {
        AudioClip currentBack = AudioController._instance.backgroundAudio.clip;
        if (currentBack != null && currentBack.name.Equals(audioClipName))
        {
            AudioController.RestartBackgroundAudio();
        }
    }

    public void Unload()
    {
        // we also reload so the background audio comes back to normal
        GameObject.Destroy(audioClip);
        audioClip = null;
    }
}