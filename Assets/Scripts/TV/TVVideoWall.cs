using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// A theme's video wallpaper plays on a TV again (it used to be replaced by one still picture from the video because a video
/// once froze a small projector). It plays without sound and drops frames rather than slowing the menu down. If a TV can't
/// keep up, that theme goes back to the still picture by itself and stays that way:
///  - the menu runs under 9 frames a second for 6 seconds while the video plays (idle cooling's slower rate is not counted), or
///  - the video reports an error, or
///  - Revo was shut down (froze / was killed) within 20 seconds of starting that video last time.
/// </summary>
public class TVVideoWall : MonoBehaviour
{
    private const string StillKey = "tv.wall.still3.";   // + theme file: 1 = this TV shows a still picture for that theme
    private const string StrikeKey = "tv.wall.strikes.";  // + theme file: starts in a row that ended with Revo gone while the video was starting
    private const string TryingKey = "tv.wall.trying";  // the theme whose video started last; cleared once it played fine
    private static bool crashChecked;
    private static string sessionStill; // couldn't play this time only
    private static TVVideoWall me;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVVideoWall");
        DontDestroyOnLoad(go);
        me = go.AddComponent<TVVideoWall>();
    }

    /// <summary>May this theme's video wallpaper play on this TV?</summary>
    public static bool Allowed(string theme)
    {
        if (string.IsNullOrEmpty(theme)) return false;
        if (!crashChecked)
        {
            crashChecked = true;
            if (PlayerPrefs.GetString(TryingKey, "") == theme)
            {
                // once can be anything (the TV switched off, Revo was closed); twice in a row is the video
                int strikes = PlayerPrefs.GetInt(StrikeKey + theme, 0) + 1;
                PlayerPrefs.SetInt(StrikeKey + theme, strikes); PlayerPrefs.DeleteKey(TryingKey); PlayerPrefs.Save();
                Debug.Log("[Wallpaper] Revo stopped while the video of " + theme + " was starting last time (" + strikes + " in a row)");
                if (strikes >= 2) MarkStill(theme);
            }
        }
        return theme != sessionStill && PlayerPrefs.GetInt(StillKey + theme, 0) == 0;
    }

    /// <summary>Revo is closing on purpose (a restart for a theme or look): not the video's fault.</summary>
    public static void ClosingNormally()
    {
        if (!PlayerPrefs.HasKey(TryingKey)) return;
        PlayerPrefs.DeleteKey(TryingKey); PlayerPrefs.Save();
    }

    private void OnApplicationPause(bool paused) { if (paused) ClosingNormally(); }
    private void OnApplicationQuit() { ClosingNormally(); }

    private static void MarkStill(string theme)
    {
        PlayerPrefs.SetInt(StillKey + theme, 1);
        PlayerPrefs.DeleteKey(TryingKey);
        PlayerPrefs.Save();
    }

    private VideoPlayer vp;
    private Texture lastTex;
    private static bool resting, slideHeld;
    /// <summary>Holds the video still while the bar is a still picture (page slide), so the whole wall stays in step.</summary>
    public static void HoldForSlide(bool on)
    {
        if (me == null || me.vp == null || me.playing == null) { slideHeld = false; return; }
        if (on && me.vp.isPlaying) { me.vp.Pause(); slideHeld = true; }
        else if (!on && slideHeld) { slideHeld = false; if (!resting) me.vp.Play(); }
    }
    /// <summary>Holds the video on its frame (idle) or plays it again.</summary>
    public static void Rest(bool on)
    {
        if (on == resting || me == null || me.vp == null) return;
        if (on && (me.playing == null || !me.vp.isPlaying)) return;
        resting = on;
        if (on) me.vp.Pause(); else if (me.playing != null) me.vp.Play();
        Debug.Log("[Wallpaper] video " + (on ? "resting (idle)" : "playing again"));
    }
    /// <summary>A theme's video wallpaper is playing right now.</summary>
    public static bool Playing => me != null && me.playing != null && me.vp != null && me.vp.isPlaying;
    private string playing;
    private float startedAt, slowSince = -1f, frames, frameTime;
    private bool hooked;

    private void Update()
    {
        var wc = WallpaperController._instance;
        vp = wc != null ? wc.videoPlayer : null;
        if (vp == null) return;
        if (!hooked)
        {
            hooked = true;
            // a file that isn't there is not this TV's fault (only a video it can't decode is)
            vp.errorReceived += (v, msg) => GiveUp("the video couldn't be played (" + msg + ")", !string.IsNullOrEmpty(v.url) && System.IO.File.Exists(v.url));
        }

        string theme = wc.currentWallpaper != null && wc.currentWallpaper.isVideo && ThemeController._instance != null && ThemeController._instance.currentTheme != null
            ? ThemeController._instance.currentTheme.GetImportedFileName() : null;
        if (theme != playing)
        {
            playing = theme; slowSince = -1f; frames = frameTime = 0f;
            if (theme != null)
            {
                vp.audioOutputMode = VideoAudioOutputMode.None; // the menu has its own music
                vp.skipOnDrop = true;
                vp.isLooping = true;
                startedAt = Time.unscaledTime;
                PlayerPrefs.SetString(TryingKey, theme); PlayerPrefs.Save();
                Debug.Log("[Wallpaper] playing the video of " + theme);
            }
        }
        // the video's picture (made once the video is ready) goes to every wallpaper showing it
        if (theme != null && vp.texture != lastTex) { lastTex = vp.texture; if (lastTex != null) ThemedWallpaper.VideoTextureChanged(); }
        if (theme == null || !vp.isPlaying) return;

        // played fine for a while: a later shutdown isn't the video's fault
        if (startedAt > 0f && Time.unscaledTime - startedAt > 20f && PlayerPrefs.GetString(TryingKey, "") == theme)
        {
            PlayerPrefs.DeleteKey(TryingKey); PlayerPrefs.DeleteKey(StrikeKey + theme); PlayerPrefs.Save();
            Debug.Log("[Wallpaper] the video plays fine on this TV");
        }

        // too slow? (measured over 1 s windows; idle cooling runs slower on purpose and doesn't count)
        if (TVPerf.Cooling || Time.unscaledTime - startedAt < 4f) { slowSince = -1f; frames = frameTime = 0f; return; }
        frames++; frameTime += Time.unscaledDeltaTime;
        if (frameTime < 1f) return;
        float fps = frames / frameTime; frames = frameTime = 0f;
        if (fps >= 9f) { slowSince = -1f; return; }
        if (slowSince < 0f) slowSince = Time.unscaledTime;
        else if (Time.unscaledTime - slowSince > 6f) GiveUp("the menu ran at " + fps.ToString("0.0") + " fps with it", false); // a busy moment: this time only
    }

    private void GiveUp(string why, bool remember)
    {
        if (playing == null) return;
        Debug.Log("[Wallpaper] " + why + ": " + playing + " shows a still picture " + (remember ? "from now on" : "this time"));
        if (remember) MarkStill(playing);
        else { PlayerPrefs.DeleteKey(TryingKey); PlayerPrefs.SetInt(StillKey + playing, 0); sessionStill = playing; }
        playing = null;
        var wc = WallpaperController._instance;
        if (wc != null) wc.ResetBackgrounds();
    }
}
