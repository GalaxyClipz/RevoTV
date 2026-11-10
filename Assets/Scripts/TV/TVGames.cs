using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Disc games for the Disc Channel: what a disc image is (its own header carries the real title, game ID and console for
/// GameCube and Wii discs), and the TV's "Games" folder that the Channel Server uploads into (streamed to disk, so a 4 GB Wii
/// disc never has to fit in memory). TVGameScreen lists them; TVChannelServer's Games section adds and removes them.
/// </summary>
public static class TVGames
{
    public static readonly string[] Extensions = { ".iso", ".gcm", ".gcz", ".wbfs", ".rvz", ".ciso", ".dol", ".elf" };

    public class Info
    {
        public string path, title, id, console, size, where;
        public bool recognized;
    }

    // free space where the games go (Android's own count: .NET's DriveInfo isn't available in this build); 0 = unknown
    public static long FreeBytes(string dir)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { Directory.CreateDirectory(dir); using var st = new AndroidJavaObject("android.os.StatFs", dir); return st.Call<long>("getAvailableBytes"); }
        catch (Exception) { }
#endif
        return 0;
    }

    public static readonly string[] Archives = { ".zip", ".7z" };
    public static bool IsArchive(string file) => Array.IndexOf(Archives, Path.GetExtension(file ?? "").ToLowerInvariant()) >= 0;

    // the biggest GameCube / Wii image inside a .zip or .7z is written next to it (streamed: a 4 GB game never sits in memory)
    public static string Unpack(string archive, string dir, long free, Action alive)
    {
        try { System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch (Exception) { }
        string part = null;
        try
        {
            using (var ar = SharpCompress.Archives.ArchiveFactory.Open(archive))
            {
                SharpCompress.Archives.IArchiveEntry best = null;
                foreach (var e in ar.Entries) if (!e.IsDirectory && IsGame(e.Key) && (best == null || e.Size > best.Size)) best = e;
                if (best == null) return "There's no GameCube or Wii game inside that archive (" + string.Join(" ", Extensions) + ")";
                if (free > 0 && free < best.Size + (64L << 20)) return "Not enough space on the TV to unpack it (" + Size(free) + " free, the game needs " + Size(best.Size) + ")";
                string name = Path.GetFileName(best.Key);
                part = Path.Combine(dir, name + ".part"); string final = Path.Combine(dir, name);
                using (var reader = ar.ExtractAllEntries())
                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.Key != best.Key) continue;
                        using (var src = reader.OpenEntryStream())
                        using (var f = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                        {
                            var buf = new byte[1 << 20]; int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0) { f.Write(buf, 0, n); alive?.Invoke(); }
                        }
                        break;
                    }
                if (!File.Exists(part)) return "Couldn't unpack the game from that archive";
                if (File.Exists(final)) File.Delete(final);
                File.Move(part, final);
                Debug.Log("[Games] unpacked " + name + " from " + Path.GetFileName(archive));
            }
            return null;
        }
        catch (Exception e)
        {
            try { if (part != null && File.Exists(part)) File.Delete(part); } catch (Exception) { }
            Debug.LogWarning("[Games] unpack failed: " + e.Message);
            return "Couldn't unpack it: " + e.Message;
        }
        finally { try { File.Delete(archive); } catch (Exception) { } }
    }

    public static bool IsGame(string file)
    {
        string ext = Path.GetExtension(file ?? "").ToLowerInvariant();
        return Array.IndexOf(Extensions, ext) >= 0;
    }

    // the folder uploads go to: shared storage (the emulator can read it there); the app's own folder when Revo can't write there
    public static string Folder
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string shared = "/storage/emulated/0/Games";
            try { Directory.CreateDirectory(shared); if (Directory.Exists(shared)) return shared; } catch (Exception) { }
#endif
            return Path.Combine(Application.persistentDataPath, "Games");
        }
    }

    public static Info Read(string path)
    {
        var info = new Info { path = path, where = path.StartsWith("/storage/emulated") || path.StartsWith(Application.persistentDataPath) ? "TV" : "USB" };
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try { info.size = Size(new FileInfo(path).Length); } catch (Exception) { info.size = ""; }
        try
        {
            // GameCube / Wii disc header: ID at 0, Wii magic at 0x18, GameCube magic at 0x1C, title at 0x20 (WBFS: the disc starts at 0x200)
            long start = ext == ".wbfs" ? 0x200 : 0;
            if (ext == ".iso" || ext == ".gcm" || ext == ".wbfs")
            {
                var head = new byte[0x60];
                using (var f = File.OpenRead(path)) { f.Seek(start, SeekOrigin.Begin); if (f.Read(head, 0, head.Length) < head.Length) head = null; }
                if (head != null)
                {
                    uint wii = (uint) (head[0x18] << 24 | head[0x19] << 16 | head[0x1A] << 8 | head[0x1B]);
                    uint gc = (uint) (head[0x1C] << 24 | head[0x1D] << 16 | head[0x1E] << 8 | head[0x1F]);
                    if (wii == 0x5D1C9EA3 || gc == 0xC2339F3D)
                    {
                        info.console = wii == 0x5D1C9EA3 ? "Wii" : "GameCube";
                        info.id = Encoding.ASCII.GetString(head, 0, 6).Trim('\0', ' ');
                        int n = 0; while (n < 0x40 && head[0x20 + n] != 0) n++;
                        string t = Encoding.UTF8.GetString(head, 0x20, n).Trim();
                        if (t.Length > 0) { info.title = t; info.recognized = true; }
                    }
                }
            }
        }
        catch (Exception e) { Debug.Log("[Games] " + Path.GetFileName(path) + ": " + e.Message); }
        if (info.console == null) info.console = ext == ".wbfs" || ext == ".rvz" ? "Wii / GameCube" : ext == ".dol" || ext == ".elf" ? "Homebrew" : "GameCube / Wii";
        if (info.title == null) info.title = CleanName(Path.GetFileNameWithoutExtension(path));
        return info;
    }

    // "Super_Game_(USA)_[!]" -> "Super Game"
    public static string CleanName(string n)
    {
        n = System.Text.RegularExpressions.Regex.Replace(n, @"[\(\[].*?[\)\]]", "");
        n = n.Replace('_', ' ').Replace('.', ' ');
        n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim();
        return n.Length > 0 ? n : "Game";
    }

    public static string Size(long b) => b >= 1L << 30 ? (b / (double) (1L << 30)).ToString("0.0") + " GB" : b >= 1L << 20 ? (b / (double) (1L << 20)).ToString("0") + " MB" : (b / 1024.0).ToString("0") + " KB";

    // the Games folder's contents, for the Channel Server page
    public static object List()
    {
        var list = new List<object>();
        string dir = Folder;
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!IsGame(f)) continue;
                var i = Read(f);
                list.Add(TVTiles.D("file", Path.GetFileName(f), "title", i.title, "console", i.console, "id", i.id, "size", i.size, "recognized", i.recognized));
            }
        return TVTiles.D("folder", dir, "games", list, "types", string.Join(" ", Extensions) + "  (or a .zip / .7z with the game inside)");
    }

    public static string Delete(string file)
    {
        if (string.IsNullOrEmpty(file) || file.Contains("/") || file.Contains("\\") || file.Contains("..")) return "Pick a game";
        string p = Path.Combine(Folder, file);
        if (!File.Exists(p) || !IsGame(p)) return "That game isn't in the Games folder";
        try { File.Delete(p); } catch (Exception e) { return "Couldn't delete it: " + e.Message; }
        return null;
    }

    // called on the server's own thread: the request body is copied straight from the network into the file, 1 MB at a time
    public static string Receive(Stream net, long length, string dir, long free, string fileName, Action alive = null)
    {
        fileName = Path.GetFileName(fileName ?? "").Trim();
        if (fileName.Length == 0 || fileName.Contains("..")) return "The file needs a name";
        if (!IsGame(fileName) && !IsArchive(fileName)) return "Only disc images (" + string.Join(" ", Extensions) + ") or a .zip / .7z with one inside can go here";
        if (length <= 0) return "That file is empty";
        string part = Path.Combine(dir, fileName + ".part"), final = Path.Combine(dir, fileName);
        try
        {
            Directory.CreateDirectory(dir);
            if (free > 0 && free < length + (64L << 20)) return "Not enough space on the TV (" + Size(free) + " free, the game needs " + Size(length) + ")";
            var buf = new byte[1 << 20];
            long left = length;
            using (var f = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                while (left > 0)
                {
                    int n = net.Read(buf, 0, (int) Math.Min(buf.Length, left));
                    if (n <= 0) throw new IOException("the upload stopped part way");
                    f.Write(buf, 0, n);
                    left -= n;
                    alive?.Invoke(); // the TV keeps showing the phone as connected while a big game is coming in
                }
            }
            if (File.Exists(final)) File.Delete(final);
            File.Move(part, final);
            Debug.Log("[Games] received " + fileName + " (" + Size(length) + ") into " + dir);
            if (IsArchive(final)) return Unpack(final, dir, free > 0 ? free - length : 0, alive);
            return null;
        }
        catch (Exception e)
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (Exception) { }
            Debug.LogWarning("[Games] upload failed: " + e.Message);
            return "The upload didn't finish: " + e.Message;
        }
    }
}
