using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// "Make a theme" in the Channel Server: pick a wallpaper, sounds, home music and a highlight colour from a phone or computer,
/// press Save, and the TV builds an ordinary Revo theme zip (the same format the Content Hub uses) and switches to it.
/// It starts from the theme in use (or Revo's own look), so everything not picked stays as it was. The parts picked so far
/// wait in persistentDataPath/ThemeMaker until saved or cleared.
/// </summary>
public static class TVThemeMaker
{
    // what the page offers -> the theme's own sound slots (AudioLibrary names)
    private static readonly Dictionary<string, string[]> SoundSlots = new Dictionary<string, string[]>
    {
        { "click", new[] { "CLICK_BUTTON", "CLICK_CHANNEL", "CLICK_PLUSLESS", "CLICK_WARNING" } },
        { "back", new[] { "CLICK_BUTTON_BACK" } },
        { "hover", new[] { "HOVER_BUTTON", "HOVER_CHANNEL" } },
        { "open", new[] { "OPEN_CHANNEL" } },
        { "leave", new[] { "LEAVE_CHANNEL" } },
        { "start", new[] { "HOME_START_SOUND", "HOME_MENU_START" } },
        { "music", new[] { "HOME_MENU_LOOP" } },
        { "shopmusic", new[] { "SHOP_MUSIC_LOOP" } },
    };
    private static readonly string[] Pictures = { "wallpaper", "emptyslot" };

    private static string Dir => Path.Combine(Application.persistentDataPath, "ThemeMaker");

    private static string SlotFile(string slot)
    {
        if (!Directory.Exists(Dir)) return null;
        foreach (var f in Directory.GetFiles(Dir)) if (Path.GetFileName(f).StartsWith(slot + ".")) return f;
        return null;
    }

    public static object State()
    {
        var parts = new Dictionary<string, object>();
        foreach (var s in SoundSlots.Keys) parts[s] = OriginalName(s);
        foreach (var s in Pictures) parts[s] = OriginalName(s);
        var tc = ThemeController._instance;
        return TVTiles.D("parts", parts, "base", tc != null && tc.GetCurrentTheme() != null ? tc.GetCurrentTheme().name : "Revo", "glass", TVGlass.On);
    }

    private static string OriginalName(string slot)
    {
        var f = SlotFile(slot);
        if (f == null) return null;
        var n = f + ".name";
        return File.Exists(n) ? File.ReadAllText(n) : Path.GetFileName(f);
    }

    // one picked part (body = the file); clear = take it out again
    public static string SetPart(string slot, byte[] data, string fileName, bool clear)
    {
        if (string.IsNullOrEmpty(slot) || (!SoundSlots.ContainsKey(slot) && Array.IndexOf(Pictures, slot) < 0)) return "Unknown part";
        Directory.CreateDirectory(Dir);
        var old = SlotFile(slot);
        if (old != null) { File.Delete(old); if (File.Exists(old + ".name")) File.Delete(old + ".name"); }
        if (clear) return null;
        if (data == null || data.Length < 16) return "That file is empty";
        string ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        bool picture = Array.IndexOf(Pictures, slot) >= 0;
        if (picture && ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif") return "Pick a PNG, JPG or GIF picture";
        if (!picture && ext != ".mp3" && ext != ".ogg" && ext != ".wav") return "Pick an MP3, OGG or WAV sound";
        if (picture && data.Length > 12 * 1024 * 1024) return "That picture is too big (12 MB at most)";
        string path = Path.Combine(Dir, slot + ext);
        File.WriteAllBytes(path, data);
        File.WriteAllText(path + ".name", Path.GetFileName(fileName ?? (slot + ext)));
        return null;
    }

    // builds the theme and switches to it; colour is "#rrggbb" or empty
    public static string Save(string name, string color, bool fromDefault, int emptyPercent = 100, string barColor = null, string lineColor = null, string textColor = null, string buttonColor = null)
    {
        if (TVGlass.On) return "Custom themes only work in the classic menu. Turn Glass 2026 off on the TV first";
        name = (name ?? "").Trim();
        if (name.Length == 0) return "Give your theme a name";
        if (name.Length > 40) name = name.Substring(0, 40);
        var tc = ThemeController._instance;
        if (tc == null) return "The TV isn't ready yet";

        // the theme it starts from: the one in use, or Revo's own light look
        string baseZip = null;
        var cur = tc.GetCurrentTheme();
        if (!fromDefault && cur != null && !string.IsNullOrEmpty(cur.importedFileName)) baseZip = SaveManager.SAVE_FOLDER_THEMES + cur.importedFileName;
        if (baseZip == null || !File.Exists(baseZip))
            foreach (var f in Directory.GetFiles(SaveManager.SAVE_FOLDER_THEMES, "theme_riilight*.zip")) { baseZip = f; break; }
        if (baseZip == null || !File.Exists(baseZip)) return "Couldn't find a theme to start from";

        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string outName = "theme_tvmade_" + id + ".zip";
        string outPath = SaveManager.SAVE_FOLDER_THEMES + outName;
        try
        {
            JObject json;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var src = ZipFile.OpenRead(baseZip))
            using (var dst = ZipFile.Open(outPath, ZipArchiveMode.Create))
            {
                var tj = src.GetEntry("theme.json");
                if (tj == null) return "The starting theme has no theme.json";
                using (var r = new StreamReader(tj.Open())) json = JObject.Parse(r.ReadToEnd());

                // the new parts go in under their own names; a base file with the same name is left out
                var newFiles = new Dictionary<string, string>(); // slot -> name in the zip
                AddPart("wallpaper", dst, newFiles, used);
                var emptyFrames = EmptyFrames(dst, used);
                foreach (var slot in SoundSlots.Keys) AddPart(slot, dst, newFiles, used);

                foreach (var e in src.Entries)
                {
                    if (e.FullName == "theme.json" || e.FullName == "onlineinfo.json" || used.Contains(e.FullName) || e.FullName.EndsWith("/")) continue;
                    var ne = dst.CreateEntry(e.FullName, System.IO.Compression.CompressionLevel.Fastest);
                    using var a = e.Open(); using var b = ne.Open(); a.CopyTo(b);
                }

                json["name"] = name;
                json["id"] = id;
                json["lastEdited"] = DateTime.Now.ToString("o");
                json["isDefaultTheme"] = false;
                json.Remove("forkId"); json.Remove("lastBundle");
                if (newFiles.TryGetValue("wallpaper", out string wall))
                    json["wallpaper"] = new JObject { ["textureName"] = "wallpaper", ["imageFileName"] = wall, ["filePath"] = "", ["isGif"] = wall.EndsWith(".gif", StringComparison.OrdinalIgnoreCase), ["isVideo"] = false, ["gifFrame"] = 0, ["gifDelay"] = 0.0 };
                if (emptyFrames != null)
                {
                    var tex = json["replacementTextures"] as JArray ?? new JArray();
                    for (int i = tex.Count - 1; i >= 0; i--) { string tn = (string) tex[i]["textureName"]; if (tn == "empty1" || tn == "empty2" || tn == "empty3") tex.RemoveAt(i); }
                    for (int i = 0; i < 3; i++)
                        tex.Add(new JObject { ["textureName"] = "empty" + (i + 1), ["imageFileName"] = emptyFrames[i % emptyFrames.Count], ["filePath"] = "", ["isGif"] = false, ["gifFrame"] = 0, ["gifDelay"] = 0.0 });
                    json["replacementTextures"] = tex;
                }
                var audio = json["replacementAudio"] as JArray ?? new JArray();
                foreach (var kv in SoundSlots)
                {
                    if (!newFiles.TryGetValue(kv.Key, out string file)) continue;
                    foreach (var clip in kv.Value)
                    {
                        for (int i = audio.Count - 1; i >= 0; i--) if ((string) audio[i]["audioClipName"] == clip) audio.RemoveAt(i);
                        audio.Add(new JObject { ["audioClipName"] = clip, ["audioFileName"] = file, ["filePath"] = "" });
                    }
                }
                json["replacementAudio"] = audio;
                if (!string.IsNullOrEmpty(color) && ColorUtility.TryParseHtmlString(color, out Color c))
                {
                    var map = json["colorMap"] as JObject ?? new JObject();
                    foreach (var key in new[] { "MainAccent", "ChannelHighlight", "HubAccent", "ChannelBorders" })
                        map[key] = new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = 1.0 };
                    json["colorMap"] = map;
                }
                // the bottom bar: its body, the line along its curve, the time / date and its round buttons
                var bmap = json["colorMap"] as JObject ?? new JObject();
                void Put(string hex, params string[] keys)
                {
                    if (string.IsNullOrEmpty(hex) || !ColorUtility.TryParseHtmlString(hex, out Color bc)) return;
                    foreach (var key in keys) bmap[key] = new JObject { ["r"] = bc.r, ["g"] = bc.g, ["b"] = bc.b, ["a"] = 1.0 };
                }
                Put(barColor, "HomeBar", "HomeClip");
                Put(lineColor, "HomeClipBorders", "MainAccent");
                Put(textColor, "ClockText", "DateText");
                Put(buttonColor, "HomeCircleButtonsBackground");
                if (bmap.Count > 0) json["colorMap"] = bmap;
                TVThemeExtras.Write(json, emptyPercent);
                var te = dst.CreateEntry("theme.json", System.IO.Compression.CompressionLevel.Fastest);
                using (var w = new StreamWriter(te.Open())) w.Write(json.ToString(Newtonsoft.Json.Formatting.None));
            }
        }
        catch (Exception e)
        {
            try { File.Delete(outPath); } catch (Exception) { }
            Debug.LogError("[ThemeMaker] " + e);
            return "Couldn't build the theme: " + e.Message;
        }
        Debug.Log("[ThemeMaker] built " + outName + " from " + Path.GetFileName(baseZip));
        // the parts are used up: the next theme starts fresh
        try { Directory.Delete(Dir, true); } catch (Exception) { }
        TVThemes.SwitchTo(outName, SlotWasWallpaper(outPath));
        return null;
    }

    private static bool SlotWasWallpaper(string zip)
    {
        try { using var z = ZipFile.OpenRead(zip); using var r = new StreamReader(z.GetEntry("theme.json").Open()); return JObject.Parse(r.ReadToEnd())["wallpaper"] is JObject; }
        catch (Exception) { return false; }
    }

    // the picked empty-slot picture as up to three PNG frames in the zip (a GIF: three frames spread over it)
    private static List<string> EmptyFrames(ZipArchive dst, HashSet<string> used)
    {
        var f = SlotFile("emptyslot");
        if (f == null) return null;
        var names = new List<string>();
        if (Path.GetExtension(f).ToLowerInvariant() == ".gif")
        {
            var frames = new List<Texture2D>(); var delays = new List<float>();
            StaticUtils.LoadGif(f, frames, delays);
            if (frames.Count > 0)
            {
                int n = Mathf.Min(3, frames.Count);
                for (int i = 0; i < n; i++)
                {
                    var t = frames[i * frames.Count / n];
                    string entry = "tv_empty" + (i + 1) + ".png";
                    var ze = dst.CreateEntry(entry, System.IO.Compression.CompressionLevel.Fastest);
                    using (var w = ze.Open()) { var png = t.EncodeToPNG(); w.Write(png, 0, png.Length); }
                    names.Add(entry); used.Add(entry);
                }
            }
            foreach (var t in frames) UnityEngine.Object.Destroy(t);
            if (names.Count > 0) return names;
        }
        string one = "tv_empty" + Path.GetExtension(f).ToLowerInvariant();
        dst.CreateEntryFromFile(f, one, System.IO.Compression.CompressionLevel.Fastest);
        used.Add(one); names.Add(one);
        return names;
    }

    private static void AddPart(string slot, ZipArchive dst, Dictionary<string, string> newFiles, HashSet<string> used)
    {
        var f = SlotFile(slot);
        if (f == null) return;
        string entry = "tv_" + slot + Path.GetExtension(f).ToLowerInvariant();
        dst.CreateEntryFromFile(f, entry, System.IO.Compression.CompressionLevel.Fastest);
        newFiles[slot] = entry;
        used.Add(entry);
    }
}
