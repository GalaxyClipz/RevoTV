using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Animations;
using Data.ChannelTargets;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Changes to the home grid that don't need the phone's touch editor: put an installed app in a slot, move or swap tiles,
/// remove one, rename it, and give a tile or its channel preview a picture or GIF. Used by the Channel Server
/// (phone / PC page) and the TV's own Move. Everything runs on Unity's main thread and saves straight away.
/// </summary>
public static class TVTiles
{
    public const string CustomTag = "tvcustom"; // pictures made here: icon_tvcustom_<id>.zip / banner_tvcustom_<id>.zip
    private const string DefaultIcon = "icon_defaultstatic_1630688976077.zip";
    private const string DefaultBanner = "banner_defaultstatic_1630695836374.zip";

    // the drawing areas the menu uses: an icon is 940 x 688 units; a preview is 1920 units tall and, on a 16:9 TV, as
    // wide as the screen (the panel is widened to the screen, TVLayout)
    private static readonly Vector2 IconArea = new Vector2(940f, 688f);
    private static readonly Vector2 BannerArea = new Vector2(1920f * 16f / 9f, 1920f);

    public static bool Busy()
    {
        var gc = GridController._instance;
        return gc == null || ChannelController._instance == null || gc.isChangingGrid || gc.IsLiftingChannel()
               || AppController.IsTransitioning() || ChannelController._instance.currentChannel != null;
    }

    public static Channel At(int grid, int pos)
    {
        foreach (var c in ChannelController._instance.loadedChannels)
            if (c.gridNumber == grid && c.position == pos) return c;
        return null;
    }

    public static int Pages => GridController._instance.GetNumberOfGrids();
    public static int Slots => GridController._instance.GetMaxAppsNumber();

    private static bool Valid(int grid, int pos) => grid >= 0 && grid < Pages && pos >= 0 && pos < Slots;

    // ---------------------------------------------------------------- add

    /// <returns>null when done, otherwise why not</returns>
    public static string AddApp(int grid, int pos, string package)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        if (!Valid(grid, pos)) return "That slot doesn't exist";
        if (At(grid, pos) != null) return "That slot already has a channel";
        var app = FindApp(package);
        if (app == null) return "That app isn't installed";
        var icon = ChannelController._instance.GetOrLoadChannelAnimation(DefaultIcon);
        var banner = ChannelController._instance.GetOrLoadChannelAnimation(DefaultBanner);
        if (icon == null || banner == null) return "Revo's default artwork is missing";

        var channel = new Channel { gridNumber = grid, position = pos };
        var target = new ChannelAppTarget();
        channel.target = target;
        target.LinkChannel(channel);
        channel.info = app;
        channel.SetIconAnimation(icon);
        channel.SetBannerAnimation(banner);
        channel.isFullySetup = true;
        ChannelController._instance.InitializeNewChannel(channel);
        Debug.Log("[Tiles] added " + package + " at page " + grid + " slot " + pos);
        return null;
    }

    /// <summary>Adds one of Revo's own channels (an app that isn't installed on the TV): its name and picture come from here.</summary>
    public static string AddBuiltin(int grid, int pos, string package, string name, byte[] picture)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        if (!Valid(grid, pos)) return "That slot doesn't exist";
        if (At(grid, pos) != null) return "That slot already has a channel";
        var icon = ChannelController._instance.GetOrLoadChannelAnimation(DefaultIcon);
        var banner = ChannelController._instance.GetOrLoadChannelAnimation(DefaultBanner);
        if (icon == null || banner == null) return "Revo's default artwork is missing";
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(picture, false);
        var channel = new Channel { gridNumber = grid, position = pos };
        var target = new ChannelAppTarget();
        channel.target = target;
        target.LinkChannel(channel);
        channel.info = new AppInfo(name, package, tex);
        channel.SetIconAnimation(icon);
        channel.SetBannerAnimation(banner);
        channel.isFullySetup = true;
        ChannelController._instance.InitializeNewChannel(channel);
        Debug.Log("[Tiles] added " + package + " at page " + grid + " slot " + pos);
        return null;
    }

    // Weather, Photos and Insert Disk are part of Revo itself: their app can't be changed or picked from the installed ones
    public static bool IsLocked(string package)
    {
        return package == "revo.weather" || package == "revo.gallery" || package == "wiiphone.gallery"
            || package == "revo.disc" || package == "wiiphone.disc";
    }

    public static AppInfo FindApp(string package)
    {
        var linker = AndroidLinker._instance;
        if (linker == null || string.IsNullOrEmpty(package)) return null;
        if (linker.apps.Count == 0 || !linker.IsAppCountAccurate()) linker.FindAllApps();
        foreach (var a in linker.apps) if (a.packageName == package) return a;
        return null;
    }

    // ---------------------------------------------------------------- move / swap

    public static string Move(int fromGrid, int fromPos, int toGrid, int toPos)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        if (!Valid(fromGrid, fromPos) || !Valid(toGrid, toPos)) return "That slot doesn't exist";
        if (fromGrid == toGrid && fromPos == toPos) return null;
        var a = At(fromGrid, fromPos);
        if (a == null) return "There's no channel to move there";
        var b = At(toGrid, toPos); // swapped when the target is taken

        Lift(a);
        if (b != null) Lift(b);
        a.gridNumber = toGrid; a.position = toPos;
        if (b != null) { b.gridNumber = fromGrid; b.position = fromPos; }
        Place(a);
        if (b != null) Place(b);
        AnimationController._instance.RestartIconAnimations();
        if (runner == null) { var go = new GameObject("TVTilesRunner"); UnityEngine.Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
        runner.StartCoroutine(Reconcile());
        Debug.Log("[Tiles] moved page " + fromGrid + " slot " + fromPos + " to page " + toGrid + " slot " + toPos + (b != null ? " (swapped)" : ""));
        return null;
    }

    // Slot order is the child order of each page, but a tile removed this frame still counts as a child until the frame
    // ends. A moment after a move, drop any empty slot that shares a slot with a tile, add the ones that are missing and
    // put every child back in slot order.
    private class Runner : MonoBehaviour { }
    private static Runner runner;

    private static IEnumerator Reconcile()
    {
        yield return null;
        yield return null;
        var gc = GridController._instance;
        if (gc == null) yield break;
        var pages = UnityEngine.Object.FindObjectsByType<Grid>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var g in pages)
        {
            if (g.innerGrid == null) continue;
            var sb = new System.Text.StringBuilder("[Tiles] page " + g.number + " children:");
            foreach (Transform ch in g.innerGrid)
            {
                var ah = ch.GetComponent<AppHandler>();
                var eh = ch.GetComponent<EmptyAppHandler>();
                sb.Append(' ').Append(ah != null ? "A" + (ah.channel != null ? ah.channel.position.ToString() : "?") : eh != null ? "E" + eh.position : "X:" + ch.name);
            }
            Debug.Log(sb.ToString());
        }
        foreach (var g in pages)
        {
            if (g.innerGrid == null) continue;
            var taken = new HashSet<int>();
            foreach (Transform ch in g.innerGrid)
            {
                var ah = ch.GetComponent<AppHandler>();
                if (ah != null && ah.channel != null) taken.Add(ah.channel.position);
            }
            var seen = new HashSet<int>();
            foreach (Transform ch in g.innerGrid)
            {
                var eh = ch.GetComponent<EmptyAppHandler>();
                if (eh == null) continue;
                if (taken.Contains(eh.position) || !seen.Add(eh.position))
                {
                    g.emptyChannels.Remove(eh);
                    UnityEngine.Object.Destroy(ch.gameObject);
                }
            }
        }
        yield return null;
        int max = gc.GetMaxAppsNumber();
        foreach (var g in pages)
        {
            if (g.innerGrid == null) continue;
            var slots = new Dictionary<int, Transform>();
            foreach (Transform ch in g.innerGrid)
            {
                var ah = ch.GetComponent<AppHandler>();
                var eh = ch.GetComponent<EmptyAppHandler>();
                int pos = ah != null && ah.channel != null ? ah.channel.position : eh != null ? eh.position : -1;
                if (pos >= 0 && !slots.ContainsKey(pos)) slots[pos] = ch;
            }
            for (int pos = 0; pos < max; pos++)
                if (!slots.ContainsKey(pos)) gc.SpawnEmptyChannel(g.number, pos);
            var order = new List<KeyValuePair<int, Transform>>();
            foreach (Transform ch in g.innerGrid)
            {
                var ah = ch.GetComponent<AppHandler>();
                var eh = ch.GetComponent<EmptyAppHandler>();
                int pos = ah != null && ah.channel != null ? ah.channel.position : eh != null ? eh.position : int.MaxValue;
                order.Add(new KeyValuePair<int, Transform>(pos, ch));
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < order.Count; i++) order[i].Value.SetSiblingIndex(i);
        }
    }

    // off the grid (an empty slot takes its place), as the phone's lift does
    private static void Lift(Channel c)
    {
        if (c.appHandler != null)
        {
            // the old tile is only destroyed at the end of the frame: moved to the end of its page first, so the slots
            // (child order = slot number) stay right for the empty slot and tile put in during this same frame
            c.appHandler.transform.SetAsLastSibling();
            GridController._instance.RemoveChannelFromGrid(c.appHandler);
        }
        c.appHandler = null;
    }

    // back on the grid at its (new) slot, as the phone's lay-down does
    private static void Place(Channel c)
    {
        GridController._instance.InsertChannel(c);
        c.LinkAnimations();
        c.Save();
    }

    // ---------------------------------------------------------------- remove / rename

    public static string Remove(int grid, int pos)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        var c = At(grid, pos);
        if (c == null) return "That slot is already empty";
        if (c.appHandler != null) GridController._instance.DeleteChannel(c.appHandler);
        else ChannelController._instance.DeleteChannel(c);
        Debug.Log("[Tiles] removed page " + grid + " slot " + pos);
        return null;
    }

    public static string Rename(int grid, int pos, string name)
    {
        var c = At(grid, pos);
        if (c == null) return "That slot is empty";
        name = (name ?? "").Trim();
        if (name.Length > 48) name = name.Substring(0, 48);
        c.SetTag(name.Length == 0 ? null : name);
        return null;
    }

    // ---------------------------------------------------------------- pictures

    /// <summary>
    /// Makes a one-picture animation (a still image or a GIF) and puts it on the tile (icon) or its preview (banner).
    /// fill: the picture covers the whole area (edges may be cropped); otherwise it fits inside it on a plain background.
    /// </summary>
    // the Forecast Channel's preview (its live sky and chime, TVWeatherPreview) is built in: only Revo itself writes it
    public static bool IsBuiltInPreview(Channel c) => c != null && c.info != null && c.info.packageName == TVWeather.Package;
    private static bool builtInWrite;
    public static string SetBuiltInPicture(int grid, int pos, CHANNELTYPE type, byte[] data, bool gif, bool fill)
    {
        builtInWrite = true;
        try { return SetPicture(grid, pos, type, data, gif, fill); } finally { builtInWrite = false; }
    }

    public static string SetPicture(int grid, int pos, CHANNELTYPE type, byte[] data, bool gif, bool fill)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        var c = At(grid, pos);
        if (c == null) return "That slot is empty: add an app first";
        if (type == CHANNELTYPE.BANNER && !builtInWrite && IsBuiltInPreview(c)) return "The Forecast Channel's preview is built in (its tile picture can still be changed)";
        if (data == null || data.Length < 16) return "That file is empty";

        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string kind = type == CHANNELTYPE.ICON ? "icon" : "banner";
        string fileName = kind + "_" + CustomTag + "_" + id + ".zip";
        string work = Path.Combine(Application.temporaryCachePath, "tvart_" + id);
        Directory.CreateDirectory(work);
        try
        {
            string image = gif ? "picture.gif" : "picture.png";
            Vector2Int size;
            if (gif)
            {
                if (!GifSize(data, out size)) return "That GIF couldn't be read";
                File.WriteAllBytes(Path.Combine(work, image), data);
            }
            else
            {
                // big photos are scaled down first: the TV only shows them at screen size, and memory is tight
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data, false)) { UnityEngine.Object.Destroy(tex); return "That picture couldn't be read (use PNG, JPG or GIF)"; }
                int max = type == CHANNELTYPE.ICON ? 1024 : 1920;
                var small = Shrink(tex, max);
                File.WriteAllBytes(Path.Combine(work, image), small.EncodeToPNG());
                size = new Vector2Int(small.width, small.height);
                if (small != tex) UnityEngine.Object.Destroy(small);
                UnityEngine.Object.Destroy(tex);
            }

            Vector2 area = type == CHANNELTYPE.ICON ? IconArea : BannerArea;
            float s = fill ? Mathf.Max(area.x / size.x, area.y / size.y) : Mathf.Min(area.x / size.x, area.y / size.y);
            // a fitted icon sits on Wii-white, a fitted preview on black
            var back = type == CHANNELTYPE.ICON || fill ? Color.white : new Color(0.03f, 0.03f, 0.04f, 1f);
            string json = AnimationJson(type, image, gif, s, back, id);
            if (type == CHANNELTYPE.BANNER && OldSound(c, out byte[] snd, out string sndName, out bool loop))
            {
                File.WriteAllBytes(Path.Combine(work, sndName), snd);
                var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
                doc["shouldLoopAudio"] = loop;
                json = doc.ToString(Formatting.None);
                Debug.Log("[Tiles] the new preview keeps its sound (" + sndName + ")");
            }
            File.WriteAllText(Path.Combine(work, "animation.json"), json);

            string zip = SaveManager.GetChannelSaveFolder(type) + fileName;
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(work, zip, System.IO.Compression.CompressionLevel.Fastest, false);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (Exception) { }
        }

        string err = Swap(c, type, fileName);
        if (err != null) return err;
        Debug.Log("[Tiles] " + kind + " picture " + fileName + " on page " + grid + " slot " + pos + " (" + (gif ? "gif" : "image") + ", " + (fill ? "fill" : "fit") + ")");
        return null;
    }

    // puts an animation file (already in the animations folder) on a tile (icon) or its preview (banner)
    private static string Swap(Channel c, CHANNELTYPE type, string fileName)
    {
        var anim = ChannelController._instance.GetOrLoadChannelAnimation(fileName);
        if (anim == null) return "The picture was saved but couldn't be loaded";
        string old = c.GetAnimationName(type);
        bool drawn = c.appHandler != null;
        if (drawn) Lift(c);
        c.UnlinkAnimations();
        if (type == CHANNELTYPE.ICON) c.SetIconAnimation(anim); else c.SetBannerAnimation(anim);
        if (drawn) Place(c); else c.Save();
        AnimationController._instance.RestartIconAnimations();
        DropUnused(old);
        return null;
    }

    // ---------------------------------------------------------------- preview sound

    private static readonly string[] SoundTypes = { "wav", "mp3", "ogg" };

    private static string BannerZip(Channel c)
    {
        string name = c.GetAnimationName(CHANNELTYPE.BANNER);
        return string.IsNullOrEmpty(name) ? null : SaveManager.GetChannelSaveFolder(CHANNELTYPE.BANNER) + name;
    }

    /// <summary>Whether the channel's preview plays a sound when opened.</summary>
    public static bool HasSound(int grid, int pos)
    {
        var c = At(grid, pos);
        string zip = c == null ? null : BannerZip(c);
        if (zip == null || !File.Exists(zip)) return false;
        return ZipHasSound(zip);
    }

    public static byte[] SoundBytes(int grid, int pos, out string type)
    {
        type = null;
        var c = At(grid, pos);
        string zip = c == null ? null : BannerZip(c);
        if (zip == null || !File.Exists(zip)) return null;
        try
        {
            using var z = ZipFile.OpenRead(zip);
            foreach (var e in z.Entries)
            {
                if (!e.FullName.StartsWith("audio.", StringComparison.OrdinalIgnoreCase)) continue;
                string ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                type = ext == ".mp3" ? "audio/mpeg" : ext == ".ogg" ? "audio/ogg" : ext == ".wav" ? "audio/wav" : "application/octet-stream";
                using var st = e.Open(); using var ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray();
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>Gives a channel preview a sound (a WAV, MP3 or OGG file), or takes it away when data is null.</summary>
    public static string SetSound(int grid, int pos, byte[] data, string fileName)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        var c = At(grid, pos);
        if (c == null) return "That slot is empty: add an app first";
        if (IsBuiltInPreview(c)) return "The Forecast Channel's preview sound is built in";
        string ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        if (ext == "oga") ext = "ogg";
        if (data != null && (data.Length < 1024 || Array.IndexOf(SoundTypes, ext) < 0)) return "Use an MP3, OGG or WAV sound file";
        return PutSound(c, data, ext, data != null);
    }

    // whether a preview zip carries a sound (any "audio.*", also the ".bin" the Content Hub uses)
    private static bool ZipHasSound(string zip)
    {
        if (zip == null || !File.Exists(zip)) return false;
        try { using var z = ZipFile.OpenRead(zip); foreach (var e in z.Entries) if (Path.GetFileName(e.FullName).StartsWith("audio.", StringComparison.OrdinalIgnoreCase)) return true; }
        catch (Exception) { }
        return false;
    }

    // the sound of a channel's current preview (bytes, entry name, loops)
    private static bool OldSound(Channel c, out byte[] data, out string name, out bool loop)
    {
        data = null; name = null; loop = false;
        string zip = c == null ? null : BannerZip(c);
        if (zip == null || !File.Exists(zip)) return false;
        try
        {
            using var z = ZipFile.OpenRead(zip);
            foreach (var e in z.Entries)
            {
                string n = Path.GetFileName(e.FullName);
                if (!n.StartsWith("audio.", StringComparison.OrdinalIgnoreCase)) continue;
                using (var st = e.Open()) using (var ms = new MemoryStream()) { st.CopyTo(ms); data = ms.ToArray(); }
                name = n.ToLowerInvariant();
            }
            if (data == null) return false;
            var js = z.GetEntry("animation.json");
            if (js != null) { using var rd = new StreamReader(js.Open()); var doc = Newtonsoft.Json.Linq.JObject.Parse(rd.ReadToEnd()); loop = doc["shouldLoopAudio"] != null && (bool) doc["shouldLoopAudio"]; }
            return true;
        }
        catch (Exception) { data = null; return false; }
    }

    private static string PutSound(Channel c, byte[] data, string ext, bool loop)
    {
        string src = BannerZip(c);
        if (src == null || !File.Exists(src)) return "This channel's preview file wasn't found";

        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string fileName2 = "banner_" + CustomTag + "_" + id + ".zip";
        string dst = SaveManager.GetChannelSaveFolder(CHANNELTYPE.BANNER) + fileName2;
        try
        {
            File.Copy(src, dst, true);
            using (var z = ZipFile.Open(dst, ZipArchiveMode.Update))
            {
                foreach (var e in new List<ZipArchiveEntry>(z.Entries))
                    if (Path.GetFileName(e.FullName).StartsWith("audio.", StringComparison.OrdinalIgnoreCase)) e.Delete();
                if (data != null)
                {
                    var entry = z.CreateEntry("audio." + ext, System.IO.Compression.CompressionLevel.NoCompression);
                    using var st = entry.Open();
                    st.Write(data, 0, data.Length);
                }
                var js = z.GetEntry("animation.json");
                if (js != null)
                {
                    Newtonsoft.Json.Linq.JObject doc;
                    using (var rd = new StreamReader(js.Open())) doc = Newtonsoft.Json.Linq.JObject.Parse(rd.ReadToEnd());
                    doc["shouldLoopAudio"] = data != null && loop;
                    if (doc["id"] != null) doc["id"] = id;
                    js.Delete();
                    var ne = z.CreateEntry("animation.json");
                    using var wr = new StreamWriter(ne.Open());
                    wr.Write(doc.ToString(Formatting.None));
                }
            }
        }
        catch (Exception e)
        {
            try { File.Delete(dst); } catch (Exception) { }
            Debug.LogWarning("[Tiles] sound: " + e.Message);
            return "The sound couldn't be added to this preview";
        }
        string err = Swap(c, CHANNELTYPE.BANNER, fileName2);
        if (err != null) { try { File.Delete(dst); } catch (Exception) { } return err; }
        Debug.Log("[Tiles] preview sound " + (data != null ? ext + " " + data.Length + " bytes" : "removed") + " on page " + c.gridNumber + " slot " + c.position);
        return null;
    }

    // ---------------------------------------------------------------- Content Hub pictures

    /// <summary>Puts a downloaded hub animation on a tile (icon) or its preview (banner).</summary>
    public static string ApplyHub(int grid, int pos, CHANNELTYPE type, string animationId)
    {
        if (Busy()) return "The TV is busy, try again in a moment";
        var c = At(grid, pos);
        if (c == null) return "That slot is empty: add an app first";
        string fileName = (type == CHANNELTYPE.ICON ? "icon_" : "banner_") + animationId + ".zip";
        if (!File.Exists(SaveManager.GetChannelSaveFolder(type) + fileName)) return "That picture isn't on the TV yet";
        // a hub preview without a sound of its own keeps the sound this channel's preview had
        bool keep = type == CHANNELTYPE.BANNER && !ZipHasSound(SaveManager.GetChannelSaveFolder(type) + fileName) && OldSound(c, out _, out _, out _);
        byte[] kept = null; string keptName = null; bool keptLoop = false;
        if (keep) OldSound(c, out kept, out keptName, out keptLoop);
        string err = Swap(c, type, fileName);
        if (err == null) Debug.Log("[Tiles] hub " + fileName + " on page " + grid + " slot " + pos);
        if (err == null && keep) PutSound(c, kept, keptName.Substring(keptName.LastIndexOf('.') + 1), keptLoop);
        return err;
    }

    // a picture made here that no channel uses any more is deleted (built-in and downloaded artwork is never touched)
    private static void DropUnused(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !fileName.Contains("_" + CustomTag + "_")) return;
        foreach (var c in ChannelController._instance.loadedChannels)
            if (c.iconName == fileName || c.bannerName == fileName) return;
        var anim = ChannelController._instance.GetLoadedChannelAnimation(fileName);
        if (anim != null)
        {
            ChannelController._instance.DeleteChannelAnimation(anim);
            ChannelController._instance.UnlistAnimation(anim);
        }
        else
        {
            SaveManager.DeleteChannelAnimationFile(fileName);
        }
    }

    // a still picture (first frame of a GIF) of what a tile shows, for the phone page; null when it has none
    public static byte[] Thumbnail(int grid, int pos)
    {
        var c = At(grid, pos);
        if (c == null) return null;
        try
        {
            if (!string.IsNullOrEmpty(c.iconName) && c.iconName.Contains("_" + CustomTag + "_") && c.iconAnimation != null)
            {
                foreach (var img in c.iconAnimation.images)
                {
                    var t = img.isGif && img.HasGifFrames() ? img.gifFrames[0] : img.image;
                    if (t != null) return Shrink(t.DuplicateTexture(), 256).EncodeToPNG();
                }
            }
            // built-in channels (revo.*) have no app icon of their own: the page shows their name instead
            if (c.info != null && c.info.icon != null && !(c.info.packageName ?? "").StartsWith("revo.")) return c.info.icon.DuplicateTexture().EncodeToPNG();
            // no app icon (or a built-in channel): the tile's own picture, as the TV shows it
            if (c.iconAnimation != null)
                foreach (var img in c.iconAnimation.images)
                {
                    var t = img.isGif && img.HasGifFrames() ? img.gifFrames[0] : img.image;
                    if (t != null) return Shrink(t.DuplicateTexture(), 256).EncodeToPNG();
                }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Tiles] thumbnail: " + e.Message);
        }
        return null;
    }

    private static Texture2D Shrink(Texture2D tex, int max)
    {
        if (tex.width <= max && tex.height <= max) return tex;
        float k = (float) max / Mathf.Max(tex.width, tex.height);
        int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(tex.height * k));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        outTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return outTex;
    }

    // the logical screen size from a GIF's header (bytes 6-9, little endian)
    private static bool GifSize(byte[] d, out Vector2Int size)
    {
        size = default;
        if (d.Length < 10 || d[0] != 'G' || d[1] != 'I' || d[2] != 'F') return false;
        size = new Vector2Int(d[6] | d[7] << 8, d[8] | d[9] << 8);
        return size.x > 0 && size.y > 0;
    }

    // key, value, key, value... (plain dictionaries: release builds strip what only reflection would read)
    public static Dictionary<string, object> D(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string) kv[i]] = kv[i + 1];
        return d;
    }

    private static string AnimationJson(CHANNELTYPE type, string image, bool gif, float scale, Color back, long id)
    {
        object Vec(float x, float y, float z) => D("x", x, "y", y, "z", z);
        object Col(Color c) => D("r", c.r, "g", c.g, "b", c.b, "a", c.a);
        object Img(string name, int layer, object sc, Color col, bool isGif) => D(
            "imageName", name, "isIcon", false, "isGif", isGif, "layer", layer, "animationList", new object[0], "startTime", 0f,
            "endTime", -1f, "initialColor", Col(col), "initialLocalPosition", Vec(0, 0, 0), "initialLocalScale", sc,
            "initialLocalRotation", Vec(0, 0, 0));
        Vector2 bg = type == CHANNELTYPE.ICON ? new Vector2(1024f, 1024f) : new Vector2(1920f * 16f / 9f, 1920f);
        var doc = D(
            "type", type.ToString(),
            "name", "My picture",
            "images", new[] { Img(image, 0, Vec(scale, scale, 1f), Color.white, gif) },
            "background", Img("Background", -100, Vec(bg.x, bg.y, 1f), back, false),
            "id", id,
            "lastEdited", DateTime.Now.ToString("o"),
            "shouldCacheTextures", true,
            "isLegacy", false,
            "duration", 5f,
            "shouldLoopAudio", false);
        return JsonConvert.SerializeObject(doc, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
    }
}
