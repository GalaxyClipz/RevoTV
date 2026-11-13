using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

public class AnimationHolder : MonoBehaviour
{
    public AnimatedImage animationInfo;
    public RawImage rawImage;
    public VideoPlayer video;
    public TextMeshProUGUI text;
    public Channel currentChannel;
    public bool isPreparing = false;
    public bool finishedVideo = false;
    public int gifFrame = 0;
    public float gifDelay = 0;
    private bool isFrozen = false;

    public void Setup()
    {
        if (animationInfo.isVideo)
        {
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.renderMode = VideoRenderMode.APIOnly;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.playbackSpeed = animationInfo.GetPlaybackSpeed();
        }
    }

    public void AddText()
    {
        GameObject obj = Instantiate(AnimationController._instance.holderTextPrefab, transform);
        text = obj.GetComponent<TextMeshProUGUI>();
    }

    public void LinkChannel(Channel channel)
    {
        this.currentChannel = channel;
    }

    public void PrepareVideo()
    {
        isPreparing = true;
        video.Prepare();
    }

    public void CheckDebugText()
    {
        if (animationInfo.Equals(PreviewController._instance.currentDebugImage))
        {
            PreviewController._instance.maxDebugText.gameObject.SetActive(true);
            PreviewController._instance.maxDebugText.text = "MAX: " + (Math.Floor(video.length * 100) / 100f);
        }
    }

    public void UpdateGif()
    {
        if (animationInfo.isGif && !isFrozen)
        {
            var fr = animationInfo.gifFrames; var dl = animationInfo.gifFrameDelays;
            if (fr == null || fr.Count == 0 || dl == null || dl.Count == 0) return;
            // real time: skip frames the screen had no time to show, keep the leftover (it played slow and uneven on a TV at 20 fps)
            gifDelay += Mathf.Min(Time.deltaTime, 0.25f) * animationInfo.GetPlaybackSpeed();
            int start = gifFrame, guard = 0;
            while (guard++ < 64)
            {
                float targetDelay = Mathf.Max(0.02f, dl[Mathf.Min(gifFrame, dl.Count - 1)]);
                if (gifDelay < targetDelay) break;
                gifDelay -= targetDelay;
                if (gifFrame >= fr.Count - 1)
                {
                    if (animationInfo.freezeLastFrame) { isFrozen = true; break; }
                    gifFrame = 0;
                }
                else gifFrame++;
            }
            if (gifFrame != start) rawImage.texture = fr[gifFrame];
        }
    }

    public void RefreshGif()
    {
        if (animationInfo.isGif)
        {
            gifFrame = 0;
            gifDelay = 0;
            isFrozen = false;
            if (animationInfo.HasGifFrames())
            {
                rawImage.texture = animationInfo.gifFrames[0];
            }
        }
    }
}
