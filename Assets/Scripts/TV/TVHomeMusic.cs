using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// A song of your own for the Home menu (and Settings), sent from the Channel Server page. One file in the app's own
/// folder; the menu's usual music comes back as soon as it is removed. Channel previews and the Content Hub shop keep their sounds.
/// </summary>
public static class TVHomeMusic
{
    private static readonly string[] Types = { "ogg", "mp3", "wav" };
    private static AudioClip clip;
    private static string loadedFrom;

    public static string FilePath
    {
        get
        {
            foreach (var t in Types)
            {
                string p = Path.Combine(Application.persistentDataPath, "homemusic." + t);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }

    public static bool Has => FilePath != null;

    public static string Name => FilePath == null ? null : Path.GetFileName(FilePath);

    /// <returns>null when saved, otherwise why not</returns>
    public static string Save(byte[] data, string fileName)
    {
        if (data == null || data.Length < 1024) return "That file is empty";
        string ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        if (ext == "oga") ext = "ogg";
        if (System.Array.IndexOf(Types, ext) < 0) return "Use an MP3, OGG or WAV file";
        Clear();
        File.WriteAllBytes(Path.Combine(Application.persistentDataPath, "homemusic." + ext), data);
        Debug.Log("[HomeMusic] saved " + ext + ", " + data.Length + " bytes");
        if (AudioController._instance != null) AudioController.RestartBackgroundAudio();
        return null;
    }

    public static void Clear()
    {
        foreach (var t in Types)
        {
            try { File.Delete(Path.Combine(Application.persistentDataPath, "homemusic." + t)); } catch (System.Exception) { }
        }
        if (clip != null) { Object.Destroy(clip); clip = null; loadedFrom = null; }
        if (AudioController._instance != null) AudioController.RestartBackgroundAudio();
    }

    /// <summary>The loaded song, or null (not set, or it couldn't be read).</summary>
    public static AudioClip Clip => clip;

    public static IEnumerator Load()
    {
        string path = FilePath;
        if (path == null) yield break;
        if (clip != null && loadedFrom == path) yield break;
        if (clip != null) { Object.Destroy(clip); clip = null; }
        bool done = false;
        AudioController.LoadAudio(path, c => { clip = c; loadedFrom = path; done = true; });
        float wait = 0f;
        while (!done && wait < 20f) { wait += Time.unscaledDeltaTime; yield return null; }
    }
}
