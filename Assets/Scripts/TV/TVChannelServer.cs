using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Animations;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Channel Server (Preferences > Channel Server): a tiny web page on the home network for setting up the launcher from a
/// phone or computer - add installed apps, move, swap, rename and remove tiles, and give a tile or its channel preview a
/// picture or GIF. Off until switched on, and off again with Revo. The link shown on the TV carries a 4-digit code;
/// requests without it are refused. One background thread waits for connections, so it costs nothing while idle;
/// every change runs on Unity's main thread through TVTiles.
/// </summary>
public static class TVChannelServer
{
    private const int MaxUpload = 24 * 1024 * 1024; // pictures are limited to 12 MB by the page; music may be bigger
    private const int MaxGifPixels = 12 * 1024 * 1024; // frames x width x height: the TV keeps every GIF frame in memory

    private static TcpListener listener;
    private static string code, url, page;
    private static readonly Dictionary<string, byte[]> iconCache = new Dictionary<string, byte[]>();

    public static bool Running => listener != null;
    public static string Url => url;
    private static long lastRequest; // Stopwatch ticks of the last request with the right code, 0 = none yet
    private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    public static float SecondsSinceRequest
    {
        get
        {
            long last = Interlocked.Read(ref lastRequest);
            return last == 0 ? float.MaxValue : (float) ((clock.ElapsedTicks - last) / (double) System.Diagnostics.Stopwatch.Frequency);
        }
    }

    // ---------------------------------------------------------------- switch

    // the server runs only while its screen is open (TVServerScreen): Back closes both
    public static void ShowFromSettings() => TVServerScreen.Show();

    public static bool Start()
    {
        string ip = LocalIp();
        if (string.IsNullOrEmpty(ip)) return false;
        var page0 = Resources.Load<TextAsset>("ChannelServer/index");
        page = page0 != null ? page0.text : "<h1>Channel Server page missing</h1>";
        code = PlayerPrefs.GetString("tv.server.code", "");
        if (code.Length != 4) { code = UnityEngine.Random.Range(1000, 10000).ToString(); PlayerPrefs.SetString("tv.server.code", code); PlayerPrefs.Save(); }
        Interlocked.Exchange(ref lastRequest, 0);
        foreach (int port in new[] { 8080, 8081, 8088, 8090, 0 })
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                listener = l;
                int real = ((IPEndPoint) l.LocalEndpoint).Port;
                url = "http://" + ip + ":" + real + "/?k=" + code;
                break;
            }
            catch (Exception e)
            {
                Debug.Log("[Server] port " + port + " busy: " + e.Message);
            }
        }
        if (listener == null) return false;
        var t = new Thread(AcceptLoop) { IsBackground = true, Name = "Revo Channel Server" };
        t.Start(listener);
        Application.quitting -= Stop;
        Application.quitting += Stop;
        Debug.Log("[Server] on at " + url.Replace(code, "****"));
        return true;
    }

    public static void Stop()
    {
        var l = listener;
        listener = null;
        try { l?.Stop(); } catch (Exception) { }
        iconCache.Clear();
        Debug.Log("[Server] off");
    }

    private static string LocalIp()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var helper = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV");
            string ip = helper.CallStatic<string>("localIp", AndroidLinker.currentActivity);
            if (!string.IsNullOrEmpty(ip)) return ip;
        }
        catch (Exception e) { Debug.LogWarning("[Server] ip: " + e.Message); }
#endif
        try
        {
            // the address the TV would use to reach the network (nothing is sent)
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("10.255.255.255", 1);
            return ((IPEndPoint) s.LocalEndPoint).Address.ToString();
        }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------- HTTP

    private static void AcceptLoop(object state)
    {
        var l = (TcpListener) state;
        while (listener == l)
        {
            TcpClient client;
            try { client = l.AcceptTcpClient(); }
            catch (Exception) { break; } // stopped
            ThreadPool.QueueUserWorkItem(_ => Serve(client));
        }
    }

    private static void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 15000; client.SendTimeout = 15000;
                var stream = client.GetStream();
                if (!ReadRequest(stream, out string method, out string path, out var query, out byte[] body))
                {
                    Send(stream, 400, "text/plain", Encoding.UTF8.GetBytes("Bad request"));
                    return;
                }
                if (!query.TryGetValue("k", out string k) || k != code)
                {
                    Send(stream, 403, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(
                        "<meta name=viewport content='width=device-width'><body style='font-family:sans-serif;padding:24px'>" +
                        "<h2>Revo Channel Server</h2><p>Type the 4-digit code shown on the TV (Settings &gt; Channel Server):</p>" +
                        "<form method=get action='/'><input name=k inputmode=numeric pattern='[0-9]*' maxlength=4 autofocus style='font-size:28px;width:5em;padding:6px'> " +
                        "<button style='font-size:22px;padding:8px 18px'>Open</button></form>"));
                    return;
                }
                Interlocked.Exchange(ref lastRequest, clock.ElapsedTicks);
                Route(stream, method, path, query, body);
            }
            catch (Exception e)
            {
                Debug.Log("[Server] request failed: " + e.Message);
            }
        }
    }

    private static void Route(NetworkStream s, string method, string path, Dictionary<string, string> q, byte[] body)
    {
        int I(string key) => q.TryGetValue(key, out var v) && int.TryParse(v, out int n) ? n : -1;
        string S(string key) => q.TryGetValue(key, out var v) ? v : null;

        if (method == "GET") switch (path)
        {
            case "/":
            case "/index.html":
                Send(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page));
                return;
            case "/api/state":
                Json(s, OnMain(State));
                return;
            case "/api/hub/progress":
                Json(s, TVTiles.D("p", TVHubProxy.Progress));
                return;
            case "/api/apps":
                Json(s, OnMain(Apps));
                return;
            case "/api/appicon":
                Png(s, OnMain(() => AppIcon(S("pkg"))));
                return;
            case "/api/thumb":
                Png(s, OnMain(() => TVTiles.Thumbnail(I("page"), I("pos"))));
                return;
            case "/api/tileinfo":
                Json(s, OnMain(() => (object) TVTiles.D("sound", TVTiles.HasSound(I("page"), I("pos")))));
                return;
            case "/api/homemusic":
                Json(s, OnMain(() => (object) TVTiles.D("has", TVHomeMusic.Has, "name", TVHomeMusic.Name)));
                return;
            case "/api/tilesound":
            {
                string type = null;
                var snd = OnMain(() => TVTiles.SoundBytes(I("page"), I("pos"), out type));
                if (snd == null) Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("No sound"));
                else Send(s, 200, type, snd);
                return;
            }
            case "/api/maker":
                Json(s, OnMain(TVThemeMaker.State));
                return;
            case "/api/look":
                Json(s, OnMain(() => (object) TVTiles.D("glow", TVGlowColor.Current, "empty", TVThemeExtras.State())));
                return;
            case "/api/games":
                Json(s, OnMain(TVGames.List));
                return;
            case "/api/hub/mine":
                Json(s, OnMain(TVHubPost.State));
                return;
            case "/api/hub/search":
                Json(s, OnMainAsync(() => TVHubProxy.Search(S("type"), S("q"), S("sort"), Math.Max(1, I("hpage")))));
                return;
            case "/api/hub/thumb":
            {
                var t = OnMainAsync(() => TVHubProxy.Thumb(S("type"), S("id"), S("which")));
                if (t.data == null) Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("No picture"));
                else Send(s, 200, t.type, t.data);
                return;
            }
        }

        if (method != "POST") { Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
        string error;
        switch (path)
        {
            case "/api/add": error = OnMain(() => TVTiles.AddApp(I("page"), I("pos"), S("pkg"))); break;
            case "/api/move": error = OnMain(() => TVTiles.Move(I("page"), I("pos"), I("toPage"), I("toPos"))); break;
            case "/api/remove": error = OnMain(() => TVTiles.Remove(I("page"), I("pos"))); break;
            case "/api/rename": error = OnMain(() => TVTiles.Rename(I("page"), I("pos"), Encoding.UTF8.GetString(body ?? new byte[0]))); break;
            case "/api/picture":
            {
                bool gif = S("gif") == "1";
                error = gif ? CheckGif(body) : null;
                if (error == null)
                {
                    var type = S("type") == "banner" ? CHANNELTYPE.BANNER : CHANNELTYPE.ICON;
                    error = OnMain(() => TVTiles.SetPicture(I("page"), I("pos"), type, body, gif, S("fill") != "0"));
                }
                break;
            }
            case "/api/sound":
                error = OnMain(() => TVTiles.SetSound(I("page"), I("pos"), S("clear") == "1" ? null : body, S("file")));
                break;
            case "/api/homemusic":
                error = OnMain(() =>
                {
                    if (S("clear") == "1") { TVHomeMusic.Clear(); return (string) null; }
                    return TVHomeMusic.Save(body, S("file"));
                });
                break;
            case "/api/hub/install":
                Json(s, OnMainAsync(() => TVHubProxy.Install(S("type"), S("id"))));
                return;
            case "/api/hub/apply":
                error = OnMain(() => TVTiles.ApplyHub(I("page"), I("pos"), S("kind") == "banner" ? CHANNELTYPE.BANNER : CHANNELTYPE.ICON, S("id")));
                break;
            case "/api/hub/theme":
                error = OnMain(() => TVHubProxy.ApplyTheme(S("id"), true));
                break;
            case "/api/maker/part":
                error = OnMain(() => TVThemeMaker.SetPart(S("slot"), body, S("file"), S("clear") == "1"));
                break;
            case "/api/look/empty":
                error = OnMain(() => TVThemeExtras.SetEmptyPercent(I("percent")));
                break;
            case "/api/look/glow":
                error = OnMain(() => { var v = S("color"); if (v != "auto" && !ColorUtility.TryParseHtmlString(v, out _)) return "Pick a colour"; TVGlowColor.Set(v); return null; });
                break;
            case "/api/games/upload":
            {
                string dir = OnMain(() => TVGames.Folder);
                long free = OnMain(() => TVGames.FreeBytes(dir));
                long.TryParse(S("__len"), out long len);
                error = TVGames.Receive(s, len, dir, free, S("name"), () => Interlocked.Exchange(ref lastRequest, clock.ElapsedTicks));
                if (error == null) OnMain(() => { TVGameScreen.Refresh(); return 0; });
                break;
            }
            case "/api/games/delete":
                error = OnMain(() => { var e = TVGames.Delete(S("file")); if (e == null) TVGameScreen.Refresh(); return e; });
                break;
            case "/api/hub/postpreview":
                Json(s, OnMainAsync(() => TVHubPost.PostPreview(I("page"), I("pos"), S("name"), Encoding.UTF8.GetString(body ?? new byte[0]))));
                return;
            case "/api/sound/search":
                Json(s, OnMainAsync(() => TVSoundSearch.Search(S("q"))));
                return;
            case "/api/sound/use":
                error = OnMainAsync(() => TVSoundSearch.Use(S("url"), S("name"), S("to"), I("page"), I("pos")));
                break;
            case "/api/hub/post":
                Json(s, OnMainAsync(() => TVHubPost.Post(S("file"), S("name"), Encoding.UTF8.GetString(body ?? new byte[0]))));
                return;
            case "/api/maker/save":
                error = OnMain(() => TVThemeMaker.Save(S("name"), S("color"), S("from") == "default", string.IsNullOrEmpty(S("empty")) ? 100 : I("empty"), S("bar"), S("line"), S("text"), S("btn")));
                break;
            default: Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return;
        }
        Json(s, TVTiles.D("ok", error == null, "error", error));
    }

    // ---------------------------------------------------------------- data for the page

    private static object State()
    {
        var gc = GridController._instance;
        var tiles = new List<object>();
        foreach (var c in ChannelController._instance.loadedChannels)
        {
            if (c.position >= TVTiles.Slots) continue;
            tiles.Add(TVTiles.D(
                "page", c.gridNumber, "pos", c.position, "name", SafeTag(c),
                "pkg", c.info != null ? c.info.packageName : null,
                "custom", (c.iconName ?? "").Contains("_" + TVTiles.CustomTag + "_"),
                "customPreview", (c.bannerName ?? "").Contains("_" + TVTiles.CustomTag + "_")));
        }
        return TVTiles.D("pages", TVTiles.Pages, "slots", TVTiles.Slots, "cols", Columns(gc), "busy", TVTiles.Busy(), "tiles", tiles);
    }

    private static string SafeTag(Channel c)
    {
        try { return c.GetTag(); } catch (Exception) { return c.info != null ? c.info.name : "Channel"; }
    }

    // how many tiles side by side, as the home page lays them out (one row shares a height)
    private static int Columns(GridController gc)
    {
        var grid = gc.currentGrid != null ? gc.currentGrid.innerGrid : null;
        if (grid == null || grid.childCount == 0) return 4;
        float y = grid.GetChild(0).localPosition.y;
        int n = 0;
        for (int i = 0; i < grid.childCount; i++) if (Mathf.Abs(grid.GetChild(i).localPosition.y - y) < 1f) n++;
        return Mathf.Clamp(n, 1, 12);
    }

    private static object Apps()
    {
        var linker = AndroidLinker._instance;
        if (linker.apps.Count == 0 || !linker.IsAppCountAccurate()) linker.FindAllApps();
        var list = new List<object>();
        foreach (var a in linker.apps)
            if (a.packageName != Application.identifier) list.Add(TVTiles.D("pkg", a.packageName, "name", a.name));
        return list;
    }

    private static byte[] AppIcon(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return null;
        if (iconCache.TryGetValue(pkg, out var b)) return b;
        var app = TVTiles.FindApp(pkg);
        try { b = app != null && app.icon != null ? app.icon.DuplicateTexture().EncodeToPNG() : null; }
        catch (Exception) { b = null; }
        iconCache[pkg] = b;
        return b;
    }

    // every GIF frame is kept in memory as a full picture, so very long or very large GIFs are turned away
    private static string CheckGif(byte[] data)
    {
        if (data == null || data.Length < 16) return "That file is empty";
        string tmp = Path.Combine(Application.temporaryCachePath, "check_" + Thread.CurrentThread.ManagedThreadId + ".gif");
        try
        {
            File.WriteAllBytes(tmp, data);
            using var gif = new ThreeDISevenZeroR.UnityGifDecoder.GifStream(tmp);
            long frames = 0;
            while (gif.HasMoreData)
            {
                if (gif.CurrentToken == ThreeDISevenZeroR.UnityGifDecoder.GifStream.Token.Image) { gif.SkipToken(); frames++; }
                else gif.SkipToken();
            }
            long pixels = frames * gif.Header.width * gif.Header.height;
            if (frames == 0) return "That GIF has no frames";
            if (pixels > MaxGifPixels)
                return "That GIF is too big for the TV's memory (" + frames + " frames at " + gif.Header.width + "x" + gif.Header.height + "). Try a shorter or smaller one.";
            return null;
        }
        catch (Exception) { return "That GIF couldn't be read"; }
        finally { try { File.Delete(tmp); } catch (Exception) { } }
    }

    // ---------------------------------------------------------------- plumbing

    private static T OnMain<T>(Func<T> f)
    {
        T result = default;
        Exception error = null;
        var done = new ManualResetEventSlim(false); // not disposed: a late answer after a timeout still sets it
        SyncContext.RunOnUnityThread(() =>
        {
            try { result = f(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        if (!done.Wait(20000)) throw new TimeoutException("The TV didn't answer in time");
        if (error != null)
        {
            Debug.LogError("[Server] " + error);
            if (typeof(T) == typeof(string)) return (T) (object) ("Something went wrong on the TV: " + error.Message);
            throw error;
        }
        return result;
    }

    // an async job (a hub request) run on Unity's thread; waits for it to finish
    private static T OnMainAsync<T>(Func<Task<T>> f)
    {
        T result = default;
        Exception error = null;
        var done = new ManualResetEventSlim(false);
        SyncContext.RunOnUnityThread(async () =>
        {
            try { result = await f(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        if (!done.Wait(90000)) throw new TimeoutException("The Content Hub took too long");
        if (error != null) { Debug.LogError("[Server] " + error); throw error; }
        return result;
    }

    private static bool ReadRequest(NetworkStream s, out string method, out string path, out Dictionary<string, string> query, out byte[] body)
    {
        method = path = null; query = new Dictionary<string, string>(); body = null;
        // headers: read up to the blank line (8 KB at most)
        var head = new List<byte>(1024);
        int b, matched = 0;
        while (head.Count < 8192 && (b = s.ReadByte()) >= 0)
        {
            head.Add((byte) b);
            matched = (b == '\r' || b == '\n') ? matched + 1 : 0;
            if (matched == 4) break;
        }
        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length < 2) return false;
        method = first[0];
        string target = first[1];
        int qi = target.IndexOf('?');
        path = qi >= 0 ? target.Substring(0, qi) : target;
        if (qi >= 0)
            foreach (var pair in target.Substring(qi + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                query[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
        long length = 0;
        foreach (var line in lines)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) long.TryParse(line.Substring(15).Trim(), out length);
        // a game upload is copied from the network straight into its file by the route (TVGames.Receive), never held in memory
        if (path == "/api/games/upload") { query["__len"] = length.ToString(); return length > 0; }
        if (length < 0 || length > MaxUpload) return false;
        if (length > 0)
        {
            body = new byte[length];
            int read = 0, total = (int) length;
            while (read < total)
            {
                int n = s.Read(body, read, total - read);
                if (n <= 0) return false;
                read += n;
            }
        }
        return true;
    }

    private static void Json(NetworkStream s, object o) => Send(s, 200, "application/json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None })));

    private static void Png(NetworkStream s, byte[] png)
    {
        if (png == null) Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("No picture"));
        else Send(s, 200, "image/png", png);
    }

    private static void Send(NetworkStream s, int status, string type, byte[] body)
    {
        string reason = status == 200 ? "OK" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : "Bad Request";
        var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                                             "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        s.Write(header, 0, header.Length);
        s.Write(body, 0, body.Length);
        s.Flush();
    }
}
