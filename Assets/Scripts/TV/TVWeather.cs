using System;
using System.Collections;
using System.Collections.Generic;
using Animations;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// The built-in Weather Channel (package revo.weather), a Wii-style forecast screen: the weather now and the next
/// days for the viewer's own city, from Open-Meteo (free, no account). Icons and the little chime are drawn / made in
/// code, so nothing here is anyone else's artwork or sound. OK flips Fahrenheit / Celsius, Back closes.
/// </summary>
public static class TVWeather
{
    public const string Package = "revo.weather";
    private const double Lat = 35.3733, Lon = -119.0187;
    private const string CacheKey = "tv.weather.c2_";
    private const string AddedKey = "tv.weather.added";
    private const string BannerKey = "tv.weather.banner";
    private const string UnitKey = "tv.weather.celsius";

    private static GameObject root;
    private static Runner runner;
    public static bool Open => root != null;
    public static float MovedAt = -10f; // the globe was last moving (a glide, a held turn or zoom, the keyboard open)

    // ---------------------------------------------------------------- the channel on the home menu

    /// <summary>Puts the channel in the first free slot once (never again if the person removes it).</summary>
    public static void EnsureChannel(MonoBehaviour host)
    {
        if (!TVMode.IsTV || (PlayerPrefs.GetInt(AddedKey, 0) == 1 && PlayerPrefs.GetInt(BannerKey, 0) == 1)) return;
        host.StartCoroutine(AddRoutine());
    }

    private static IEnumerator AddRoutine()
    {
        for (int tries = 0; tries < 60; tries++)
        {
            yield return new WaitForSecondsRealtime(2f);
            var cc = ChannelController._instance;
            var gc = GridController._instance;
            if (cc == null || gc == null || cc.loadedChannels == null || cc.loadedChannels.Count == 0 || TVTiles.Busy()) continue;
            foreach (var c in cc.loadedChannels)
            {
                if (c.info == null || c.info.packageName != Package) continue;
                PlayerPrefs.SetInt(AddedKey, 1);
                if (PlayerPrefs.GetInt(BannerKey, 0) != 1)
                {
                    // the first version of the channel had the default preview picture: give it the weather one
                    string e = TVTiles.SetBuiltInPicture(c.gridNumber, c.position, CHANNELTYPE.BANNER, BannerPng(), false, true);
                    if (e == null) PlayerPrefs.SetInt(BannerKey, 1);
                }
                PlayerPrefs.Save();
                yield break;
            }
            if (PlayerPrefs.GetInt(AddedKey, 0) == 1) yield break; // it was added and the person removed it
            for (int g = 0; g < TVTiles.Pages; g++)
            for (int p = 0; p < TVTiles.Slots; p++)
            {
                if (TVTiles.At(g, p) != null) continue;
                var art = IconPng();
                string err = TVTiles.AddBuiltin(g, p, Package, "Weather Channel", art);
                if (err != null) { Debug.Log("[Weather] " + err); goto next; }
                yield return new WaitForSecondsRealtime(1.5f);
                err = TVTiles.SetBuiltInPicture(g, p, CHANNELTYPE.ICON, art, false, true);
                if (err != null) Debug.Log("[Weather] picture: " + err);
                yield return new WaitForSecondsRealtime(1f);
                err = TVTiles.SetBuiltInPicture(g, p, CHANNELTYPE.BANNER, BannerPng(), false, true);
                if (err != null) Debug.Log("[Weather] banner: " + err);
                PlayerPrefs.SetInt(AddedKey, 1);
                PlayerPrefs.SetInt(BannerKey, 1);
                PlayerPrefs.Save();
                yield break;
            }
            next:;
        }
    }

    // ---------------------------------------------------------------- opening

    private const string CityKey = "tv.weather.city";
    // the forecast's place is a ZIP code typed in once (asked the first time Weather opens): the globe's "My place" pin is
    // separate and never changes it
    private const string ZipKey = "tv.weather.zip", ZipAskedKey = "tv.weather.zipasked";
    private static readonly bool HomeForWeather = false;
    private const string WindKey = "tv.weather.kmh";
    private static readonly string[] CityNames = { "My location", "Denver", "Los Angeles", "San Francisco", "Las Vegas", "Phoenix", "New York", "Chicago", "Houston", "Miami", "Seattle", "London", "Tokyo" };
    private static readonly double[] CityLat = { 0, 39.7392, 34.0522, 37.7749, 36.1699, 33.4484, 40.7128, 41.8781, 29.7604, 25.7617, 47.6062, 51.5074, 35.6762 };
    private static readonly double[] CityLon = { 0, -104.9903, -118.2437, -122.4194, -115.1398, -112.0740, -74.0060, -87.6298, -95.3698, -80.1918, -122.3321, -0.1278, 139.6503 };
    // "My location" is found from the TV's internet address (nothing is shipped in the app): name|lat|lon, kept for next time
    private const string AutoKey = "tv.weather.auto";
    private const string HomeKey = "tv.weather.home"; // "name|lat|lon": the exact spot saved with "Make this my place" (your house), over the internet's guess
    private static bool Home(out string name, out double lat, out double lon)
    {
        var p = PlayerPrefs.GetString(HomeKey, "").Split('|'); var inv = System.Globalization.CultureInfo.InvariantCulture;
        name = p.Length == 3 ? p[0] : null; lat = lon = 0;
        return p.Length == 3 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out lat) && double.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out lon);
    }
    internal static string TownForPreview => PlayerPrefs.GetString(ZipKey, "").Length > 0 || PlayerPrefs.GetString(AutoKey, "").Length > 0 ? CityName : null;
    private static string CityName
    {
        get
        {
            if (CityIndex != 0) return CityNames[CityIndex];
            var p = PlayerPrefs.GetString(AutoKey, "").Split('|');
            return p.Length == 3 && p[0].Length > 0 ? p[0] : "Finding you...";
        }
    }
    private static bool Place(out double lat, out double lon)
    {
        lat = CityLat[CityIndex]; lon = CityLon[CityIndex];
        if (CityIndex != 0) return true;
        var p = PlayerPrefs.GetString(AutoKey, "").Split('|');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return p.Length == 3 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out lat) && double.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out lon);
    }
    private static int CityIndex => 0; // always the town you're in (found from the internet); the old city list is no longer used

    public static void Show()
    {
        if (Open) return;
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        Build(app.overlayCanvas);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    public static void Close()
    {
        if (!Open) return;
        if (runner != null && runner.CancelChoices()) { AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON); return; }
        if (runner != null && runner.InSettings) { runner.ExitSettings(); return; }
        UnityEngine.Object.Destroy(root);
        root = null;
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
        AudioController.FadeInHome();
    }

    // ---------------------------------------------------------------- data

    private class Day { public string name; public int code; public float hi, lo, wind, uv; public int rain; }

    private class Data
    {
        public float temp, feels, wind; public int humidity, code, windDir; public bool day = true; public string updated;
        public List<Day> days = new List<Day>();
    }

    private static Data Parse(string json)
    {
        var o = JObject.Parse(json);
        var cur = o["current"];
        var d = new Data
        {
            temp = (float) cur["temperature_2m"], feels = (float) cur["apparent_temperature"], wind = (float) cur["wind_speed_10m"],
            humidity = (int) cur["relative_humidity_2m"], code = (int) cur["weather_code"], day = (int) cur["is_day"] == 1,
            windDir = cur["wind_direction_10m"] != null ? (int) cur["wind_direction_10m"] : 0,
            updated = DateTime.Now.ToString("h:mm tt")
        };
        var dd = o["daily"];
        var times = (JArray) dd["time"];
        for (int i = 0; i < times.Count && i < 6; i++)
        {
            var date = DateTime.Parse((string) times[i]);
            Func<string, float> f = key => dd[key] != null && dd[key][i].Type != JTokenType.Null ? (float) dd[key][i] : 0f;
            d.days.Add(new Day
            {
                name = i == 0 ? "Today" : i == 1 ? "Tomorrow" : date.ToString("dddd"),
                code = (int) dd["weather_code"][i], hi = f("temperature_2m_max"), lo = f("temperature_2m_min"),
                rain = Mathf.RoundToInt(f("precipitation_probability_max")), wind = f("wind_speed_10m_max"), uv = f("uv_index_max")
            });
        }
        return d;
    }

    private static string Describe(int c)
    {
        if (c == 0) return "Clear";
        if (c == 1) return "Mostly clear";
        if (c == 2) return "Partly cloudy";
        if (c == 3) return "Cloudy";
        if (c == 45 || c == 48) return "Foggy";
        if (c >= 51 && c <= 57) return "Drizzle";
        if (c >= 61 && c <= 67) return "Rain";
        if (c >= 71 && c <= 77) return "Snow";
        if (c >= 80 && c <= 82) return "Showers";
        if (c == 85 || c == 86) return "Snow showers";
        if (c >= 95) return "Thunderstorms";
        return "Cloudy";
    }

    private static string Compass(int deg)
    {
        string[] p = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        return p[Mathf.RoundToInt(deg / 22.5f) % 16];
    }

    // ---------------------------------------------------------------- screen
    // Laid out like the Wii's Forecast Channel: a bar of buttons across the top and the bottom, a banner with the page's name
    // and the city, a blue sky with the big temperature, and a red "as of" strip. One page per day, a UV page, a Settings page.

    private static void Build(RectTransform parent)
    {
        root = new GameObject("TV Weather Screen", typeof(RectTransform));
        var rt = (RectTransform) root.transform;
        rt.SetParent(parent, false);
        rt.SetAsFirstSibling();
        TVServerScreen.Stretch(rt);
        float h = parent.rect.height > 1f ? parent.rect.height : 1080f;
        var r = root.AddComponent<Runner>();
        r.h = h; r.font = TVServerScreen.SettingsFont();
        runner = r;
        r.Setup();
        if (!PlayerPrefs.HasKey(ZipKey) && !PlayerPrefs.HasKey(ZipAskedKey)) { PlayerPrefs.SetInt(ZipAskedKey, 1); PlayerPrefs.Save(); r.AskZip(); }
        var sound = new GameObject("Chime").AddComponent<AudioSource>();
        sound.transform.SetParent(rt, false);
        sound.playOnAwake = false; sound.spatialBlend = 0f; sound.volume = 0.5f;
        sound.clip = Chime();
        sound.Play();
        sound.gameObject.AddComponent<WeatherMusic>();
    }

    private class Runner : MonoBehaviour
    {
        public float h;
        public TMP_FontAsset font;
        private RawImage bg;
        private RectTransform body, bars;
        private Data data;
        private string message = "Getting the forecast...";
        private int page;               // 0 Current, 1..5 the days, 6 UV
        private bool settings;
        private RectTransform iconRt;
        private readonly List<RectTransform> clouds = new List<RectTransform>();
        private readonly List<RectTransform> drops = new List<RectTransform>();
        private float t;
        private bool falling, snowing;

        public bool InSettings => settings || globe;
        private bool globe;
        private int globeZoom = 1, globeSet;
        private string globeJson;
        private List<int> globeShown = new List<int>();
        // pages: Current, Today, Tomorrow, 5-Day Forecast, the other days, UV Index
        private List<int> Order()
        {
            var o = new List<int> { -1 };
            if (data == null) return o;
            int n = Mathf.Min(data.days.Count, 6);
            for (int i = 0; i < n && i < 2; i++) o.Add(i);
            o.Add(100);
            for (int i = 2; i < n; i++) o.Add(i);
            o.Add(101);
            o.Add(102);
            return o;
        }
        private int PageCount => Order().Count;
        private int Kind(int p) { var o = Order(); return o[Mathf.Clamp(p, 0, o.Count - 1)]; }

        public void Setup()
        {
            bg = TVServerScreen.Child(transform, "Sky").gameObject.AddComponent<RawImage>();
            bg.raycastTarget = true;
            bg.texture = Sky(true);
            LoadCache();
            Render();
            Fetch();
        }

        private string CacheName => CacheKey + CityIndex;

        private void LoadCache()
        {
            string cached = PlayerPrefs.GetString(CacheName, "");
            data = null;
            if (cached.Length > 0) { try { data = Parse(cached); } catch (Exception) { } }
        }

        // Back while the list of matches is up: only the list closes (the globe stays)
        public bool CancelChoices()
        {
            if (zipPad != null) { CloseZipPad(); return true; }
            if (!globe || choices == null) return false;
            choices = null; message = null; pendingCell = "Cell Find a place"; Render();
            return true;
        }

        public void ExitSettings() { if (settings && !globe) pendingCell = "Cell Settings"; settings = false; globe = false; Render(); }

        public void Fetch()
        {
            if (data == null) message = "Getting the forecast...";
            StopCoroutine("FetchRoutine");
            StartCoroutine("FetchRoutine");
        }

        private IEnumerator FetchRoutine()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string zip = PlayerPrefs.GetString(ZipKey, "");
            bool zipFound = false;
            if (CityIndex == 0 && zip.Length >= 3)
            {
                // the ZIP code's town and middle (Zippopotam), or Open-Meteo's place list as a second try
                using (var zr = UnityWebRequest.Get("https://api.zippopotam.us/us/" + UnityWebRequest.EscapeURL(zip)))
                {
                    zr.timeout = 10;
                    yield return zr.SendWebRequest();
                    if (zr.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var pl = JObject.Parse(zr.downloadHandler.text)["places"]?[0];
                            if (pl != null)
                            {
                                PlayerPrefs.SetString(AutoKey, ((string) pl["place name"]).Replace("|", " ") + "|" + double.Parse((string) pl["latitude"], inv).ToString(inv) + "|" + double.Parse((string) pl["longitude"], inv).ToString(inv));
                                zipFound = true;
                            }
                        }
                        catch (Exception) { }
                    }
                }
                if (!zipFound)
                using (var zr = UnityWebRequest.Get("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + UnityWebRequest.EscapeURL(zip)))
                {
                    zr.timeout = 10;
                    yield return zr.SendWebRequest();
                    if (zr.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var g = JObject.Parse(zr.downloadHandler.text)["results"]?[0];
                            if (g != null) { PlayerPrefs.SetString(AutoKey, ((string) g["name"]).Replace("|", " ") + "|" + ((double) g["latitude"]).ToString(inv) + "|" + ((double) g["longitude"]).ToString(inv)); zipFound = true; }
                        }
                        catch (Exception) { }
                    }
                }
                Debug.Log("[Weather] ZIP " + zip + (zipFound ? " -> " + CityName : " not found"));
                if (!zipFound) { message = "Couldn't find ZIP code " + zip + ": press Settings to type it again"; Render(); yield break; }
            }
            else if (CityIndex == 0 && HomeForWeather && Home(out _, out double hLat, out double hLon))
            {
                // "My place" is set on the globe: the forecast is for that spot, named after the town it's in (the internet's guess
                // from the TV's address can be a town far away, wherever the provider's equipment is)
                string town = null;
                // the nearest city, town or village to My place (OpenStreetMap's places, via Photon, within 50 km); a house outside
                // any town's limits otherwise came back as just its county
                string q = "https://photon.komoot.io/reverse?lat=" + hLat.ToString(inv) + "&lon=" + hLon.ToString(inv)
                           + "&limit=10&radius=50&osm_tag=place:city&osm_tag=place:town&osm_tag=place:village";
                using (var op = UnityWebRequest.Get(q))
                {
                    op.timeout = 15; op.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
                    yield return op.SendWebRequest();
                    if (op.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            double best = double.MaxValue, cosl = Math.Cos(hLat * Math.PI / 180.0);
                            foreach (var el in (JArray) JObject.Parse(op.downloadHandler.text)["features"])
                            {
                                var co = (JArray) el["geometry"]?["coordinates"]; if (co == null || co.Count < 2) continue;
                                double lo = (double) co[0], la = (double) co[1], dx = (lo - hLon) * cosl, dy = la - hLat, d = dx * dx + dy * dy;
                                string nm = (string) el["properties"]?["name"];
                                if (!string.IsNullOrEmpty(nm) && d < best) { best = d; town = nm; }
                            }
                        }
                        catch (Exception) { }
                    }
                    else Debug.Log("[Weather] nearest town: " + op.error);
                }
                string url0 = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language=en&lat=" + hLat.ToString(inv) + "&lon=" + hLon.ToString(inv);
                if (string.IsNullOrEmpty(town))
                using (var rv = UnityWebRequest.Get(url0))
                {
                    rv.timeout = 10; rv.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
                    yield return rv.SendWebRequest();
                    if (rv.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var ad = JObject.Parse(rv.downloadHandler.text)["address"];
                            town = (string) (ad?["city"] ?? ad?["town"] ?? ad?["village"] ?? ad?["hamlet"] ?? ad?["municipality"] ?? ad?["county"]);
                        }
                        catch (Exception) { }
                    }
                    else Debug.Log("[Weather] town of My place: " + rv.error);
                }
                if (string.IsNullOrEmpty(town)) { var old = PlayerPrefs.GetString(AutoKey, "").Split('|'); town = old.Length == 3 && old[0].Length > 0 ? old[0] : "My place"; }
                PlayerPrefs.SetString(AutoKey, town.Replace("|", " ") + "|" + hLat.ToString(inv) + "|" + hLon.ToString(inv));
                Debug.Log("[Weather] forecast for My place: " + town);
            }
            else if (CityIndex == 0)
            {
                using (var geo = UnityWebRequest.Get("https://ipwho.is/?fields=success,city,latitude,longitude"))
                {
                    geo.timeout = 10;
                    yield return geo.SendWebRequest();
                    if (geo.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var g = JObject.Parse(geo.downloadHandler.text);
                            if ((bool) g["success"])
                                PlayerPrefs.SetString(AutoKey, (string) g["city"] + "|" + ((double) g["latitude"]).ToString(inv) + "|" + ((double) g["longitude"]).ToString(inv));
                        }
                        catch (Exception) { }
                    }
                }
            }
            if (!Place(out double lat, out double lon))
            {
                message = "Couldn't find your location: press Settings and pick a city";
                Render();
                yield break;
            }
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(inv)
                + "&longitude=" + lon.ToString(inv)
                + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m,is_day"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max,uv_index_max"
                + "&temperature_unit=fahrenheit&wind_speed_unit=mph&timezone=auto&forecast_days=6";
            // a slow or dropped answer is tried again (three goes) before the forecast is called offline
            for (int attempt = 0; attempt < 3; attempt++)
            using (var req = UnityWebRequest.Get(url))
            {
                if (attempt > 0) yield return new WaitForSecondsRealtime(attempt * 3f);
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success && attempt < 2) { Debug.Log("[Weather] try " + (attempt + 1) + ": " + req.error); continue; }
                attempt = 3;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        data = Parse(req.downloadHandler.text);
                        PlayerPrefs.SetString(CacheName, req.downloadHandler.text);
                        message = null;
                    }
                    catch (Exception e) { Debug.Log("[Weather] " + e.Message); message = data == null ? "The forecast couldn't be read" : null; }
                }
                else message = data == null ? "No connection: the forecast shows when the TV is online" : "Offline: showing the last forecast";
            }
            if (page >= PageCount) page = 0;
            Render();
        }

        private void Go(int to)
        {
            settings = false;
            string fn = TVNav.FocusName;
            bool fromTop = page > 0 && fn == "Cell " + PageName(page - 1), fromBottom = page + 1 < PageCount && fn == "Cell " + PageName(page + 1);
            page = ((to % PageCount) + PageCount) % PageCount;
            // the page buttons change their names: the light goes to the one in the same place (or its neighbour)
            if (fromTop) pendingCell = page > 0 ? "Cell " + PageName(page - 1) : "Cell Refresh";
            else if (fromBottom) pendingCell = page + 1 < PageCount ? "Cell " + PageName(page + 1) : "Cell Globe";
            AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
            Render();
        }

        private string PageName(int p)
        {
            if (data == null) return "Current";
            int k = Kind(p);
            if (k < 0) return "Current";
            if (k == 100) return "5-Day Forecast";
            if (k == 101) return "UV Index";
            if (k == 102) return "Laundry Index";
            return data.days[k].name;
        }

        // ------------------------------------------------------------ drawing helpers

        private RectTransform Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color color, Sprite sprite = null)
        {
            var rt = TVServerScreen.Child(parent, name);
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (color.a > 0f || sprite != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.color = color; img.raycastTarget = false;
                if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            }
            return rt;
        }

        private TextMeshProUGUI Label(Transform parent, string s, float size, Color c, TextAlignmentOptions al, float x0, float y0, float x1, float y1)
        {
            var t = TVServerScreen.Text(parent, s, font, h * size, c);
            t.alignment = al;
            t.enableWordWrapping = false;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        // a top-to-bottom shade (made once per pair of colours)
        private static readonly Dictionary<string, Sprite> grads = new Dictionary<string, Sprite>();
        private static Sprite Grad(Color top, Color bottom)
        {
            string key = top + "|" + bottom;
            if (grads.TryGetValue(key, out var sp) && sp != null) return sp;
            var t = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 64; y++) { var c = Color.Lerp(bottom, top, y / 63f); t.SetPixel(0, y, c); t.SetPixel(1, y, c); }
            t.Apply(false, true);
            return grads[key] = Sprite.Create(t, new Rect(0, 0, 2, 64), new Vector2(0.5f, 0.5f));
        }
        private RectTransform GradBox(Transform parent, string name, float x0, float y0, float x1, float y1, Color top, Color bottom, float inset = 0f)
        {
            var rt = Box(parent, name, x0, y0, x1, y1, Color.white);
            var img = rt.GetComponent<Image>(); img.sprite = Grad(top, bottom); img.type = Image.Type.Simple;
            if (inset > 0f) { rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset); }
            return rt;
        }
        private static void Outline(TextMeshProUGUI t, Color32 c, float w) { t.outlineWidth = w; t.outlineColor = c; }

        // a cell in the top or bottom bar: a Wii-grey panel that is also a button
        private void Cell(Transform parent, string label, float x0, float x1, float y0, float y1, UnityEngine.Events.UnityAction click, bool arrowUp, bool arrowDown)
        {
            var rt = globe ? Box(parent, "Cell " + label, x0, y0, x1, y1, new Color(0.93f, 0.94f, 0.96f))
                           : GradBox(parent, "Cell " + label, x0, y0, x1, y1, new Color(0.98f, 0.98f, 0.99f), new Color(0.8f, 0.82f, 0.85f));
            var img = rt.GetComponent<Image>();
            img.raycastTarget = true;
            var edge = Box(rt, "Edge", 0f, 0f, 0.004f, 1f, new Color(0.55f, 0.6f, 0.66f));
            edge.sizeDelta = new Vector2(Mathf.Max(2f, h * 0.003f), 0f);
            if (click != null)
            {
                var b = rt.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                string cellName = rt.name;
                b.onClick.AddListener(() => { lastCell = cellName; click(); });
            }
            string s = (arrowUp ? "▲  " : "") + (arrowDown ? "▼  " : "") + label;
            var t = Label(rt, s, 0.04f, globe ? new Color(0.2f, 0.45f, 0.62f) : new Color(0.12f, 0.13f, 0.15f), TextAlignmentOptions.Center, 0f, 0f, 1f, 1f);
            t.enableAutoSizing = true; t.fontSizeMin = h * 0.024f; t.fontSizeMax = h * 0.04f;
        }

        private static string Deg(float f, bool c) { return Mathf.RoundToInt(c ? (f - 32f) * 5f / 9f : f) + "°"; }
        private static string Spd(float mph, bool kmh) { return kmh ? Mathf.RoundToInt(mph * 1.609f) + " km/h" : Mathf.RoundToInt(mph) + " mph"; }

        public void Render()
        {
            if (globe)
            {
                string fn = TVNav.FocusName; // a rebuild after a download must keep the light where the person put it, not on the last button pressed
                if (pendingCell != null) { lastCell = pendingCell; pendingCell = null; } // the button that changes its own name
                else if (fn != null && fn.StartsWith("Cell ")) lastCell = fn;
                if (lastCell != null) TVNav.ReturnToNamed(lastCell);
            }
            else
            {
                // the forecast pages too: Refresh / the day buttons rebuild the page, the light stays up there (it used to drop to the bottom row)
                string fn = TVNav.FocusName;
                string want = pendingCell ?? (fn != null && fn.StartsWith("Cell ") ? fn : null);
                pendingCell = null;
                if (want != null) TVNav.ReturnToNamed(want);
            }
            if (body != null) Destroy(body.gameObject);
            clouds.Clear(); drops.Clear(); iconRt = null;
            body = TVServerScreen.Child(transform, "Body");
            body.SetSiblingIndex(1);
            bool c = PlayerPrefs.GetInt(UnitKey, 0) == 1, kmh = PlayerPrefs.GetInt(WindKey, 0) == 1;
            string city = CityName;
            bool day = data == null || data.day;
            bg.texture = Sky(day);
            bg.uvRect = new Rect(0f, 0f, 1f, 1f);
            if (zipPad != null) { DrawZipPad(); return; }
if (!settings && !globe && data != null) MapBackground(day ? new Color(0.02f, 0.14f, 0.62f, 0.68f) : new Color(0.02f, 0.04f, 0.2f, 0.8f));

            // sky effects behind the pages: drifting clouds, rain or snow from the forecast
            falling = snowing = false;
            if (data != null && !settings && !globe)
            {
                int code = Kind(page) < 0 || Kind(page) >= 100 ? data.code : data.days[Kind(page)].code;
                falling = (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || code >= 95;
                snowing = (code >= 71 && code <= 77) || code == 85 || code == 86;
                if (falling || snowing)
                {
                    var dropsRoot = TVServerScreen.Child(body, "Drops");
                    dropsRoot.gameObject.AddComponent<Canvas>(); // the falling rain / snow moves every frame: kept apart from the page's text
                    int n = snowing ? 36 : 46;
                    for (int i = 0; i < n; i++)
                    {
                        var d = new GameObject("Drop", typeof(RectTransform)).AddComponent<Image>();
                        d.transform.SetParent(dropsRoot, false);
                        d.raycastTarget = false;
                        d.sprite = TVServerScreen.DotSprite();
                        d.color = snowing ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.75f, 0.9f, 1f, 0.5f);
                        var rt = d.rectTransform;
                        rt.anchorMin = rt.anchorMax = Vector2.zero;
                        rt.sizeDelta = snowing ? new Vector2(h * 0.014f, h * 0.014f) : new Vector2(h * 0.005f, h * 0.03f);
                        rt.anchoredPosition = new Vector2(h * 1.78f * ((i * 53) % n) / n, h * 0.12f + h * 0.68f * ((i * 29) % n) / n);
                        drops.Add(rt);
                    }
                }
            }

            var yellow = new Color(1f, 0.86f, 0.2f);
            var white = new Color(0.97f, 0.98f, 1f);
            string title = settings ? "Settings" : globe ? "Current Weather" : PageName(page);
            string asOf = data != null ? "As of " + data.updated : "";

            // the banner: the page's name on the left, the city in a green pill on the right
            if (globe)
            {
                Box(body, "Banner", 0f, 0.8f, 1f, 0.9f, new Color(0.02f, 0.1f, 0.4f, 0.92f));
                Box(body, "BannerLine", 0f, 0.895f, 1f, 0.9f, new Color(0.3f, 0.85f, 0.4f));
                Label(body, title.ToUpperInvariant(), 0.058f, white, TextAlignmentOptions.MidlineLeft, 0.03f, 0.8f, 0.5f, 0.9f).fontStyle = FontStyles.Bold;
                var pill = Box(body, "City", 0.52f, 0.815f, 0.97f, 0.885f, new Color(0.22f, 0.72f, 0.3f), TVServerScreen.PillSprite());
                Label(pill, city, 0.045f, white, TextAlignmentOptions.MidlineRight, 0.04f, 0f, 0.96f, 1f);
            }
            else
            {
                // the forecast pages: a green banner, the city in a darker green pill with a light rim
                GradBox(body, "Banner", 0f, 0.8f, 1f, 0.9f, new Color(0.2f, 0.72f, 0.22f), new Color(0.04f, 0.42f, 0.1f));
                Box(body, "BannerLine", 0f, 0.8f, 1f, 0.804f, new Color(0.02f, 0.25f, 0.05f));
                var tl = Label(body, title.ToUpperInvariant(), 0.056f, white, TextAlignmentOptions.MidlineLeft, 0.025f, 0.8f, 0.5f, 0.9f);
                tl.fontStyle = FontStyles.Bold; Outline(tl, new Color32(0, 40, 10, 255), 0.22f);
                var rim = Box(body, "CityRim", 0.52f, 0.812f, 0.975f, 0.888f, new Color(0.55f, 0.95f, 0.55f), TVServerScreen.PillSprite());
                var pill = Box(rim, "City", 0f, 0f, 1f, 1f, new Color(0.08f, 0.5f, 0.14f), TVServerScreen.PillSprite());
                pill.offsetMin = new Vector2(3f, 3f); pill.offsetMax = new Vector2(-3f, -3f);
                var cl = Label(pill, city, 0.042f, white, TextAlignmentOptions.MidlineRight, 0.04f, 0f, 0.95f, 1f);
                cl.enableAutoSizing = true; cl.fontSizeMin = h * 0.026f; cl.fontSizeMax = h * 0.042f;
            }

            if (data == null)
            {
                Label(body, message ?? "", 0.045f, white, TextAlignmentOptions.Center, 0.05f, 0.3f, 0.95f, 0.6f);
            }
            else if (settings)
            {
                string[] heads = { "Your ZIP code", "Temperature display", "Wind display" };
                string zipNow = PlayerPrefs.GetString(ZipKey, "");
                string[] vals = { zipNow.Length > 0 ? city + "  (" + zipNow + ")" : "Not set", c ? "Celsius" : "Fahrenheit", kmh ? "km/h" : "mph" };
                for (int i = 0; i < 3; i++)
                {
                    float top = 0.77f - i * 0.2f;
                    Box(body, "Head", 0.12f, top - 0.04f, 0.88f, top, new Color(0.02f, 0.1f, 0.4f, 0.85f));
                    Label(body, heads[i], 0.032f, white, TextAlignmentOptions.Center, 0.12f, top - 0.04f, 0.88f, top);
                    var cellv = Box(body, "Value", 0.12f, top - 0.14f, 0.6f, top - 0.04f, new Color(1f, 1f, 1f, 0.16f));
                    Label(cellv, vals[i], 0.05f, white, TextAlignmentOptions.Center, 0f, 0f, 1f, 1f);
                    int which = i;
                    var chg = Box(body, "Change", 0.62f, top - 0.14f, 0.88f, top - 0.04f, new Color(0.96f, 0.97f, 0.98f), TVServerScreen.PillSprite());
                    chg.GetComponent<Image>().raycastTarget = true;
                    var b = chg.gameObject.AddComponent<Button>();
                    b.targetGraphic = chg.GetComponent<Image>();
                    b.onClick.AddListener(() => Change(which));
                    Label(chg, "Change", 0.04f, new Color(0.15f, 0.15f, 0.17f), TextAlignmentOptions.Center, 0f, 0f, 1f, 1f);
                }
            }
            else if (globe)
            {
                DrawGlobe(white);
            }
            else if (Kind(page) == 100)
            {
                // the 5-day overview: one card per day
                int nd = Mathf.Min(5, data.days.Count);
                for (int i = 0; i < nd; i++)
                {
                    var d = data.days[i];
                    float x0 = 0.03f + i * 0.1945f;
                    // like the old channel's cards: a blue panel with a light edge, the day on a darker strip, big bold numbers
                    var edge = Box(body, "Day " + d.name, x0, 0.2f, x0 + 0.18f, 0.76f, new Color(0.45f, 0.7f, 1f, 0.95f));
                    var card = GradBox(edge, "Fill", 0f, 0f, 1f, 1f, new Color(0.13f, 0.4f, 0.9f), new Color(0.02f, 0.12f, 0.5f), Mathf.Max(2f, h * 0.003f));
                    GradBox(card, "Head", 0f, 0.84f, 1f, 1f, new Color(0.05f, 0.22f, 0.66f), new Color(0.02f, 0.12f, 0.45f));
                    Box(card, "HeadLine", 0f, 0.835f, 1f, 0.842f, new Color(0.45f, 0.7f, 1f, 0.9f));
                    string dn = i == 0 ? "Today" : d.name == "Tomorrow" ? "Tmrw" : d.name.Length > 3 ? d.name.Substring(0, 3) : d.name;
                    bool gold = i == 0 || dn.StartsWith("Sat") || dn.StartsWith("Sun");
                    var nl = Label(card, dn.ToUpperInvariant(), 0.05f, gold ? yellow : white, TextAlignmentOptions.Center, 0f, 0.84f, 1f, 1f);
                    nl.fontStyle = FontStyles.Bold; Outline(nl, new Color32(0, 15, 60, 255), 0.2f);
                    var di = new GameObject("DayIcon", typeof(RectTransform)).AddComponent<RawImage>();
                    di.transform.SetParent(card, false);
                    di.texture = Icon(d.code, true); di.raycastTarget = false;
                    var drt = di.rectTransform;
                    drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.615f);
                    drt.sizeDelta = new Vector2(h * 0.26f, h * 0.26f);
                    var hl = Label(card, Deg(d.hi, c), 0.085f, yellow, TextAlignmentOptions.Center, 0f, 0.27f, 1f, 0.43f);
                    hl.fontStyle = FontStyles.Bold; Outline(hl, new Color32(40, 25, 0, 255), 0.18f);
                    var ll = Label(card, Deg(d.lo, c), 0.075f, new Color(0.62f, 0.85f, 1f), TextAlignmentOptions.Center, 0f, 0.12f, 1f, 0.27f);
                    ll.fontStyle = FontStyles.Bold; Outline(ll, new Color32(0, 15, 60, 255), 0.18f);
                    if (d.rain > 0) Label(card, d.rain + "% rain", 0.024f, new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center, 0f, 0.025f, 1f, 0.11f);
                }
            }
            else if (Kind(page) == 102)
            {
                // the laundry index: how good a day it is to dry washing outside (dry air, wind, no rain)
                float score = 10f - data.days[0].rain / 12f - Mathf.Max(0f, (data.humidity - 50f) / 10f) + Mathf.Min(2f, data.days[0].wind / 10f);
                int idx = Mathf.Clamp(Mathf.RoundToInt(score), 0, 10);
                string say = idx >= 8 ? "A great day to hang washing outside" : idx >= 5 ? "Good for drying outside" : idx >= 3 ? "Fair: it may dry slowly" : "Dry it indoors today";
                Label(body, "Laundry Index", 0.045f, white, TextAlignmentOptions.Center, 0.15f, 0.62f, 0.85f, 0.7f);
                Box(body, "LaundryLine", 0.15f, 0.615f, 0.85f, 0.62f, new Color(1f, 1f, 1f, 0.6f));
                var num = Box(body, "LaundryNum", 0.15f, 0.33f, 0.4f, 0.6f, new Color(1f, 1f, 1f, 0.2f));
                Label(num, idx.ToString(), 0.2f, white, TextAlignmentOptions.Center, 0f, 0f, 1f, 1f).fontStyle = FontStyles.Bold;
                var lv = Box(body, "LaundryLevel", 0.42f, 0.33f, 0.85f, 0.6f, new Color(1f, 1f, 1f, 0.2f));
                Label(lv, say, 0.045f, white, TextAlignmentOptions.Center, 0.02f, 0f, 0.98f, 1f);
            }
            else if (Kind(page) == 101)
            {
                // the UV index page
                float uv = data.days[0].uv;
                string level = uv < 3 ? "Low" : uv < 6 ? "Moderate" : uv < 8 ? "High" : uv < 11 ? "Very High" : "Extreme";
                Label(body, "UV Index", 0.045f, white, TextAlignmentOptions.Center, 0.15f, 0.62f, 0.85f, 0.7f);
                Box(body, "UVLine", 0.15f, 0.615f, 0.85f, 0.62f, new Color(1f, 1f, 1f, 0.6f));
                var num = Box(body, "UVNum", 0.15f, 0.33f, 0.4f, 0.6f, new Color(1f, 1f, 1f, 0.2f));
                Label(num, Mathf.RoundToInt(uv).ToString(), 0.2f, white, TextAlignmentOptions.Center, 0f, 0f, 1f, 1f).fontStyle = FontStyles.Bold;
                var lv = Box(body, "UVLevel", 0.42f, 0.33f, 0.85f, 0.6f, new Color(1f, 1f, 1f, 0.2f));
                Label(lv, level, 0.07f, white, TextAlignmentOptions.Center, 0f, 0f, 1f, 1f);
                var sun = new GameObject("Sun", typeof(RectTransform)).AddComponent<RawImage>();
                sun.transform.SetParent(body, false);
                sun.texture = Icon(0, true); sun.raycastTarget = false;
                iconRt = sun.rectTransform;
                sun.gameObject.AddComponent<Canvas>(); // it bobs every frame
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.1f, 0.68f);
                iconRt.sizeDelta = new Vector2(h * 0.2f, h * 0.2f);
            }
            else
            {
                float temp, lo = 0f, windMph;
                int code, rain;
                string note;
                if (Kind(page) < 0)
                {
                    temp = data.temp; code = data.code; windMph = data.wind; rain = data.days[0].rain;
                    note = "Feels like " + Deg(data.feels, c) + "   ·   Humidity " + data.humidity + "%";
                }
                else
                {
                    var d = data.days[Kind(page)];
                    temp = d.hi; lo = d.lo; code = d.code; windMph = d.wind; rain = d.rain;
                    note = "Low " + Deg(d.lo, c);
                }
                var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<RawImage>();
                icon.transform.SetParent(body, false);
                icon.texture = Icon(code, Kind(page) < 0 ? data.day : true);
                icon.raycastTarget = false;
                iconRt = icon.rectTransform;
                icon.gameObject.AddComponent<Canvas>(); // it bobs every frame
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.77f, 0.5f);
                iconRt.sizeDelta = new Vector2(h * 0.52f, h * 0.52f);
                Label(body, Deg(temp, c), 0.3f, yellow, TextAlignmentOptions.Center, 0.03f, 0.43f, 0.55f, 0.78f).fontStyle = FontStyles.Bold;
                Label(body, Describe(code), 0.06f, white, TextAlignmentOptions.Center, 0.03f, 0.33f, 0.55f, 0.43f);
                Label(body, note, 0.03f, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Center, 0.03f, 0.25f, 0.55f, 0.33f);
                bool wet = rain >= 40;
                if (rain > 0) // no chance of rain: nothing said about it
                Label(body, (Kind(page) < 0 ? "Chance of rain today:  " : "Chance of rain:  ") + rain + "%" + (wet ? "   Rain likely" : ""), 0.04f, wet ? new Color(0.7f, 0.9f, 1f) : new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.MidlineLeft, 0.03f, 0.15f, 0.52f, 0.24f).fontStyle = wet ? FontStyles.Bold : FontStyles.Normal;
                string wind = Kind(page) < 0 ? Spd(windMph, kmh) : "Wind up to " + Spd(windMph, kmh);
                Label(body, wind, 0.045f, white, TextAlignmentOptions.MidlineRight, 0.5f, 0.15f, 0.97f, 0.24f);
            }

            // the strips: "supported by" in blue, "as of" in red
            Box(body, "Credit", 0f, 0.1f, 0.6f, 0.14f, new Color(0.02f, 0.1f, 0.4f, 0.9f));
            Label(body, "supported by Open-Meteo.com", 0.022f, white, TextAlignmentOptions.MidlineLeft, 0.015f, 0.1f, 0.6f, 0.14f);
            Box(body, "AsOf", 0.6f, 0.1f, 1f, 0.14f, new Color(0.72f, 0.08f, 0.14f, 0.95f));
            Label(body, message != null && data != null ? message : asOf, 0.024f, white, TextAlignmentOptions.MidlineRight, 0.6f, 0.1f, 0.985f, 0.14f);

            // the button bars
            bars = body;
            Box(body, "TopBar", 0f, 0.9f, 1f, 1f, new Color(0.9f, 0.91f, 0.93f));
            Box(body, "BottomBar", 0f, 0f, 1f, 0.1f, new Color(0.9f, 0.91f, 0.93f));
            if (globe)
            {
                Cell(body, "◄ Back to weather", 0f, 0.3f, 0f, 0.1f, ExitSettings, false, false);
                Cell(body, "My place", 0f, 0.15f, 0.9f, 1f, GoHome, false, false);
                bool searched = pinName != null && pinName != "My place" && choices == null;
                Cell(body, searched ? "Cancel search" : "Find a place", 0.15f, 0.33f, 0.9f, 1f, searched ? (UnityEngine.Events.UnityAction) CancelSearch : OpenFind, false, false);
                var cs = body.Find("Cell Cancel search"); if (cs != null) cs.name = "Cell Find a place"; // the light stays on it when it changes back
                if (pinName != null && pinName != "My place" && choices == null) Cell(body, "Set as my place", 0.33f, 0.5f, 0.9f, 1f, SetHome, false, false);
                else if (pinName == "My place" && choices == null && Home(out _, out _, out _)) Cell(body, "Remove my place", 0.33f, 0.5f, 0.9f, 1f, ForgetHome, false, false);
                else Cell(body, globeZoom >= TownZoom ? "Next town ►" : "Next city ►", 0.33f, 0.5f, 0.9f, 1f, NextCity, false, false);
                // a press zooms one step; holding OK keeps zooming (GlobeHold)
                Cell(body, "Zoom out  –", 0.5f, 0.66f, 0.9f, 1f, () => { if (!HeldJustNow()) ZoomBy(-1); }, false, false);
                Cell(body, "Zoom in  +", 0.66f, 0.82f, 0.9f, 1f, () => { if (!HeldJustNow()) ZoomBy(1); }, false, false);
                Cell(body, PlayerPrefs.GetInt(UnitKey, 0) == 1 ? "Show °F" : "Show °C", 0.82f, 1f, 0.9f, 1f, () => { PlayerPrefs.SetInt(UnitKey, PlayerPrefs.GetInt(UnitKey, 0) == 1 ? 0 : 1); pendingCell = "Cell " + (PlayerPrefs.GetInt(UnitKey, 0) == 1 ? "Show °F" : "Show °C"); globeDirty = true; Render(); }, false, false);
                // a press turns one step; holding OK keeps turning smoothly until it's let go (GlobeHold)
                Cell(body, "◄ Left", 0.3f, 0.5f, 0f, 0.1f, () => { if (!HeldJustNow()) Turn(-40f, 0f); }, false, false);
                Cell(body, "Right ►", 0.5f, 0.7f, 0f, 0.1f, () => { if (!HeldJustNow()) Turn(40f, 0f); }, false, false);
                Cell(body, "▲ Up", 0.7f, 0.85f, 0f, 0.1f, () => { if (!HeldJustNow()) Turn(0f, 25f); }, false, false);
                Cell(body, "▼ Down", 0.85f, 1f, 0f, 0.1f, () => { if (!HeldJustNow()) Turn(0f, -25f); }, false, false);
                if (choices != null)
                {
                    // the matches, nearest first, in a panel over the globe
                    int n = choices.Count; float rowH = 0.085f, top = 0.5f + (n + 1) * rowH * 0.5f;
                    Box(body, "PickShade", 0f, 0f, 1f, 1f, new Color(0f, 0.02f, 0.08f, 0.55f)).GetComponent<Image>().raycastTarget = true; // only the list (and Back) from here
                    Box(body, "PickPanel", 0.12f, top - (n + 1) * rowH - 0.02f, 0.88f, top + 0.07f, new Color(0.02f, 0.1f, 0.4f, 0.92f));
                    Label(body, "Which one did you mean?", 0.04f, white, TextAlignmentOptions.Center, 0.12f, top, 0.88f, top + 0.065f).fontStyle = FontStyles.Bold;
                    for (int i = 0; i < n; i++)
                    {
                        int k = i;
                        Cell(body, ChoiceLabel(i), 0.14f, 0.86f, top - (i + 1) * rowH, top - i * rowH - 0.008f, () => PickChoice(k), false, false);
                    }
                    Cell(body, "Cancel", 0.4f, 0.6f, top - (n + 1) * rowH, top - n * rowH - 0.008f, () => { choices = null; message = null; pendingCell = "Cell Find a place"; Render(); }, false, false);
                }
            }
            else if (!settings)
            {
                if (page > 0) Cell(body, PageName(page - 1), 0.3f, 0.7f, 0.9f, 1f, () => Go(page - 1), true, false);
                Cell(body, "Refresh", 0f, 0.3f, 0.9f, 1f, Fetch, false, false);
                Cell(body, "Settings", 0.7f, 1f, 0.9f, 1f, () => { settings = true; Render(); }, false, false);
                if (page + 1 < PageCount) Cell(body, PageName(page + 1), 0.3f, 0.7f, 0f, 0.1f, () => Go(page + 1), false, true);
                Cell(body, "Globe", 0.7f, 1f, 0f, 0.1f, EnterGlobe, false, false);
            }
            if (!globe) Cell(body, settings ? "Back" : "Revo Menu", 0f, 0.3f, 0f, 0.1f, settings ? (UnityEngine.Events.UnityAction) ExitSettings : TVWeather.Close, false, false);
        }

        // ------------------------------------------------------------ the globe: today's weather in cities around you

        private static readonly string[] MapNames = { "Los Angeles", "San Francisco", "Seattle", "Denver", "Phoenix", "Dallas", "Houston", "Chicago", "Atlanta", "Miami", "New York", "Boston", "Toronto", "Mexico City", "Vancouver", "Las Vegas", "Washington", "Minneapolis", "London", "Paris", "Madrid", "Berlin", "Rome", "Cairo", "Moscow", "Dubai", "Delhi", "Bangkok", "Beijing", "Tokyo", "Sydney", "Auckland", "Cape Town", "Lagos", "Sao Paulo", "Buenos Aires", "Lima", "Honolulu", "Anchorage", "Seoul", "Detroit", "Philadelphia", "Nashville", "New Orleans", "Salt Lake City", "Portland", "San Diego", "Kansas City", "St. Louis", "Orlando", "Charlotte", "Pittsburgh", "Austin", "Albuquerque", "Boise", "Calgary", "Montreal", "Havana", "Bogota", "Santiago", "Rio de Janeiro", "Reykjavik", "Dublin", "Amsterdam", "Vienna", "Stockholm", "Athens", "Istanbul", "Nairobi", "Johannesburg", "Casablanca", "Tehran", "Karachi", "Mumbai", "Singapore", "Jakarta", "Manila", "Hong Kong", "Shanghai", "Osaka", "Perth", "Melbourne", "Brisbane", "Lisbon", "Oslo", "Helsinki", "Warsaw", "Kyiv", "Riyadh", "Addis Ababa", "Accra", "Fairbanks", "Quito", "Caracas", "Denpasar", "Taipei", "Hanoi", "Dhaka", "Kathmandu", "Tashkent", "Kenai", "Winnipeg", "Halifax", "Panama City", "San Jose", "Trujillo", "La Paz", "Montevideo", "Brasilia", "Manaus" };
        private static readonly double[] MapLat = { 34.05, 37.77, 47.61, 39.74, 33.45, 32.78, 29.76, 41.88, 33.75, 25.76, 40.71, 42.36, 43.65, 19.43, 49.28, 36.17, 38.91, 44.98, 51.51, 48.86, 40.42, 52.52, 41.90, 30.04, 55.76, 25.20, 28.61, 13.76, 39.90, 35.68, -33.87, -36.85, -33.92, 6.52, -23.55, -34.60, -12.05, 21.31, 61.22, 37.57, 42.33, 39.95, 36.16, 29.95, 40.76, 45.52, 32.72, 39.1, 38.63, 28.54, 35.23, 40.44, 30.27, 35.08, 43.62, 51.05, 45.5, 23.11, 4.71, -33.45, -22.91, 64.15, 53.35, 52.37, 48.21, 59.33, 37.98, 41.01, -1.29, -26.2, 33.57, 35.69, 24.86, 19.08, 1.35, -6.21, 14.6, 22.32, 31.23, 34.69, -31.95, -37.81, -27.47, 38.72, 59.91, 60.17, 52.23, 50.45, 24.71, 9.03, 5.6, 64.84, -0.18, 10.48, -8.65, 25.03, 21.03, 23.81, 27.72, 41.3, 60.55, 49.9, 44.65, 8.98, 9.93, -8.11, -16.5, -34.9, -15.79, -3.12 };
        private static readonly double[] MapLon = { -118.24, -122.42, -122.33, -104.99, -112.07, -96.80, -95.37, -87.63, -84.39, -80.19, -74.01, -71.06, -79.38, -99.13, -123.12, -115.14, -77.04, -93.27, -0.13, 2.35, -3.70, 13.40, 12.50, 31.24, 37.62, 55.27, 77.21, 100.50, 116.41, 139.65, 151.21, 174.76, 18.42, 3.38, -46.63, -58.38, -77.04, -157.86, -149.90, 126.98, -83.05, -75.17, -86.78, -90.07, -111.89, -122.68, -117.16, -94.58, -90.2, -81.38, -80.84, -80.0, -97.74, -106.65, -116.2, -114.07, -73.57, -82.37, -74.07, -70.67, -43.17, -21.94, -6.26, 4.9, 16.37, 18.07, 23.73, 28.98, 36.82, 28.05, -7.59, 51.39, 67.01, 72.88, 103.82, 106.85, 120.98, 114.17, 121.47, 135.5, 115.86, 144.96, 153.03, -9.14, 10.75, 24.94, 21.01, 30.52, 46.68, 38.74, -0.19, -147.72, -78.47, -66.9, 115.22, 121.57, 105.85, 90.41, 85.32, 69.24, -151.26, -97.14, -63.58, -79.52, -84.08, -79.03, -68.15, -56.16, -47.88, -60.02 };

        // ---- towns found on the fly: the list above is only the start. Whatever place the map is looking at, OpenStreetMap is asked
        // for its cities and towns (bigger ones from far away, smaller ones closer), so no place is left out.
        private static readonly List<string> XName = new List<string>();
        private static readonly List<double> XLat = new List<double>(), XLon = new List<double>();
        private static readonly List<int> XTier = new List<int>();
        private static readonly HashSet<string> xCells = new HashSet<string>();
        private static readonly int LocalStart = MapNames.Length; // (no built-in local towns: GeoNames covers every town)
        private static int TCount => MapNames.Length + XName.Count;
        private static string TNm(int i) => i < MapNames.Length ? MapNames[i] : XName[i - MapNames.Length];
        private static double TLa(int i) => i < MapNames.Length ? MapLat[i] : XLat[i - MapNames.Length];
        private static double TLo(int i) => i < MapNames.Length ? MapLon[i] : XLon[i - MapNames.Length];
        private static int TMin(int i) => i < MapNames.Length ? (i < LocalStart ? 3 : i < LocalStart + 22 ? 9 : 10) : XTier[i - MapNames.Length];

        // Towns come from GeoNames (Resources/Towns.bytes: every place of 5000 people or more, 70,000 of them, CC BY 4.0), so no place is left out.
        private static string[] gName; private static float[] gLat, gLon; private static int[] gPop;
        private static readonly HashSet<int> gUsed = new HashSet<int>();
        private static bool LoadTowns()
        {
            if (gName != null) return true;
            var ta = Resources.Load<TextAsset>("Towns");
            if (ta == null) return false;
            string[] lines = ta.text.Split('\n'); Resources.UnloadAsset(ta);
            var n = new List<string>(lines.Length); var la = new List<float>(lines.Length); var lo = new List<float>(lines.Length); var po = new List<int>(lines.Length);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string ln in lines)
            {
                var p = ln.Split('\t'); if (p.Length < 4) continue;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float a) || !float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float b)) continue;
                int.TryParse(p[3], out int pop);
                n.Add(p[0]); la.Add(a); lo.Add(b); po.Add(pop);
            }
            gName = n.ToArray(); gLat = la.ToArray(); gLon = lo.ToArray(); gPop = po.ToArray();
            return true;
        }

        private IEnumerator TownsRoutine()
        {
            if (globeZoom < TownZoom || !LoadTowns()) yield break;
            float z = Zoom, ax = 0.92f * SkyAspect;
            double halfLat = Math.Min(8.0, 0.76 / z * Mathf.Rad2Deg), cosl = Math.Max(0.15, Math.Cos(globeLat * Math.PI / 180.0));
            double halfLon = Math.Min(14.0, ax / z * Mathf.Rad2Deg / cosl);
            int minPop = globeZoom <= 8 ? 100000 : globeZoom == 9 ? 40000 : globeZoom == 10 ? 15000 : globeZoom == 11 ? 8000 : 5000;
            double s = globeLat - halfLat * 1.6, nn = globeLat + halfLat * 1.6, w = globeLon - halfLon * 1.6, e = globeLon + halfLon * 1.6;
            var pick = new List<int>();
            for (int i = 0; i < gName.Length; i++)
            {
                if (gPop[i] < minPop) break; // the file is sorted by population: nothing smaller follows
                if (gLat[i] < s || gLat[i] > nn || gUsed.Contains(i)) continue;
                double lo = gLon[i]; if (lo < w - 360 * 0 && lo + 360 >= w && lo + 360 <= e) lo += 360; else if (lo > e && lo - 360 >= w) lo -= 360;
                if (lo < w || lo > e) continue;
                pick.Add(i);
                if (pick.Count >= 140) break;
            }
            foreach (int i in pick)
            {
                gUsed.Add(i);
                if (XName.Count > 3000) break;
                bool dup = false;
                for (int k = 0; k < TCount && !dup; k++)
                {
                    if (Math.Abs(TLa(k) - gLat[i]) < 0.06 && Math.Abs(TLo(k) - gLon[i]) < 0.06 / cosl) dup = true;
                    else if (string.Equals(TNm(k), gName[i], StringComparison.OrdinalIgnoreCase) && Math.Abs(TLa(k) - gLat[i]) < 0.6) dup = true;
                }
                if (dup) continue;
                int pop = gPop[i];
                XName.Add(gName[i]); XLat.Add(gLat[i]); XLon.Add(gLon[i]); XTier.Add(pop >= 1000000 ? 5 : pop >= 250000 ? 7 : pop >= 100000 ? 8 : pop >= 40000 ? 9 : pop >= 15000 ? 10 : 11);
            }
            yield break;
        }

        private List<int> MapCities()
        {
            Place(out double clat, out double clon);
            var idx = new List<int>();
            for (int i = 0; i < TCount; i++) idx.Add(i);
            idx.Sort((a, b) => Dist(a, clat, clon).CompareTo(Dist(b, clat, clon)));
            return idx;
        }
        private static double Dist(int i, double clat, double clon)
        {
            double dx = (TLo(i) - clon) * Math.Cos(clat * Math.PI / 180.0), dy = TLa(i) - clat;
            return dx * dx + dy * dy;
        }

        // ---- the globe: a sphere drawn from the world map, turned with the buttons; today's weather in the cities facing you
        private float globeLon, globeLat;
        private Texture2D globeTex;
        private Color32[] globePix;
        private const int GH = 240, MapW = 3072, MapH = 1536, TownZoom = 7;
        private static readonly float[] GlobeZoom = { 0.7f, 0.95f, 1.35f, 2f, 3.2f, 5f, 8f, 14f, 26f, 48f, 80f, 140f, 280f, 560f, 1100f, 2200f, 4400f, 8800f, 17600f, 30000f }; // the last two: close enough to pick out a house
        private static readonly int[] GlobeMax = { 9, 9, 11, 13, 16, 20, 24, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 };
        private const int SatZoom = 8; // from here on (a region about 10 degrees wide) the map is a real satellite photo
        private float zoomAnim = -1f; // between two zoom steps the sphere is drawn at this in-between zoom
        private float Zoom => zoomAnim > 0f ? zoomAnim : GlobeZoom[Mathf.Clamp(globeZoom, 0, GlobeZoom.Length - 1)];
        private static float SkyAspect => Mathf.Clamp(Screen.width / (0.8f * Screen.height), 1.2f, 3f);
        private readonly Dictionary<int, JToken> cityWx = new Dictionary<int, JToken>();
        private readonly Dictionary<int, float> stateWx = new Dictionary<int, float>(); // state / province index -> temperature in F
        private static readonly string[] StAbbr = { "AL", "AZ", "AR", "CA", "CO", "FL", "GA", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "SC", "SD", "TN", "TX", "UT", "VA", "WA", "WI", "WY", "ON", "QC", "AB", "BC", "SON", "CHH", "BCN", "AK", "HI", "ME", "VT", "NH", "MA", "CT", "RI", "NJ", "DE", "MD", "WV", "BEIJING", "TIANJIN", "HEBEI", "SHANXI", "INNER MONGOLIA", "LIAONING", "JILIN", "HEILONGJIANG", "SHANGHAI", "JIANGSU", "ZHEJIANG", "ANHUI", "FUJIAN", "JIANGXI", "SHANDONG", "HENAN", "HUBEI", "HUNAN", "GUANGDONG", "GUANGXI", "HAINAN", "CHONGQING", "SICHUAN", "GUIZHOU", "YUNNAN", "TIBET", "SHAANXI", "GANSU", "QINGHAI", "NINGXIA", "XINJIANG", "RAJASTHAN", "GUJARAT", "MAHARASHTRA", "MADHYA PRADESH", "UTTAR PRADESH", "BIHAR", "WEST BENGAL", "ODISHA", "KARNATAKA", "TAMIL NADU", "ANDHRA PRADESH", "TELANGANA", "KERALA", "PUNJAB", "HARYANA", "ASSAM", "CHHATTISGARH", "JHARKHAND", "KASHMIR", "AMAZONAS", "PARA", "MATO GROSSO", "BAHIA", "MINAS GERAIS", "SAO PAULO", "PARANA", "RIO GRANDE DO SUL", "GOIAS", "MARANHAO", "CEARA", "PERNAMBUCO", "W. AUSTRALIA", "N. TERRITORY", "QUEENSLAND", "S. AUSTRALIA", "NEW SOUTH WALES", "VICTORIA", "TASMANIA", "MANITOBA", "SASKATCHEWAN", "NOVA SCOTIA", "NEW BRUNSWICK", "NEWFOUNDLAND", "YUKON", "NWT", "NUNAVUT", "JALISCO", "VERACRUZ", "OAXACA", "CHIAPAS", "YUCATAN", "COAHUILA", "NUEVO LEON", "TAMAULIPAS", "DURANGO", "SINALOA", "MICHOACAN", "GUERRERO", "PUEBLA", "ZACATECAS", "SAN LUIS POTOSI", "KRASNOYARSK", "SAKHA", "IRKUTSK", "KHABAROVSK", "PRIMORSKY", "CHUKOTKA", "TYUMEN", "ARKHANGELSK", "MURMANSK", "KRASNODAR", "TATARSTAN", "NOVOSIBIRSK", "CHELYABINSK" };
        private bool spinning, globeDirty = true;
        private string lastCell, pendingCell;

        private void EnterGlobe()
        {
            if (Place(out double a, out double o)) { globeLat = Mathf.Clamp((float) a, -60f, 60f); globeLon = (float) o; }
            else { globeLat = 25f; globeLon = -30f; }
            globe = true; globeDirty = true; pinName = null; pendingCell = "Cell My place"; Render(); FetchGlobe(); // the light starts on the friendliest button
        }

        // one press: back over the viewer's own city, close enough to read the towns around it
        // the place found last (your street address, say) becomes "My place": the globe's home and the forecast's town
        private void SetHome()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(HomeKey, pinName.Replace("|", " ") + "|" + pinLat.ToString(inv) + "|" + pinLon.ToString(inv)); PlayerPrefs.Save();
            Fetch(); // the forecast follows My place
            message = "Saved: My place is now " + pinName; pinName = "My place"; pendingCell = "Cell My place";
            globeDirty = true; Render();
        }

        // back to the internet's guess of where the TV is
        private void ForgetHome()
        {
            PlayerPrefs.DeleteKey(HomeKey); PlayerPrefs.Save(); Fetch(); // the forecast goes back to the internet's guess
            message = "Removed: My place is your town again"; pinName = null; pendingCell = "Cell My place";
            globeDirty = true; Render();
        }

        private void GoHome()
        {
            double a, o;
            if (spinning || !(CityIndex == 0 && Home(out _, out a, out o)) && !Place(out a, out o)) return;
            bool house = CityIndex == 0 && Home(out _, out _, out _);
            pinName = house ? "My place" : null; pinLat = (float) a; pinLon = (float) o;
            StartCoroutine(GlideTo((float) a, (float) o, house ? GlobeZoom.Length - 3 : Mathf.Max(globeZoom, 10))); // your house, close up
            return;
            globeDirty = true; Render(); FetchGlobe();
        }

        // one zoom step, glided: the sphere grows (or shrinks) over a few frames with the town names hidden, then the names come back
        // in their right places. Nothing is drawn at an old zoom, so names never drift along with the picture.
        private int townCity = -1;
        private void ZoomBy(int d)
        {
            int to = Mathf.Clamp(globeZoom + d, 0, GlobeZoom.Length - 1);
            if (spinning || to == globeZoom) return;
            StartCoroutine(ZoomAnim(to));
        }

        // the whole globe in view: Up / Down turn it on over the poles and round to the far side (it shows upside down there, like a
        // real globe; the names stay upright). Closer in, the map keeps north up and stops short of the poles.
        private bool FreeSpin => globeZoom <= 2;
        private float LatStep(float lat) => FreeSpin ? Mathf.Repeat(lat + 180f, 360f) - 180f : Mathf.Clamp(lat, -70f, 70f);
        private void NorthUp()
        {
            if (Mathf.Abs(globeLat) <= 90f) { globeLat = Mathf.Clamp(globeLat, -70f, 70f); return; }
            globeLat = Mathf.Clamp(Mathf.Sign(globeLat) * 180f - globeLat, -70f, 70f); globeLon += 180f; // the same spot, turned the right way up
        }

        private IEnumerator ZoomAnim(int to)
        {
            if (to > 2) NorthUp();
            spinning = true;
            HideLabels();
            float a = Mathf.Log(Zoom), b = Mathf.Log(GlobeZoom[to]), t0 = Time.unscaledTime, dur = 0.22f;
            for (float k = 0f; k < 1f; )
            {
                k = Mathf.Clamp01((Time.unscaledTime - t0) / dur); float e = k * (2f - k); // ease out: no stop-start between held steps
                zoomAnim = Mathf.Exp(Mathf.Lerp(a, b, e));
                if (!SatCovers()) RenderSphere(true);
                PlaceSat();
                yield return null;
            }
            zoomAnim = -1f; globeZoom = to; spinning = false; townCity = -1;
            globeDirty = true; Render(); FetchGlobe();
        }

        private void OnDestroy() { if (globeTex != null) Destroy(globeTex); ShowMenuBehind(true); }

        // the Forecast covers the whole screen, but the menu under it (wallpaper, animated tiles, clock, the channel's preview) was
        // still drawn every frame: the remote felt slow here only. It is switched off while the Forecast is up.
        private Canvas behind;
        private void ShowMenuBehind(bool on)
        {
            if (!on && behind == null)
            {
                var app = AppController._instance;
                var c = app != null && app.gameCanvas != null ? app.gameCanvas.GetComponentInParent<Canvas>() : null;
                var mine = GetComponentInParent<Canvas>();
                if (c != null) c = c.rootCanvas;
                if (c == null || (mine != null && mine.rootCanvas == c)) return; // same canvas: leave it alone
                behind = c;
            }
            if (behind != null) behind.enabled = on;
            if (on) behind = null;
        }
        private void Start() { ShowMenuBehind(false); }

        // GlobeData.bytes: three MapW x MapH byte fields from public-domain Natural Earth data.
        // land: 128 + 16 x signed distance to the coast in map pixels (land positive); countries / states: 16 x distance to the border line.
        // Sampling a distance field gives lines and coasts of constant crispness at any zoom.
        private static byte[] gData;
        // ---- the real Earth: Esri's World Imagery at tile zoom 3 (64 tiles, fetched once), turned into a 2048 x 1024 latitude/longitude
        // photo kept on the TV (6 MB). The sphere samples it like the drawn map, so the globe looks like the satellite views close up.
        private const int PW = 2048, PH = 1024;
        private static byte[] photo; private static bool photoLoading;
        private void EnsurePhoto()
        {
            if (photo != null || photoLoading) return;
            string f = System.IO.Path.Combine(Application.persistentDataPath, "globe_photo.rgb");
            try { if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length == PW * PH * 3) { photo = System.IO.File.ReadAllBytes(f); return; } } catch (Exception) { }
            StartCoroutine(PhotoRoutine(f));
        }
        private IEnumerator PhotoRoutine(string file)
        {
            photoLoading = true;
            var buf = new byte[PW * PH * 3];
            // the photo row each output row (latitude) comes from, in Web Mercator pixels at zoom 3 (2048 tall)
            var srcRow = new int[PH];
            for (int y = 0; y < PH; y++)
            {
                double lat = Math.Max(-85.0511, Math.Min(85.0511, 90.0 - (y + 0.5) * 180.0 / PH)) * Math.PI / 180.0;
                srcRow[y] = Mathf.Clamp((int) ((0.5 - Math.Log(Math.Tan(Math.PI / 4 + lat / 2)) / (2 * Math.PI)) * 2048.0), 0, 2047);
            }
            int got = 0;
            for (int ty = 0; ty < 8; ty++)
            {
                var reqs = new UnityWebRequest[8];
                for (int tx = 0; tx < 8; tx++) { reqs[tx] = UnityWebRequestTexture.GetTexture("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/3/" + ty + "/" + tx); reqs[tx].timeout = 20; reqs[tx].SendWebRequest(); }
                while (Array.Exists(reqs, r => !r.isDone)) yield return null;
                for (int tx = 0; tx < 8; tx++)
                {
                    var rq = reqs[tx];
                    if (rq.result == UnityWebRequest.Result.Success)
                    {
                        var t = DownloadHandlerTexture.GetContent(rq);
                        if (t != null && t.width == 256 && t.height == 256)
                        {
                            var px = t.GetPixels32(); got++;
                            for (int y = 0; y < PH; y++)
                            {
                                int sy = srcRow[y] - ty * 256; if (sy < 0 || sy > 255) continue;
                                int srow = (255 - sy) * 256, drow = (y * PW + tx * 256) * 3; // Unity's pixels run bottom-up
                                for (int x = 0; x < 256; x++) { var c = px[srow + x]; int d = drow + x * 3; buf[d] = c.r; buf[d + 1] = c.g; buf[d + 2] = c.b; }
                            }
                        }
                        if (t != null) Destroy(t);
                    }
                    rq.Dispose();
                }
                yield return null;
            }
            Debug.Log("[Globe] world photo: " + got + "/64 tiles");
            if (got == 64)
            {
                photo = buf; globeDirty = true;
                if (globe && !spinning) Render(); // the real Earth shows the moment it's here
                try { System.IO.File.WriteAllBytes(file, buf); } catch (Exception e) { Debug.Log("[Globe] " + e.Message); }
            }
            photoLoading = false;
        }
        private static void PhotoAt(float u, float lat, out float r, out float g, out float b)
        {
            float fx = u * PW - 0.5f, fy = (90f - lat) / 180f * PH - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy); float tx = fx - x0, ty = fy - y0;
            int x1 = (x0 + 1 + PW) % PW; x0 = (x0 + PW) % PW; int y1 = Mathf.Clamp(y0 + 1, 0, PH - 1); y0 = Mathf.Clamp(y0, 0, PH - 1);
            int a = (y0 * PW + x0) * 3, bb = (y0 * PW + x1) * 3, c = (y1 * PW + x0) * 3, d = (y1 * PW + x1) * 3;
            float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
            r = (photo[a] * w00 + photo[bb] * w10 + photo[c] * w01 + photo[d] * w11) * 1.12f;
            g = (photo[a + 1] * w00 + photo[bb + 1] * w10 + photo[c + 1] * w01 + photo[d + 1] * w11) * 1.12f;
            b = (photo[a + 2] * w00 + photo[bb + 2] * w10 + photo[c + 2] * w01 + photo[d + 2] * w11) * 1.12f;
        }

        private bool LoadMapPixels()
        {
            EnsurePhoto();
            if (gData != null) return true;
            var ta = Resources.Load<TextAsset>("GlobeData");
            if (ta == null) return false;
            gData = ta.bytes; Resources.UnloadAsset(ta);
            if (gData.Length < MapW * MapH * 3) return false;
            SmoothCoasts();
            return true;
        }

        // the coast field was made from a pixel mask, so close up its edges stepped like stairs and small lakes showed as squares:
        // one 3x3 smoothing pass (once, when the globe first opens) rounds them off
        private static void SmoothCoasts()
        {
            var tmp = new byte[MapW * MapH];
            for (int y = 0; y < MapH; y++)
            {
                int row = y * MapW;
                for (int x = 0; x < MapW; x++)
                    tmp[row + x] = (byte) ((gData[row + (x + MapW - 1) % MapW] + gData[row + x] * 2 + gData[row + (x + 1) % MapW] + 2) >> 2);
            }
            for (int y = 0; y < MapH; y++)
            {
                int up = Mathf.Max(0, y - 1) * MapW, row = y * MapW, dn = Mathf.Min(MapH - 1, y + 1) * MapW;
                for (int x = 0; x < MapW; x++) gData[row + x] = (byte) ((tmp[up + x] + tmp[row + x] * 2 + tmp[dn + x] + 2) >> 2);
            }
        }

        private static float Field(int off, float fu, float fv)
        {
            int x0 = Mathf.FloorToInt(fu), y0 = Mathf.FloorToInt(fv);
            float tx = fu - x0, ty = fv - y0;
            int x1 = (x0 + 1 + MapW) % MapW; x0 = (x0 + MapW) % MapW;
            int y1 = Mathf.Clamp(y0 + 1, 0, MapH - 1); y0 = Mathf.Clamp(y0, 0, MapH - 1);
            float a = gData[off + y0 * MapW + x0], b = gData[off + y0 * MapW + x1], c = gData[off + y1 * MapW + x0], d = gData[off + y1 * MapW + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        // the sphere seen from (globeLon, globeLat), drawn over the whole sky: each pixel looks up the map (smoothed), shaded darker
        // toward the edge with a blue rim. Close up the sphere is bigger than the sky, so it fills the screen like the Wii's.
        private void RenderSphere(bool fast = false)
        {
            if (!LoadMapPixels()) return;
            int st = fast ? 2 : 1; // turning: every other pixel, copied over its neighbours (a quarter of the work); at rest: all of them
            int H = GH, W = Mathf.RoundToInt(GH * SkyAspect);
            if (globeTex == null || globeTex.width != W)
            {
                if (globeTex != null) Destroy(globeTex);
                globeTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                globePix = new Color32[W * H];
            }
            float R = H * 0.5f * Zoom, cx = W * 0.5f, cy = H * 0.5f;
            float ppd = R * Mathf.Deg2Rad; bool gridOn = Zoom >= 5f; float gridStep = ppd > 250f ? 0.1f : ppd > 60f ? 0.5f : ppd > 15f ? 2f : 5f;
            float la = globeLat * Mathf.Deg2Rad, ca = Mathf.Cos(la), sa = Mathf.Sin(la);
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < H; y += st)
            {
                float ny = (y + 0.5f - cy) / R;
                int row = y * W;
                if (ny <= -1f || ny >= 1f) { for (int x = 0; x < W * st && row + x < globePix.Length; x++) globePix[row + x] = clear; continue; }
                float half = Mathf.Sqrt(1f - ny * ny) * R;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - half)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + half));
                for (int x = 0; x < x0; x++) globePix[row + x] = clear;
                for (int x = x1 + 1; x < W; x++) globePix[row + x] = clear;
                for (int x = x0; x <= x1; x += st)
                {
                    float nx = (x + 0.5f - cx) / R, r2 = nx * nx + ny * ny;
                    if (r2 >= 1f) { globePix[row + x] = clear; continue; }
                    float nz = Mathf.Sqrt(1f - r2);
                    float yy = ny * ca + nz * sa, zz = nz * ca - ny * sa;
                    float lat = Mathf.Asin(Mathf.Clamp(yy, -1f, 1f)) * Mathf.Rad2Deg;
                    float u = (globeLon + Mathf.Atan2(nx, zz) * Mathf.Rad2Deg + 180f) / 360f; u -= Mathf.Floor(u);
                    float fu = u * MapW - 0.5f, fv = (90f - lat) / 180f * MapH - 0.5f;
                    float spm = ppd / (MapW / 360f); // screen pixels per map pixel
                    float sd = (Field(0, fu, fv) - 128f) / 16f * spm; // screen pixels from the coast, land positive
                    float land = Mathf.Clamp01(0.5f + sd);
                    float deg = (Field(0, fu, fv) - 128f) / 16f / (MapW / 360f);
                    float mr, mg, mb;
                    // ocean: deep blue, paler in the shallows along the coast
                    float shallow = deg < 0f ? Mathf.Exp(deg * 1.6f) * 0.55f : 0f;
                    float or_ = Mathf.Lerp(22f, 70f, shallow), og = Mathf.Lerp(82f, 160f, shallow), ob = Mathf.Lerp(166f, 225f, shallow);
                    float ice = Mathf.Clamp01((Mathf.Abs(lat) - 64f) / 8f);
                    float tone = 1f + 0.07f * Mathf.Sin(u * 62f) * Mathf.Sin(lat * 0.37f + 1f);
                    float lr = Mathf.Lerp(96f * tone, 232f, ice), lg = Mathf.Lerp(158f * tone, 240f, ice), lb = Mathf.Lerp(72f * tone, 246f, ice);
                    mr = Mathf.Lerp(or_, lr, land); mg = Mathf.Lerp(og, lg, land); mb = Mathf.Lerp(ob, lb, land);
                    if (photo != null) PhotoAt(u, lat, out mr, out mg, out mb); // the real Earth once its photo is on the TV
                    if (land > 0.02f)
                    {
                        // borders: countries always, states and provinces once close enough to read
                        float cd = Field(MapW * MapH, fu, fv) / 16f * spm;
                        float ca2 = Mathf.Clamp01(1.4f - cd) * 0.75f;
                        float sa2 = 0f;
                        if (ppd > 3.5f) { float sdd = Field(MapW * MapH * 2, fu, fv) / 16f * spm; sa2 = Mathf.Clamp01(1.2f - sdd) * Mathf.Clamp01((ppd - 3.5f) / 4f) * 0.55f; }
                        float ba = Mathf.Max(ca2, sa2) * land * (photo != null ? 0.55f : 1f); // on the photo: faint lines, as on a real map
                        mr = Mathf.Lerp(mr, 255f, ba); mg = Mathf.Lerp(mg, 255f, ba); mb = Mathf.Lerp(mb, 245f, ba);
                    }
                    if (gridOn)
                    {
                        float dLat = Mathf.Abs(Mathf.Repeat(lat / gridStep + 0.5f, 1f) - 0.5f) * gridStep * ppd;
                        float dLon = Mathf.Abs(Mathf.Repeat(u * 360f / gridStep + 0.5f, 1f) - 0.5f) * gridStep * ppd * Mathf.Cos(lat * Mathf.Deg2Rad);
                        if (Mathf.Min(dLat, dLon) < 0.7f) { mr = Mathf.Lerp(mr, 255f, 0.1f); mg = Mathf.Lerp(mg, 255f, 0.1f); mb = Mathf.Lerp(mb, 255f, 0.1f); }
                    }
                    float light = 0.5f + 0.5f * nz;
                    float rim = Mathf.Clamp01((r2 - 0.72f) / 0.28f); rim *= rim;
                    float a = r2 > 0.96f ? Mathf.Clamp01((1f - r2) / 0.04f) : 1f;
                    globePix[row + x] = new Color32(
                        (byte) Mathf.Min(255f, mr * light * (1f - rim) + 70f * rim),
                        (byte) Mathf.Min(255f, mg * light * (1f - rim) + 150f * rim),
                        (byte) Mathf.Min(255f, mb * light * (1f - rim) + 255f * rim),
                        (byte) (255f * a));
                }
                if (st == 2)
                {
                    for (int x = x0; x <= x1 && x + 1 < W; x += 2) globePix[row + x + 1] = globePix[row + x];
                    if (y + 1 < H) System.Array.Copy(globePix, row, globePix, row + W, W);
                }
            }
            globeTex.SetPixels32(globePix); globeTex.Apply(false);
            globeDirty = false;
        }

        // cities facing the viewer: x = index, y/z = screen offset in sphere radii, nearest the middle first and spaced apart
        private List<Vector3> VisibleCities()
        {
            var all = new List<Vector3>();
            if (globeZoom < 3) return all; // the whole globe: country names only
            float z = Zoom, ax = 0.92f * SkyAspect;
            float la = globeLat * Mathf.Deg2Rad, ca = Mathf.Cos(la), sa = Mathf.Sin(la);
            for (int i = 0; i < TCount; i++)
            {
                // like Apple Maps: big cities show from far away, small towns only once you are close enough to be near them
                if (globeZoom < TMin(i)) continue;
                float lat = (float) TLa(i) * Mathf.Deg2Rad, dl = ((float) TLo(i) - globeLon) * Mathf.Deg2Rad;
                float x = Mathf.Cos(lat) * Mathf.Sin(dl), y0 = Mathf.Sin(lat), z0 = Mathf.Cos(lat) * Mathf.Cos(dl);
                float ny = y0 * ca - z0 * sa, nz = y0 * sa + z0 * ca;
                if (nz < 0.25f || Mathf.Abs(x) * z > ax || Mathf.Abs(ny) * z > 0.76f) continue;
                all.Add(new Vector3(i, x, ny));
            }
            // bigger places first (a big city is never pushed out by a small town next to it), then the ones nearest the middle
            all.Sort((p, q) => { int c = TMin((int) p.x).CompareTo(TMin((int) q.x)); return c != 0 ? c : (p.y * p.y + p.z * p.z).CompareTo(q.y * q.y + q.z * q.z); });
            var shown = new List<Vector3>();
            foreach (var v in all)
            {
                bool crowded = false;
                foreach (var o in shown) if (Mathf.Abs(o.y - v.y) * z < (globeZoom >= 6 ? 0.55f : 0.8f) && Mathf.Abs(o.z - v.z) * z < (globeZoom >= 6 ? 0.28f : 0.44f)) { crowded = true; break; }
                if (!crowded && shown.Count < GlobeMax[Mathf.Clamp(globeZoom, 0, GlobeMax.Length - 1)]) shown.Add(v);
            }
            return shown;
        }

        public void FetchGlobe() { StopCoroutine("GlobeRoutine"); StartCoroutine("GlobeRoutine"); if (globeZoom >= SatZoom - 1) StartCoroutine(SatRoutine()); }

        // ---- satellite photo: from SatZoom on, the map is Esri's World Imagery (tiles fetched around the view, stitched into one texture)
        private Texture2D satTex;
        private int satZ, satTx0, satTy0, satNx, satNy;
        private bool satBusy, satAgain;
        private const double MPP0 = 156543.03392;

        // where the view's centre falls on the tile grid at tile zoom Z; s = interface units per tile pixel
        private void SatView(int Z, out double cxp, out double cyp, out float s, out float halfW, out float halfH)
        {
            float rs = h * 0.4f * Zoom, wUi = body != null && body.rect.width > 1f ? body.rect.width : h * 1.78f;
            double m = 6371000.0 / rs, lat = Math.Max(-80.0, Math.Min(80.0, globeLat)), phi = lat * Math.PI / 180.0, cos = Math.Cos(phi);
            double n = Math.Pow(2.0, Z), world = 256.0 * n;
            double lonN = ((globeLon + 180.0) % 360.0 + 360.0) % 360.0; // the globe's longitude can run past ±180 after long turns / flights
            cxp = lonN / 360.0 * world;
            cyp = (0.5 - Math.Log((1.0 + Math.Sin(phi)) / (1.0 - Math.Sin(phi))) / (4.0 * Math.PI)) * world;
            s = (float) (MPP0 * cos / n / m);
            halfW = wUi * 0.5f / s; halfH = h * 0.4f / s;
        }

        private int SatWanted()
        {
            float rs = h * 0.4f * Zoom; double m = 6371000.0 / rs, phi = Math.Max(-80.0, Math.Min(80.0, globeLat)) * Math.PI / 180.0;
            double k = 0.5 * Screen.height / h;
            return Mathf.Clamp(Mathf.RoundToInt((float) Math.Log(MPP0 * Math.Cos(phi) * k / m, 2.0)), 6, 20);
        }

        private bool SatCovers()
        {
            if (satTex == null || Mathf.Abs(satZ - SatWanted()) > 1) return false;
            SatView(satZ, out double cx, out double cy, out float s, out float hw, out float hh);
            return cx - hw >= satTx0 * 256.0 && cx + hw <= (satTx0 + satNx) * 256.0 && cy - hh >= satTy0 * 256.0 && cy + hh <= (satTy0 + satNy) * 256.0;
        }

        private IEnumerator SatRoutine()
        {
            if (satBusy) { satAgain = true; yield break; }
            satBusy = true;
            do { satAgain = false; yield return SatOnce(); } while (satAgain && globe);
            satBusy = false;
        }

        private IEnumerator SatOnce()
        {
            if (!globe || (Zoom < GlobeZoom[SatZoom - 1] && globeZoom < SatZoom - 1) || (spinning && !holdTurning && !zoomHolding) || SatCovers()) yield break;
            int Z = SatWanted(), tx0 = 0, ty0 = 0, nx = 0, ny = 0;
            for (int margin = 1; ; )
            {
                SatView(Z, out double cx, out double cy, out float s, out float hw, out float hh);
                int n = 1 << Z;
                tx0 = Mathf.Max(0, Mathf.FloorToInt((float) ((cx - hw) / 256.0)) - margin); int tx1 = Mathf.Min(n - 1, Mathf.FloorToInt((float) ((cx + hw) / 256.0)) + margin);
                ty0 = Mathf.Max(0, Mathf.FloorToInt((float) ((cy - hh) / 256.0)) - margin); int ty1 = Mathf.Min(n - 1, Mathf.FloorToInt((float) ((cy + hh) / 256.0)) + margin);
                nx = tx1 - tx0 + 1; ny = ty1 - ty0 + 1;
                if (nx * ny <= 36 && nx <= 8) break;
                if (margin > 0) margin = 0; else if (Z > 6) Z--; else break;
            }
            if (nx <= 0 || ny <= 0) yield break; // nothing to fetch (never a negative tile count)
            var reqs = new UnityWebRequest[nx * ny];
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var rq = UnityWebRequestTexture.GetTexture("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/" + Z + "/" + (ty0 + j) + "/" + (tx0 + i));
                rq.timeout = 15; rq.SendWebRequest(); reqs[j * nx + i] = rq;
            }
            // place names + borders always; road names from street level on
            // regional views show the TV's own city names with the weather; the photo's place names join in close up
            string[] layers = Z >= 13 ? new[] { "World_Transportation", "World_Boundaries_and_Places" } : globeZoom >= 11 ? new[] { "World_Boundaries_and_Places" } : new string[0];
            var refs = new UnityWebRequest[layers.Length * nx * ny];
            for (int L = 0; L < layers.Length; L++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var rq = UnityWebRequestTexture.GetTexture("https://server.arcgisonline.com/ArcGIS/rest/services/Reference/" + layers[L] + "/MapServer/tile/" + Z + "/" + (ty0 + j) + "/" + (tx0 + i));
                rq.timeout = 15; rq.SendWebRequest(); refs[(L * ny + j) * nx + i] = rq;
            }
            float until = Time.unscaledTime + 20f;
            while (Time.unscaledTime < until)
            {
                bool all = true;
                foreach (var rq in reqs) if (!rq.isDone) { all = false; break; }
                if (all) foreach (var rq in refs) if (!rq.isDone) { all = false; break; }
                if (all) break;
                yield return null;
            }
            int ok = 0;
            foreach (var rq in reqs) if (rq.isDone && rq.result == UnityWebRequest.Result.Success) ok++;
            Debug.Log($"[Sat] zoom {Z}: {ok}/{reqs.Length} tiles" + (ok < reqs.Length && reqs.Length > 0 ? " first error: " + reqs[0].error : ""));
            if (ok * 2 >= reqs.Length && globe)
            {
                var tex = new Texture2D(nx * 256, ny * 256, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                int done = 0;
                for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    var rq = reqs[j * nx + i];
                    if (!rq.isDone || rq.result != UnityWebRequest.Result.Success) continue;
                    var t = DownloadHandlerTexture.GetContent(rq);
                    if (t != null && t.width == 256 && t.height == 256)
                    {
                        var px = t.GetPixels32();
                        for (int L = 0; L < layers.Length; L++)
                        {
                            var rr = refs[(L * ny + j) * nx + i];
                            if (!rr.isDone || rr.result != UnityWebRequest.Result.Success) continue;
                            var ov = DownloadHandlerTexture.GetContent(rr);
                            if (ov != null && ov.width == 256 && ov.height == 256)
                            {
                                var op = ov.GetPixels32();
                                for (int q = 0; q < px.Length; q++)
                                {
                                    int oa = op[q].a; if (oa == 0) continue;
                                    var b = px[q]; var o = op[q];
                                    px[q] = new Color32((byte) ((o.r * oa + b.r * (255 - oa)) / 255), (byte) ((o.g * oa + b.g * (255 - oa)) / 255), (byte) ((o.b * oa + b.b * (255 - oa)) / 255), 255);
                                }
                            }
                            if (ov != null) Destroy(ov);
                        }
                        tex.SetPixels32(i * 256, (ny - 1 - j) * 256, 256, 256, px);
                    }
                    if (t != null) Destroy(t);
                    if (++done % 2 == 0) yield return null; // spread over frames: no hitch on slow chips
                }
                tex.Apply(false, true);
                var old = satTex; satTex = tex; satZ = Z; satTx0 = tx0; satTy0 = ty0; satNx = nx; satNy = ny;
                if (globe && !spinning) Render(); else PlaceSat();
                if (old != null) Destroy(old);
            }
            foreach (var rq in reqs) rq.Dispose();
            foreach (var rq in refs) rq.Dispose();
        }

        private IEnumerator GlobeRoutine()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // every state at once, the first time the map is wide enough to show them: their temperatures sit on the state names
            if (stateWx.Count == 0 && globeZoom >= 3 && globeZoom <= 10)
            {
                var sla = new List<string>(); var slo = new List<string>();
                for (int s = 0; s < StN.Length; s++) { sla.Add(StLat[s].ToString(inv)); slo.Add(StLon[s].ToString(inv)); }
                using (var sreq = UnityWebRequest.Get("https://api.open-meteo.com/v1/forecast?latitude=" + string.Join(",", sla) + "&longitude=" + string.Join(",", slo) + "&current=temperature_2m&temperature_unit=fahrenheit&timezone=auto"))
                {
                    sreq.timeout = 15;
                    yield return sreq.SendWebRequest();
                    if (sreq.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var stk = JToken.Parse(sreq.downloadHandler.text);
                            var sarr = stk is JArray sa ? sa : new JArray(stk);
                            for (int s = 0; s < StN.Length && s < sarr.Count; s++) if (sarr[s]["current"] != null) stateWx[s] = (float) sarr[s]["current"]["temperature_2m"];
                        }
                        catch (Exception) { }
                        if (globe && !spinning) Render();
                    }
                }
            }
            if (globeZoom >= TownZoom) yield return TownsRoutine();
            var need = new List<int>();
            foreach (var v in VisibleCities()) if (!cityWx.ContainsKey((int) v.x)) need.Add((int) v.x);
            if (need.Count == 0) yield break;
            var la = new List<string>(); var lo = new List<string>();
            foreach (int i in need) { la.Add(TLa(i).ToString(inv)); lo.Add(TLo(i).ToString(inv)); }
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + string.Join(",", la) + "&longitude=" + string.Join(",", lo)
                + "&current=temperature_2m,weather_code,is_day&temperature_unit=fahrenheit&timezone=auto";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                try
                {
                    var tk = JToken.Parse(req.downloadHandler.text);
                    var arr = tk is JArray a ? a : new JArray(tk);
                    for (int n = 0; n < need.Count && n < arr.Count; n++) if (arr[n]["current"] != null) cityWx[need[n]] = arr[n]["current"];
                }
                catch (Exception) { yield break; }
            }
            if (globe && !spinning) Render();
        }

        private IEnumerator Spin(float dLon, float dLat)
        {
            spinning = true;
            if (body != null) { var cr = body.Find("Space/Cities"); if (cr != null) { cr.gameObject.SetActive(false); Destroy(cr.gameObject); } cr = body.Find("Space/Regions"); if (cr != null) { cr.gameObject.SetActive(false); Destroy(cr.gameObject); } cr = body.Find("Space/Sat"); if (cr != null) { cr.gameObject.SetActive(false); Destroy(cr.gameObject); } }
            float lon0 = globeLon, lat0 = globeLat, lat1 = FreeSpin ? lat0 + dLat : Mathf.Clamp(lat0 + dLat, -70f, 70f);
            for (int i = 1; i <= 6; i++)
            {
                float k = i / 6f; k = k * k * (3f - 2f * k);
                globeLon = lon0 + dLon * k; globeLat = Mathf.Lerp(lat0, lat1, k);
                RenderSphere(true);
                yield return null;
            }
            globeLat = LatStep(globeLat);
            spinning = false;
            Render(); FetchGlobe();
        }
        // ---- getting to a place without a keyboard
        // "Next city": zoomed out, the world's biggest cities one after another (GeoNames, by population); zoomed in to the towns,
        // the nearest towns around where you started, one after another, stepping outward
        private int bigStep = -1, nearStep = -1;
        private List<int> nearList;
        private void NextCity()
        {
            if (spinning || !LoadTowns() || gName.Length == 0) return;
            int to;
            if (globeZoom < TownZoom)
            {
                bigStep = (bigStep + 1) % Mathf.Min(400, gName.Length);
                to = bigStep;
            }
            else
            {
                if (nearList == null)
                {
                    double cl = Math.Cos(globeLat * Math.PI / 180.0);
                    var all = new List<(double d, int i)>();
                    for (int i = 0; i < gName.Length; i++)
                    {
                        double dy = gLat[i] - globeLat, dx = Mathf.DeltaAngle(globeLon, gLon[i]) * cl;
                        double d = dx * dx + dy * dy;
                        if (d < 6.0 * 6.0) all.Add((d, i));
                    }
                    all.Sort((p, q) => p.d.CompareTo(q.d));
                    nearList = new List<int>();
                    foreach (var p in all) { if (p.d < 0.0004) continue; nearList.Add(p.i); if (nearList.Count >= 60) break; } // skip the one we're on
                    nearStep = -1;
                    if (nearList.Count == 0) return;
                }
                nearStep = (nearStep + 1) % nearList.Count;
                to = nearList[nearStep];
            }
            StartCoroutine(GlideTo(gLat[to], gLon[to], Mathf.Max(globeZoom, 11))); // close enough to see the town
        }

        // a smooth flight to a place: the sphere turns there (town names hidden while it moves), then settles at the wanted zoom
        private IEnumerator GlideTo(float lat, float lon, int zoom)
        {
            spinning = true; townCity = -1;
            HideLabels();
            float lon0 = globeLon, lat0 = globeLat, dLon = Mathf.DeltaAngle(lon0, lon), lat1 = Mathf.Clamp(lat, -70f, 70f);
            float z0 = Mathf.Log(Zoom), z1 = Mathf.Log(GlobeZoom[Mathf.Clamp(zoom, 0, GlobeZoom.Length - 1)]);
            const int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                float k = i / (float) steps; k = k * k * (3f - 2f * k);
                globeLon = lon0 + dLon * k; globeLat = Mathf.Lerp(lat0, lat1, k);
                zoomAnim = Mathf.Exp(Mathf.Lerp(z0, z1, k));
                RenderSphere(true); PlaceSat();
                yield return null;
            }
            zoomAnim = -1f; globeZoom = Mathf.Clamp(zoom, 0, GlobeZoom.Length - 1); spinning = false;
            globeLon = Mathf.Repeat(globeLon + 180f, 360f) - 180f; // back into -180..180 (a flight "west" to Shanghai ended at -239)
            pendingCell ??= "Cell " + (globeZoom >= TownZoom ? "Next town ►" : "Next city ►"); // the light stays on the button you're pressing
            globeDirty = true; Render(); FetchGlobe();
        }

        private void HideLabels()
        {
            if (body == null) return;
            foreach (var n in new[] { "Space/Cities", "Space/Regions" }) { var cr = body.Find(n); if (cr != null) { cr.gameObject.SetActive(false); Destroy(cr.gameObject); } }
        }

        // the satellite photo is pinned to the ground: wherever the view moves or zooms it is just moved/scaled, never fetched again
        // while it still covers the screen
        private bool SatUsable() { if (satTex == null) return false; int d = satZ - SatWanted(); return d >= -5 && d <= 6; } // a sharper photo than needed is fine (it just covers less)
        private void PlaceSat(RectTransform space = null)
        {
            if (body == null) return;
            if (space == null) space = body.Find("Space") as RectTransform; if (space == null) return;
            var p = space.Find("Sat/Photo");
            bool show = SatUsable() && Zoom >= GlobeZoom[SatZoom - 1] * 0.8f;
            if (p == null)
            {
                if (!show) return;
                var satRoot = TVServerScreen.Child(space, "Sat"); TVServerScreen.Stretch(satRoot); satRoot.gameObject.AddComponent<RectMask2D>();
                var sph = space.Find("Sphere"); if (sph != null) satRoot.SetSiblingIndex(sph.GetSiblingIndex() + 1);
                var npi = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
                npi.transform.SetParent(satRoot, false); npi.raycastTarget = false;
                var nrt = npi.rectTransform; nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f); nrt.pivot = new Vector2(0f, 1f);
                p = npi.transform;
            }
            var pi = p.GetComponent<RawImage>();
            pi.enabled = show; if (!show) return;
            pi.texture = satTex;
            SatView(satZ, out double scx, out double scy, out float ss, out _, out _);
            var prt = pi.rectTransform;
            prt.sizeDelta = new Vector2(satNx * 256f * ss, satNy * 256f * ss);
            prt.anchoredPosition = new Vector2(-(float) (scx - satTx0 * 256.0) * ss, (float) (scy - satTy0 * 256.0) * ss);
            float f = Mathf.InverseLerp(Mathf.Log(GlobeZoom[SatZoom - 1]), Mathf.Log(GlobeZoom[SatZoom]), Mathf.Log(Zoom));
            pi.color = new Color(1f, 1f, 1f, 0.35f + 0.65f * f); // fades in over the last step before the satellite zoom
        }

        // where (lat, lon) is drawn, relative to the view's centre (same maths as the names and towns)
        private Vector2 Project(float latDeg, float lonDeg, float rs)
        {
            float lat = latDeg * Mathf.Deg2Rad, dl = (lonDeg - globeLon) * Mathf.Deg2Rad, ga = globeLat * Mathf.Deg2Rad;
            float x = Mathf.Cos(lat) * Mathf.Sin(dl), y0 = Mathf.Sin(lat), z0 = Mathf.Cos(lat) * Mathf.Cos(dl);
            return new Vector2(x * rs, (y0 * Mathf.Cos(ga) - z0 * Mathf.Sin(ga)) * rs);
        }
        private float holdLat0, holdLon0; private bool holdSlideLabels; private float satPoll;

        // holding OK on an arrow button turns the globe smoothly for as long as it's held (speed builds up, smaller when zoomed in)
        private float holdFor, heldEndedAt = -10f; private Vector2 holdDir; private bool holdTurning;
        private bool HeldJustNow() => Time.unscaledTime - heldEndedAt < 0.4f;
        private float zoomHeldFor; private bool zoomHolding;

        // "Find a place": the TV's own on-screen keyboard; the typed name is looked up among ~70,000 places (biggest first) and the
        // globe flies there, close enough to see the town and its neighbours
        private TouchScreenKeyboard findKb, zipKb;
        // the ZIP code is typed on Revo's own number pad (the TV's keyboard looked out of place)
        private string zipPad, zipNote;
        public void AskZip()
        {
            zipPad = ""; zipNote = null; pendingCell = "Cell 1"; Render();
        }
        private void CloseZipPad() { zipPad = null; zipNote = null; pendingCell = settings ? "Change" : null; Render(); }
        private void CancelSearch()
        {
            pinName = null; message = null; nearList = null; globeDirty = true; pendingCell = "Cell Find a place"; Render();
        }
        private void ZipKey_(string k)
        {
            if (zipPad == null) return;
            zipNote = null;
            if (k == "Erase") { if (zipPad.Length > 0) zipPad = zipPad.Substring(0, zipPad.Length - 1); }
            else if (k == "OK")
            {
                if (zipPad.Length != 5) { zipNote = "Type all 5 numbers"; Render(); return; }
                string z = zipPad; zipPad = null;
                PlayerPrefs.SetString(ZipKey, z); PlayerPrefs.DeleteKey(CityKey); PlayerPrefs.Save();
                LoadCache(); page = 0; settings = false;
                message = "Finding ZIP code " + z + "...";
                pendingCell = "Cell Refresh"; Render(); Fetch();
                return;
            }
            else if (zipPad.Length < 5) zipPad += k;
            pendingCell = zipPad.Length == 5 && k != "Erase" ? "Cell OK" : "Cell " + k;
            Render();
        }
        private void DrawZipPad()
        {
            var blue = new Color(0.2f, 0.45f, 0.62f);
            Box(body, "Shade", 0f, 0f, 1f, 1f, new Color(0f, 0.03f, 0.12f, 0.55f));
            var panel = Box(body, "ZipPanel", 0.33f, 0.07f, 0.67f, 0.93f, new Color(0.93f, 0.94f, 0.96f, 0.98f));
            Box(panel, "Top", 0f, 0.985f, 1f, 1f, new Color(0.35f, 0.62f, 0.85f));
            Label(body, "Your ZIP code", 0.05f, blue, TextAlignmentOptions.Center, 0.33f, 0.83f, 0.67f, 0.91f);
            Label(body, "Your town's forecast comes from it", 0.024f, new Color(0.38f, 0.42f, 0.48f), TextAlignmentOptions.Center, 0.33f, 0.785f, 0.67f, 0.83f);
            for (int i = 0; i < 5; i++)
            {
                float x0 = 0.375f + i * 0.052f;
                bool next = i == zipPad.Length;
                var slot = Box(body, "Digit" + i, x0, 0.645f, x0 + 0.044f, 0.765f, next ? Color.white : new Color(0.98f, 0.99f, 1f));
                Box(slot, "Line", 0.12f, 0.08f, 0.88f, next ? 0.12f : 0.1f, next ? new Color(0.25f, 0.6f, 0.95f) : new Color(0.7f, 0.74f, 0.8f));
                if (i < zipPad.Length) Label(slot, zipPad[i].ToString(), 0.06f, new Color(0.12f, 0.18f, 0.26f), TextAlignmentOptions.Center, 0f, 0.1f, 1f, 1f);
            }
            if (zipNote != null) Label(body, zipNote, 0.024f, new Color(0.75f, 0.12f, 0.15f), TextAlignmentOptions.Center, 0.33f, 0.6f, 0.67f, 0.645f);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Erase", "0", "OK" };
            for (int k = 0; k < keys.Length; k++)
            {
                int row = k / 3, col = k % 3; string key = keys[k];
                float x0 = 0.37f + col * 0.088f, y1 = 0.59f - row * 0.105f;
                Cell(body, key, x0, x0 + 0.08f, y1 - 0.095f, y1, () => ZipKey_(key), false, false);
            }
            Cell(body, "Cancel", 0.43f, 0.57f, 0.09f, 0.155f, CloseZipPad, false, false);
        }
        private void PollZip()
        {
            if (zipKb == null || zipKb.status == TouchScreenKeyboard.Status.Visible) return;
            var kb = zipKb; zipKb = null;
            string z = System.Text.RegularExpressions.Regex.Replace(kb.text ?? "", "[^0-9A-Za-z -]", "").Trim();
            if (z.Length < 3) return; // nothing typed: keep what was there
            PlayerPrefs.SetString(ZipKey, z); PlayerPrefs.DeleteKey(CityKey); PlayerPrefs.Save();
            LoadCache(); page = 0; settings = false;
            message = "Finding ZIP code " + z + "...";
            Render(); Fetch();
        }
        private void OpenFind()
        {
            if (spinning) return;
            findKb = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, false, false, false, false, "Any place: address, landmark, town or country");
        }
        private void PollFind()
        {
            if (findKb == null || findKb.status == TouchScreenKeyboard.Status.Visible) return;
            var kb = findKb; findKb = null;
            // Back closes the keyboard too: whatever was typed is still looked up (only an empty box does nothing)
            string q = (kb.text ?? "").Trim();
            if (q.Length < 2) return;
            StartCoroutine(Search(q));
        }
        private string pinName; private float pinLat, pinLon;
        private class Choice { public string name; public float lat, lon; public int zoom; public double km; }
        private List<Choice> choices; // several places matched the search: the viewer picks one (nearest first)
        private string ChoiceLabel(int i)
        {
            var c = choices[i]; bool km = PlayerPrefs.GetInt(UnitKey, 0) == 1;
            double d = km ? c.km : c.km / 1.609344;
            string dist = d < 10 ? d.ToString("0.0") : d < 1000 ? d.ToString("0") : d.ToString("#,0");
            return (i + 1) + ".  " + c.name + "   ·   " + dist + (km ? " km" : " mi");
        }
        private void PickChoice(int i)
        {
            if (choices == null || i >= choices.Count) return;
            var c = choices[i]; choices = null;
            message = c.name; pinName = c.name.Split(',')[0]; pinLat = c.lat; pinLon = c.lon;
            pendingCell = "Cell Find a place"; nearList = null;
            StartCoroutine(GlideTo(c.lat, c.lon, c.zoom));
        }
        private IEnumerator Search(string q)
        {
            message = "Looking for \"" + q + "\"..."; Render();
            float lat = 0f, lon = 0f; int zoom = -1; string name = null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double refLat = globeLat, refLon = globeLon; if (Home(out _, out double hla, out double hlo) || Place(out hla, out hlo)) { refLat = hla; refLon = hlo; } // "near you" = My place (or the weather's town)
            // OpenStreetMap's search, twice: once only around you (so the town down the road is always found), once worldwide
            var found = new List<Choice>(); double topImp = 0; Choice famous = null;
            using (var rq = UnityWebRequest.Get("https://photon.komoot.io/api/?limit=12&lang=en&location_bias_scale=0.1&zoom=13&lat=" + refLat.ToString("0.####", inv) + "&lon=" + refLon.ToString("0.####", inv) + "&q=" + UnityWebRequest.EscapeURL(q)))
            {
                rq.timeout = 10; rq.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
                yield return rq.SendWebRequest();
                try
                {
                    if (rq.result == UnityWebRequest.Result.Success)
                        foreach (var ft in (JArray) JObject.Parse(rq.downloadHandler.text)["features"])
                        {
                            var c = new Choice { lon = (float) ft["geometry"]["coordinates"][0], lat = (float) ft["geometry"]["coordinates"][1] };
                            c.km = KmFrom(refLat, refLon, c.lat, c.lon);
                            if (c.km > 80 || found.Exists(o => Mathf.Abs(o.lat - c.lat) < 0.002f && Mathf.Abs(o.lon - c.lon) < 0.002f)) continue; // only the close ones here
                            var pr = ft["properties"]; var parts = new List<string>();
                            void Add(string v) { if (!string.IsNullOrEmpty(v) && !parts.Contains(v) && parts.Count < 3) parts.Add(v); }
                            string hn = (string) pr["housenumber"], st = (string) pr["street"];
                            Add((string) pr["name"]); if (st != null) Add(hn != null ? hn + " " + st : st); Add((string) pr["city"]); Add((string) pr["state"]);
                            c.name = string.Join(", ", parts); if (c.name.Length == 0) continue;
                            c.zoom = ZoomForPlace(pr["extent"] as JArray, (string) pr["type"], c.lat);
                            found.Add(c);
                        }
                }
                catch (Exception e) { Debug.Log("[Find] photon " + e.Message); }
            }
            foreach (bool local in new[] { true, false })
            {
                string box = local
                    ? "&bounded=1&viewbox=" + (refLon - 3).ToString("0.##", inv) + "," + (refLat + 3).ToString("0.##", inv) + "," + (refLon + 3).ToString("0.##", inv) + "," + (refLat - 3).ToString("0.##", inv)
                    : "&bounded=0&viewbox=" + (refLon - 5).ToString("0.##", inv) + "," + (refLat + 5).ToString("0.##", inv) + "," + (refLon + 5).ToString("0.##", inv) + "," + (refLat - 5).ToString("0.##", inv);
                if (!local) yield return new WaitForSecondsRealtime(1f); // the service asks for one search a second
                using (var rq = UnityWebRequest.Get("https://nominatim.openstreetmap.org/search?format=jsonv2&accept-language=en&limit=10" + box + "&q=" + UnityWebRequest.EscapeURL(q)))
                {
                    rq.timeout = 12; rq.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
                    yield return rq.SendWebRequest();
                    try
                    {
                        if (rq.result != UnityWebRequest.Result.Success) { Debug.Log("[Find] nominatim " + rq.error); continue; }
                        foreach (var r0 in JArray.Parse(rq.downloadHandler.text))
                        {
                            var c = new Choice { lat = float.Parse((string) r0["lat"], inv), lon = float.Parse((string) r0["lon"], inv) };
                            if (found.Exists(o => Mathf.Abs(o.lat - c.lat) < 0.002f && Mathf.Abs(o.lon - c.lon) < 0.002f)) continue;
                            var parts = new List<string>();
                            string nm = (string) r0["name"]; if (!string.IsNullOrEmpty(nm)) parts.Add(nm);
                            foreach (var part in ((string) r0["display_name"] ?? "").Split(','))
                            {
                                string t = part.Trim();
                                if (t.Length == 0 || parts.Contains(t) || char.IsDigit(t[0])) continue;
                                parts.Add(t); if (parts.Count >= 3) break;
                            }
                            c.name = string.Join(", ", parts);
                            JArray ext = null;
                            if (r0["boundingbox"] is JArray bb && bb.Count == 4) // south, north, west, east
                                ext = new JArray(float.Parse((string) bb[2], inv), float.Parse((string) bb[1], inv), float.Parse((string) bb[3], inv), float.Parse((string) bb[0], inv));
                            c.zoom = ZoomForPlace(ext, (string) r0["addresstype"], c.lat);
                            c.km = KmFrom(refLat, refLon, c.lat, c.lon);
                            double imp = r0["importance"] != null && r0["importance"].Type != JTokenType.Null ? (double) r0["importance"] : 0.3;
                            if (imp > topImp) { topImp = imp; famous = c; }
                            found.Add(c);
                        }
                    }
                    catch (Exception e) { Debug.Log("[Find] " + e.Message); }
                }
            }
            if (found.Count > 0)
            {
                found.Sort((x, y) => x.km.CompareTo(y.km));
                var list = found.GetRange(0, Math.Min(7, found.Count));
                // a world-famous place (a big city, a landmark) is what someone typing its name almost always means: it goes first;
                // anything less known keeps the nearest-first order ("Springfield" = the ones near you)
                if (famous != null)
                {
                    list.Remove(famous);
                    if (topImp >= 0.6) list.Insert(0, famous);
                    else { if (list.Count >= 6) list.RemoveAt(list.Count - 1); list.Add(famous); list.Sort((x, y) => x.km.CompareTo(y.km)); }
                }
                if (list.Count == 1) { lat = list[0].lat; lon = list[0].lon; name = list[0].name; zoom = list[0].zoom; }
                else
                {
                    choices = list; message = "Pick the one you mean"; pendingCell = "Cell " + ChoiceLabel(0);
                    Render();
                    yield break;
                }
            }
            if (zoom < 0)
            using (var rq = UnityWebRequest.Get("https://photon.komoot.io/api/?limit=1&lang=en&lat=" + refLat.ToString("0.###", inv) + "&lon=" + refLon.ToString("0.###", inv) + "&q=" + UnityWebRequest.EscapeURL(q)))
            {
                rq.timeout = 12; rq.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
                yield return rq.SendWebRequest();
                try
                {
                    if (rq.result == UnityWebRequest.Result.Success)
                    {
                        var ft = JObject.Parse(rq.downloadHandler.text)["features"]?[0];
                        if (ft != null)
                        {
                            lon = (float) ft["geometry"]["coordinates"][0]; lat = (float) ft["geometry"]["coordinates"][1];
                            var pr = ft["properties"];
                            var parts = new List<string>();
                            string Add(string k) { var v = (string) pr[k]; if (!string.IsNullOrEmpty(v) && !parts.Contains(v)) parts.Add(v); return v; }
                            string hn = (string) pr["housenumber"], st = (string) pr["street"];
                            Add("name"); if (st != null && hn != null && parts.Count == 0) parts.Add(hn + " " + st); else if (st != null && (string) pr["name"] == null) Add("street");
                            Add("city"); Add("state"); Add("country");
                            name = string.Join(", ", parts);
                            zoom = ZoomForPlace(pr["extent"] as JArray, (string) pr["type"], lat);
                        }
                    }
                    else Debug.Log("[Find] " + rq.error);
                }
                catch (Exception e) { Debug.Log("[Find] " + e.Message); }
            }
            if (zoom < 0 && LoadTowns())
            {
                int best = FindPlace(q.Split(',')[0].Trim(), refLat, refLon); // offline: the built-in list of towns
                if (best >= 0) { lat = gLat[best]; lon = gLon[best]; name = gName[best]; zoom = Mathf.Max(globeZoom, 11); }
            }
            if (zoom < 0) { message = "Couldn't find \"" + q + "\". Try adding the town or country"; Render(); yield break; }
            if (string.IsNullOrEmpty(name)) name = q;
            message = name; pinName = name.Split(',')[0]; pinLat = lat; pinLon = lon;
            pendingCell = "Cell Find a place";
            nearList = null;
            yield return GlideTo(lat, lon, zoom);
        }

        private static double KmFrom(double la1, double lo1, double la2, double lo2)
        {
            double r = Math.PI / 180.0, dla = (la2 - la1) * r, dlo = (lo2 - lo1) * r;
            double a = Math.Sin(dla / 2) * Math.Sin(dla / 2) + Math.Cos(la1 * r) * Math.Cos(la2 * r) * Math.Sin(dlo / 2) * Math.Sin(dlo / 2);
            return 12742.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        // the closest zoom that still shows the whole place (its outline box), or a fixed one for points
        private int ZoomForPlace(JArray ext, string type, float lat)
        {
            float cos = Mathf.Max(0.2f, Mathf.Cos(lat * Mathf.Deg2Rad));
            if (ext != null && ext.Count == 4)
            {
                float w = Mathf.Abs((float) ext[2] - (float) ext[0]) * cos, hgt = Mathf.Abs((float) ext[1] - (float) ext[3]);
                int best = 0;
                for (int i = 0; i < GlobeZoom.Length; i++)
                {
                    float seeW = 255f / GlobeZoom[i], seeH = 114f / GlobeZoom[i]; // degrees the screen spans at that zoom
                    if (seeW >= w * 1.3f && seeH >= hgt * 1.3f) best = i;
                }
                return Mathf.Clamp(best, 1, GlobeZoom.Length - 2);
            }
            switch (type)
            {
                case "country": return 3;
                case "state": return 5;
                case "city": return 10;
                case "town": return 11;
                case "district": case "locality": case "village": case "suburb": case "neighbourhood": return 12;
                case "street": case "road": return 14;
                default: return 16; // a house, shop or landmark: close enough to see it
            }
        }
        // exact name first, then names starting with it, then names containing it; the list is sorted by population, so the
        // first hit is the biggest place of that name
        private static int FindPlace(string q, double refLat, double refLon)
        {
            int starts = -1, contains = -1, exact = -1; double exactScore = double.MinValue;
            for (int i = 0; i < gName.Length; i++)
            {
                string n = gName[i];
                if (string.Equals(n, q, StringComparison.OrdinalIgnoreCase))
                {
                    // same name in several places: bigger and nearer wins
                    double sc = Math.Log10(Math.Max(1, gPop[i])) * 0.25 - 0.15 * Math.Log10(1.0 + KmFrom(refLat, refLon, gLat[i], gLon[i]) / 10.0);
                    if (sc > exactScore) { exactScore = sc; exact = i; }
                    continue;
                }
                if (starts < 0 && n.StartsWith(q, StringComparison.OrdinalIgnoreCase)) starts = i;
                else if (contains < 0 && n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) contains = i;
            }
            return exact >= 0 ? exact : starts >= 0 ? starts : contains;
        }
        private void GlobeHold()
        {
            if (!globe) return;
            string f = TVNav.FocusName;
            PollFind();
            // holding OK on a zoom button keeps zooming, one glided step after the other
            int zdir = f == "Cell Zoom in  +" ? 1 : f == "Cell Zoom out  –" ? -1 : 0;
            if (zdir != 0)
            {
                if (RInput.GetMouseButton(0))
                {
                    zoomHeldFor += Time.unscaledDeltaTime;
                    if (!zoomHolding && zoomHeldFor > 0.3f && !spinning) { zoomHolding = true; spinning = true; townCity = -1; zoomAnim = Zoom; HideLabels(); }
                    if (zoomHolding)
                    {
                        float lz = Mathf.Log(zoomAnim) + zdir * 1.6f * Time.unscaledDeltaTime; // about one level every 0.4 s
                        zoomAnim = Mathf.Exp(Mathf.Clamp(lz, Mathf.Log(GlobeZoom[0]), Mathf.Log(GlobeZoom[GlobeZoom.Length - 1])));
                        if (!SatCovers()) RenderSphere(true);
                        PlaceSat();
                        satPoll -= Time.unscaledDeltaTime;
                        if (satPoll <= 0f) { satPoll = 1f; if (Zoom >= GlobeZoom[SatZoom - 1] && !SatCovers()) StartCoroutine(SatRoutine()); }
                    }
                }
                else
                {
                    if (zoomHolding)
                    {
                        heldEndedAt = Time.unscaledTime; zoomHolding = false; spinning = false;
                        int to = 0; // the next level in the direction it was going
                        for (int i = 0; i < GlobeZoom.Length; i++) if (GlobeZoom[i] <= zoomAnim * 1.0001f) to = i;
                        if (zdir > 0 && GlobeZoom[to] < zoomAnim * 0.999f) to = Mathf.Min(to + 1, GlobeZoom.Length - 1);
                        StartCoroutine(ZoomAnim(to));
                    }
                    zoomHeldFor = 0f;
                }
                return;
            }
            if (zoomHolding) { zoomHolding = false; spinning = false; heldEndedAt = Time.unscaledTime; int to = 0; for (int i = 0; i < GlobeZoom.Length; i++) if (GlobeZoom[i] <= zoomAnim * 1.0001f) to = i; StartCoroutine(ZoomAnim(to)); }
            zoomHeldFor = 0f;
            Vector2 dir = f == "Cell ◄ Left" ? new Vector2(-1f, 0f) : f == "Cell Right ►" ? new Vector2(1f, 0f) : f == "Cell ▲ Up" ? new Vector2(0f, 1f) : f == "Cell ▼ Down" ? new Vector2(0f, -1f) : Vector2.zero;
            bool held = dir != Vector2.zero && RInput.GetMouseButton(0);
            if (!held)
            {
                holdFor = 0f;
                if (holdTurning) { holdTurning = false; heldEndedAt = Time.unscaledTime; spinning = false; nearList = null; Render(); FetchGlobe(); }
                return;
            }
            holdFor += Time.unscaledDeltaTime;
            if (holdFor < 0.3f || (spinning && !holdTurning)) return; // a short press is a normal one-step turn
            if (!holdTurning)
            {
                holdTurning = true; spinning = true; townCity = -1;
                holdLat0 = globeLat; holdLon0 = globeLon;
                holdSlideLabels = globeZoom >= TownZoom; // close up the map is flat enough to just slide the names along
                if (!holdSlideLabels) HideLabels();
            }
            float st = Mathf.Clamp(1.2f / Zoom, 0.0002f, 1f), ramp = Mathf.Clamp01((holdFor - 0.3f) / 1.2f);
            float speed = Mathf.Lerp(30f, 90f, ramp) * st; // degrees a second at full zoom-out
            // close up: about half a screen a second, building to just over one (the photo is fetched ahead and keeps up)
            speed = Mathf.Min(speed, Mathf.Lerp(0.45f, 1.1f, ramp) * 255f / Zoom);
            globeLon += dir.x * speed * Time.unscaledDeltaTime;
            globeLat = LatStep(globeLat + dir.y * speed * 0.7f * Time.unscaledDeltaTime);
            if (!SatCovers()) RenderSphere(true);
            PlaceSat();
            if (holdSlideLabels && body != null)
            {
                var off = Project(holdLat0, holdLon0, h * 0.4f * Zoom);
                foreach (var n in new[] { "Space/Cities", "Space/Regions" }) { var cr = body.Find(n) as RectTransform; if (cr != null) cr.anchoredPosition = off; }
            }
            // more photo is fetched ahead of the view while it moves (the old one stays up meanwhile)
            satPoll -= Time.unscaledDeltaTime;
            if (satPoll <= 0f) { satPoll = 0.6f; if (globeZoom >= SatZoom - 1 && !SatCovers()) StartCoroutine(SatRoutine()); }
        }

        private void Turn(float dLon, float dLat) { if (spinning) return; townCity = -1; nearList = null; float st = Mathf.Clamp(1.2f / Zoom, 0.0005f, 1f); StartCoroutine(Spin(dLon * st, dLat * st)); } // smaller steps when zoomed in

        private static Texture2D worldMap;
        private static Texture2D World() { if (worldMap == null) worldMap = Resources.Load<Texture2D>("WeatherMap"); return worldMap; }

        // view window on the world map (degrees) around the viewer's place; width in degrees by zoom step
        private static readonly float[] ZoomWidth = { 160f, 90f, 50f, 28f };
        private void MapWindow(out float lon0, out float lat0, out float wDeg, out float hDeg, float aspect)
        {
            Place(out double clat, out double clon);
            wDeg = ZoomWidth[Mathf.Clamp(globeZoom, 0, 3)];
            hDeg = wDeg * aspect;
            lon0 = (float) clon + globePanX * wDeg * 0.25f; lat0 = (float) clat + globePanY * hDeg * 0.25f;
            lat0 = Mathf.Clamp(lat0, -85f + hDeg * 0.5f, 85f - hDeg * 0.5f);
        }
        private int globePanX, globePanY;

        // sets the page's background to the map around the viewer, tinted blue like the Wii's weather pages
        private void MapBackground(Color tint)
        {
            var tex = World();
            if (tex == null) return;
            MapWindow(out float lon0, out float lat0, out float w, out float hh, 9f / 16f);
            bg.texture = tex;
            bg.uvRect = new Rect((lon0 - w * 0.5f + 180f) / 360f, (lat0 - hh * 0.5f + 90f) / 180f, w / 360f, hh / 180f);
            Box(body, "Tint", 0f, 0f, 1f, 1f, tint);
        }

        // names of countries and US states, drawn faintly under the towns so the borders mean something
        private static readonly string[] RegN = { "CANADA", "UNITED STATES", "MEXICO", "BRAZIL", "ARGENTINA", "PERU", "COLOMBIA", "CHILE", "VENEZUELA", "BOLIVIA", "CUBA", "UNITED KINGDOM", "FRANCE", "SPAIN", "GERMANY", "ITALY", "POLAND", "UKRAINE", "TURKEY", "RUSSIA", "KAZAKHSTAN", "EGYPT", "ALGERIA", "LIBYA", "NIGERIA", "ETHIOPIA", "SOUTH AFRICA", "SAUDI ARABIA", "IRAN", "INDIA", "CHINA", "MONGOLIA", "JAPAN", "THAILAND", "INDONESIA", "AUSTRALIA", "NEW ZEALAND", "GREENLAND", "SWEDEN", "NORWAY", "FINLAND", "PAKISTAN", "SUDAN", "TANZANIA", "DR CONGO", "ANGOLA", "MALI", "NIGER", "CHAD", "MOROCCO", "IRAQ", "AFGHANISTAN", "MYANMAR", "VIETNAM", "PHILIPPINES", "SOUTH KOREA", "ICELAND", "IRELAND", "PORTUGAL", "GREECE", "ROMANIA", "KENYA", "MADAGASCAR", "PAPUA NEW GUINEA", "ECUADOR", "PARAGUAY", "URUGUAY", "GUATEMALA" };
        private static readonly double[] RegLat = { 58, 39, 24, -10, -35, -9, 4, -30, 7, -17, 21.7, 54, 46.5, 40, 51, 42.8, 52, 49, 39, 61, 48, 26.5, 28, 27, 9.5, 8.5, -29, 24, 32.5, 22, 35, 46.8, 36.5, 15, -2.5, -25, -42, 73, 62, 64.5, 64, 30, 15.5, -6.3, -2.9, -12, 17, 17, 15, 31.8, 33, 33.8, 21, 14.5, 12.5, 36.5, 64.8, 53.2, 39.6, 39, 45.8, 0.5, -19, -6.5, -1.4, -23, -32.8, 15.5 };
        private static readonly double[] RegLon = { -100, -98, -102, -53, -65, -75, -73, -71, -66, -64.5, -79.5, -2.5, 2.5, -3.7, 10, 12.5, 19.5, 31.5, 35, 95, 67, 29.5, 2.5, 17.5, 8, 39.5, 24.5, 45, 54, 79, 103, 103, 138, 101, 115, 134, 172, -41, 15, 9, 26, 69.5, 30, 34.8, 23.5, 17.5, -3.5, 9.5, 18.5, -6.5, 43.5, 66, 96.5, 106, 122, 128, -18.5, -8, -8, 22, 25, 38, 46.8, 144, -78, -58, -56, -90.3 };
        private static readonly string[] StN = { "ALABAMA", "ARIZONA", "ARKANSAS", "CALIFORNIA", "COLORADO", "FLORIDA", "GEORGIA", "IDAHO", "ILLINOIS", "INDIANA", "IOWA", "KANSAS", "KENTUCKY", "LOUISIANA", "MICHIGAN", "MINNESOTA", "MISSISSIPPI", "MISSOURI", "MONTANA", "NEBRASKA", "NEVADA", "NEW MEXICO", "NEW YORK", "N. CAROLINA", "N. DAKOTA", "OHIO", "OKLAHOMA", "OREGON", "PENNSYLVANIA", "S. CAROLINA", "S. DAKOTA", "TENNESSEE", "TEXAS", "UTAH", "VIRGINIA", "WASHINGTON", "WISCONSIN", "WYOMING", "ONTARIO", "QUEBEC", "ALBERTA", "BRITISH COLUMBIA", "SONORA", "CHIHUAHUA", "BAJA CALIFORNIA", "ALASKA", "HAWAII", "MAINE", "VERMONT", "NEW HAMPSHIRE", "MASSACHUSETTS", "CONNECTICUT", "RHODE ISLAND", "NEW JERSEY", "DELAWARE", "MARYLAND", "WEST VIRGINIA", "BEIJING", "TIANJIN", "HEBEI", "SHANXI", "INNER MONGOLIA", "LIAONING", "JILIN", "HEILONGJIANG", "SHANGHAI", "JIANGSU", "ZHEJIANG", "ANHUI", "FUJIAN", "JIANGXI", "SHANDONG", "HENAN", "HUBEI", "HUNAN", "GUANGDONG", "GUANGXI", "HAINAN", "CHONGQING", "SICHUAN", "GUIZHOU", "YUNNAN", "TIBET", "SHAANXI", "GANSU", "QINGHAI", "NINGXIA", "XINJIANG", "RAJASTHAN", "GUJARAT", "MAHARASHTRA", "MADHYA PRADESH", "UTTAR PRADESH", "BIHAR", "WEST BENGAL", "ODISHA", "KARNATAKA", "TAMIL NADU", "ANDHRA PRADESH", "TELANGANA", "KERALA", "PUNJAB", "HARYANA", "ASSAM", "CHHATTISGARH", "JHARKHAND", "KASHMIR", "AMAZONAS", "PARA", "MATO GROSSO", "BAHIA", "MINAS GERAIS", "SAO PAULO", "PARANA", "RIO GRANDE DO SUL", "GOIAS", "MARANHAO", "CEARA", "PERNAMBUCO", "W. AUSTRALIA", "N. TERRITORY", "QUEENSLAND", "S. AUSTRALIA", "NEW SOUTH WALES", "VICTORIA", "TASMANIA", "MANITOBA", "SASKATCHEWAN", "NOVA SCOTIA", "NEW BRUNSWICK", "NEWFOUNDLAND", "YUKON", "NWT", "NUNAVUT", "JALISCO", "VERACRUZ", "OAXACA", "CHIAPAS", "YUCATAN", "COAHUILA", "NUEVO LEON", "TAMAULIPAS", "DURANGO", "SINALOA", "MICHOACAN", "GUERRERO", "PUEBLA", "ZACATECAS", "SAN LUIS POTOSI", "KRASNOYARSK", "SAKHA", "IRKUTSK", "KHABAROVSK", "PRIMORSKY", "CHUKOTKA", "TYUMEN", "ARKHANGELSK", "MURMANSK", "KRASNODAR", "TATARSTAN", "NOVOSIBIRSK", "CHELYABINSK" };
        private static readonly double[] StLat = { 32.8, 34.3, 34.8, 37.3, 39, 28.5, 32.6, 44.2, 40, 40, 42, 38.5, 37.5, 31, 44, 46, 32.7, 38.4, 47, 41.5, 39.3, 34.4, 42.8, 35.5, 47.5, 40.3, 35.5, 44, 41, 33.9, 44.4, 35.8, 31.3, 39.4, 37.5, 47.4, 44.5, 43, 50.5, 52, 55, 55, 29.5, 28.8, 30.5, 64.2, 20.8, 45.3, 44.0, 43.7, 42.3, 41.6, 41.7, 40.1, 39.0, 39.0, 38.6, 40.2, 39.3, 38.5, 37.5, 44.5, 41.3, 43.7, 47.5, 31.2, 33, 29.2, 32, 26, 27.6, 36.3, 34, 31, 27.5, 23.4, 23.5, 19.2, 29.8, 30.6, 26.8, 25, 31.5, 34.3, 36, 35.5, 37.3, 41.5, 26.6, 22.7, 19.5, 23.5, 27, 25.8, 23.2, 20.5, 14.8, 11, 15.8, 17.9, 10.5, 31, 29.3, 26.3, 21.3, 23.6, 34, -4, -4, -13, -12.5, -18.5, -22.2, -24.6, -29.7, -15.9, -5, -5.2, -8.4, -26, -19.5, -22.5, -30, -32, -36.8, -42, 55, 54.5, 45, 46.5, 53.5, 64, 64, 66, 20.6, 19.4, 17, 16.5, 20.7, 27.3, 25.6, 24.3, 24.8, 25, 19.3, 17.6, 19, 23.3, 22.6, 64, 66, 56, 52, 45, 66, 58, 63, 68, 45, 55.5, 55, 54.5 };
        private static readonly double[] StLon = { -86.8, -111.7, -92.4, -119.6, -105.5, -82.4, -83.4, -114.6, -89.2, -86.2, -93.5, -98.4, -85, -92, -85, -94.5, -89.7, -92.5, -109.6, -99.8, -116.8, -106, -75.5, -79.5, -100.5, -82.8, -97.5, -120.6, -77.6, -80.9, -100.2, -86.3, -99.3, -111.6, -78.6, -120.5, -90, -107.5, -86, -72, -115, -125, -110.5, -106.5, -115.5, -152.5, -156.3, -69.2, -72.7, -71.6, -72.1, -72.7, -71.5, -74.5, -75.5, -76.8, -80.6, 116.4, 117.3, 115.5, 112.3, 115, 122.5, 126.2, 128, 121.4, 119.8, 120.1, 117.3, 118, 115.7, 118.1, 113.5, 112.3, 111.7, 113.4, 108.3, 109.7, 107.9, 102.7, 106.8, 101.5, 88, 108.9, 103.8, 96, 106, 85, 73.8, 71.6, 76, 78.5, 80.8, 85.6, 87.8, 84.5, 75.7, 78.4, 79.7, 79, 76.5, 75.3, 76.2, 92.8, 82, 85.3, 76.5, -64, -52.5, -56, -41.7, -44.6, -48.7, -51.5, -53.5, -49.8, -45, -39.5, -37.9, 121, 133.4, 144.5, 135.5, 146, 144.3, 146.6, -98, -106, -63, -66, -60, -135, -119, -86, -103.6, -96.7, -96.5, -92.5, -89, -102, -99.9, -98.6, -104.9, -107.5, -101.9, -100, -97.9, -102.7, -100.4, 95, 129, 104, 135, 134, 172, 70, 43, 35, 39, 50.6, 80, 60 };

        private void DrawRegions(RectTransform root, float rs, float z, List<Vector3> towns)
        {
            var shown = new List<Vector4>(); // x, y = centre; z, w = half width and half height
            for (int pass = 0; pass < 2; pass++)
            {
                // countries first (zoomed out), then states / provinces with today's temperature, down to the zoom where towns take over
                if (pass == 0 && (globeZoom < 1 || globeZoom > 6)) continue;
                if (pass == 1 && (globeZoom < 3 || globeZoom > 10)) continue;
                var nm = pass == 0 ? RegN : StN; var la = pass == 0 ? RegLat : StLat; var lo = pass == 0 ? RegLon : StLon;
                float lh = h * (pass == 0 ? 0.034f : globeZoom >= 8 ? 0.028f : 0.032f);
                bool cel = PlayerPrefs.GetInt(UnitKey, 0) == 1;
                for (int i = 0; i < nm.Length; i++)
                {
                    float lat = (float) la[i] * Mathf.Deg2Rad, dl = ((float) lo[i] - globeLon) * Mathf.Deg2Rad, ga = globeLat * Mathf.Deg2Rad;
                    float x = Mathf.Cos(lat) * Mathf.Sin(dl), y0 = Mathf.Sin(lat), z0 = Mathf.Cos(lat) * Mathf.Cos(dl);
                    float ny = y0 * Mathf.Cos(ga) - z0 * Mathf.Sin(ga), nz = y0 * Mathf.Sin(ga) + z0 * Mathf.Cos(ga);
                    if (nz < 0.35f || Mathf.Abs(x) * z > 0.9f * SkyAspect || Mathf.Abs(ny) * z > 0.82f) continue;
                    var pos = new Vector2(x * rs, ny * rs);
                    bool stateTemp = pass == 1;
                    string txt = nm[i];
                    if (stateTemp) txt = StAbbr[i]; // like a map: a small plain abbreviation (the temperatures are on the towns)
                    float hw = lh * 0.34f * (stateTemp ? Mathf.Max(StAbbr[i].Length, 3) : nm[i].Length), hh = lh * (stateTemp ? 0.6f : 0.8f);
                    bool crowded = false;
                    foreach (var t in towns) if (Mathf.Abs(t.y * rs - pos.x) < hw + h * 0.12f && Mathf.Abs(t.z * rs - pos.y) < hh + h * 0.05f) { crowded = true; break; }
                    if (!crowded) foreach (var o in shown) if (Mathf.Abs(o.x - pos.x) < hw + o.z && Mathf.Abs(o.y - pos.y) < hh + o.w) { crowded = true; break; }
                    if (crowded || shown.Count > 90) continue;
                    shown.Add(new Vector4(pos.x, pos.y, hw, hh));
                    var lab = TVServerScreen.Text(root, txt, font, lh * (stateTemp ? 0.72f : 0.9f), new Color(1f, 1f, 1f, pass == 0 ? 0.95f : 0.7f));
                    lab.outlineWidth = stateTemp ? 0.12f : 0.18f; lab.outlineColor = new Color32(0, 0, 0, 200);
                    lab.enableWordWrapping = false; lab.fontStyle = pass == 0 ? FontStyles.Bold | FontStyles.UpperCase : FontStyles.Normal; lab.alignment = TextAlignmentOptions.Center;
                    if (pass == 0) lab.characterSpacing = 6f;
                    var lr = lab.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f); lr.anchoredPosition = pos; lr.sizeDelta = new Vector2(h * 0.6f, lh * (stateTemp ? 3f : 1.5f));
                }
            }
        }

        private void DrawGlobe(Color white)
        {
            var space = Box(body, "Space", 0f, 0.1f, 1f, 0.9f, new Color(0.01f, 0.02f, 0.09f));
            for (int i = 0; i < 70; i++)
            {
                var st = TVServerScreen.Child(space, "Star");
                st.anchorMin = st.anchorMax = new Vector2(((i * 53) % 97) / 97f, ((i * 29 + 11) % 89) / 89f);
                st.sizeDelta = Vector2.one * h * (0.003f + 0.003f * (i % 3));
                var si = st.gameObject.AddComponent<Image>();
                si.sprite = TVServerScreen.DotSprite(); si.raycastTarget = false;
                si.color = new Color(0.85f, 0.92f, 1f, 0.35f + 0.1f * (i % 5));
            }
            float z = Zoom;
            float d = h * 0.8f, rs = d * 0.5f * z;
            if (z < 1.3f)
            {
                var halo = TVServerScreen.Child(space, "Halo");
                halo.anchorMin = halo.anchorMax = new Vector2(0.5f, 0.5f);
                halo.sizeDelta = Vector2.one * d * z * 1.12f;
                var hi = halo.gameObject.AddComponent<Image>();
                hi.sprite = TVServerScreen.DotSprite(); hi.raycastTarget = false; hi.color = new Color(0.25f, 0.55f, 1f, 0.3f);
            }
            if (globeTex == null || (globeDirty && !SatCovers())) RenderSphere(); // under a full-screen satellite photo the sphere is not seen: not redrawn
            if (globeTex != null)
            {
                var ri = new GameObject("Sphere", typeof(RectTransform)).AddComponent<RawImage>();
                ri.transform.SetParent(space, false);
                ri.texture = globeTex; ri.raycastTarget = false;
                TVServerScreen.Stretch(ri.rectTransform);
            }
            if (globeZoom >= SatZoom - 1 && SatUsable())
            {
                PlaceSat(space);
                Label(space, "Imagery & labels: Esri, Maxar, Earthstar Geographics, HERE, Garmin, OpenStreetMap  ·  Search: Nominatim, Photon (OpenStreetMap)", 0.018f, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Left, 0.01f, 0.1f, 0.75f, 0.14f);
            }
            var regRoot = TVServerScreen.Child(space, "Regions");
            regRoot.anchorMin = regRoot.anchorMax = new Vector2(0.5f, 0.5f); regRoot.sizeDelta = Vector2.zero;
            var towns = VisibleCities();
            DrawRegions(regRoot, rs, z, towns);
            var cityRoot = TVServerScreen.Child(space, "Cities");
            cityRoot.anchorMin = cityRoot.anchorMax = new Vector2(0.5f, 0.5f); cityRoot.sizeDelta = Vector2.zero;
            bool c = PlayerPrefs.GetInt(UnitKey, 0) == 1;
            foreach (var v in towns)
            {
                int i = (int) v.x;
                if (!cityWx.TryGetValue(i, out var cur)) continue;
                var pos = new Vector2(v.y * rs, v.z * rs);
                var icon = new GameObject("City", typeof(RectTransform)).AddComponent<RawImage>();
                icon.transform.SetParent(cityRoot, false);
                icon.texture = Icon((int) cur["weather_code"], (int) cur["is_day"] == 1); icon.raycastTarget = false;
                bool deep = globeZoom >= TownZoom; // close up: smaller markers so towns sit side by side like a regional map
                var rt = icon.rectTransform; rt.anchoredPosition = pos + new Vector2(0f, h * 0.02f); rt.sizeDelta = Vector2.one * h * (deep ? 0.05f : 0.09f);
                var lab = TVServerScreen.Text(cityRoot, TNm(i) + "  " + Deg((float) cur["temperature_2m"], c), font, h * (deep ? 0.027f : 0.03f), white);
                lab.enableWordWrapping = false; lab.fontStyle = FontStyles.Bold;
                lab.outlineWidth = 0.2f; lab.outlineColor = new Color32(5, 25, 70, 255);
                var lr = lab.rectTransform; lr.pivot = new Vector2(0.5f, 1f);
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                lr.anchoredPosition = pos + new Vector2(0f, globeZoom >= TownZoom ? -h * 0.018f : -h * 0.035f); lr.sizeDelta = new Vector2(h * 0.5f, h * 0.045f);
            }
            Vector2 homeAt = new Vector2(float.NaN, 0f);
            if (pinName != "My place" && CityIndex == 0 && Home(out _, out double hla0, out double hlo0))
            {
                // your house: a small marker on the globe at every zoom (the big pin is for a place you just found)
                float la = (float) hla0 * Mathf.Deg2Rad, dl = ((float) hlo0 - globeLon) * Mathf.Deg2Rad, ga = globeLat * Mathf.Deg2Rad;
                float facing = Mathf.Sin(la) * Mathf.Sin(ga) + Mathf.Cos(la) * Mathf.Cos(dl) * Mathf.Cos(ga);
                var hp = Project((float) hla0, (float) hlo0, rs);
                if (facing > 0.05f && Mathf.Abs(hp.x) < h * SkyAspect * 0.5f && Mathf.Abs(hp.y) < h * 0.42f)
                {
                    var ring = new GameObject("HomeRing", typeof(RectTransform)).AddComponent<Image>();
                    ring.transform.SetParent(cityRoot, false); ring.sprite = TVServerScreen.DotSprite(); ring.raycastTarget = false; ring.color = Color.white;
                    ring.rectTransform.anchoredPosition = hp; ring.rectTransform.sizeDelta = Vector2.one * h * 0.022f;
                    var dot = new GameObject("Home", typeof(RectTransform)).AddComponent<Image>();
                    dot.transform.SetParent(cityRoot, false); dot.sprite = TVServerScreen.DotSprite(); dot.raycastTarget = false; dot.color = new Color(0.1f, 0.5f, 1f);
                    dot.rectTransform.anchoredPosition = hp; dot.rectTransform.sizeDelta = Vector2.one * h * 0.015f;
                    homeAt = hp;
                }
            }
            // a place you looked up: only while the globe is close in (zoomed out, its pin sat on top of everything and seemed to follow
            // the view), only on the side facing you, and never on top of My place
            bool pinNearHome = false;
            if (pinName != null && pinName != "My place" && !float.IsNaN(homeAt.x)) pinNearHome = (Project(pinLat, pinLon, rs) - homeAt).magnitude < h * 0.03f;
            if (pinName != null && (globeZoom >= TownZoom - 1 || pinName == "My place") && !pinNearHome)
            {
                var pp = Project(pinLat, pinLon, rs);
                float pla = pinLat * Mathf.Deg2Rad, pdl = (pinLon - globeLon) * Mathf.Deg2Rad, pga = globeLat * Mathf.Deg2Rad;
                bool pinFacing = Mathf.Sin(pla) * Mathf.Sin(pga) + Mathf.Cos(pla) * Mathf.Cos(pdl) * Mathf.Cos(pga) > 0.05f;
                bool labelBelow = !float.IsNaN(homeAt.x) && (pp - homeAt).magnitude < h * 0.12f && pp.y < homeAt.y; // keep the name off My place's dot
                if (pinFacing && Mathf.Abs(pp.x) < h * SkyAspect * 0.5f && Mathf.Abs(pp.y) < h * 0.42f)
                {
                    var pin = new GameObject("Pin", typeof(RectTransform)).AddComponent<Image>();
                    pin.transform.SetParent(cityRoot, false); pin.sprite = TVServerScreen.DotSprite(); pin.raycastTarget = false; pin.color = new Color(0.95f, 0.15f, 0.15f, 1f);
                    pin.rectTransform.anchoredPosition = pp; pin.rectTransform.sizeDelta = Vector2.one * h * 0.017f;
                    var ring = new GameObject("PinRing", typeof(RectTransform)).AddComponent<Image>();
                    ring.transform.SetParent(cityRoot, false); ring.sprite = TVServerScreen.DotSprite(); ring.raycastTarget = false; ring.color = new Color(1f, 1f, 1f, 0.9f);
                    ring.rectTransform.anchoredPosition = pp; ring.rectTransform.sizeDelta = Vector2.one * h * 0.025f; ring.transform.SetSiblingIndex(pin.transform.GetSiblingIndex());
                    var pl = TVServerScreen.Text(cityRoot, pinName, font, h * 0.024f, white);
                    pl.enableWordWrapping = false; pl.fontStyle = FontStyles.Bold; pl.outlineWidth = 0.25f; pl.outlineColor = new Color32(90, 0, 0, 255);
                    var plr = pl.rectTransform; plr.anchorMin = plr.anchorMax = new Vector2(0.5f, 0.5f); plr.pivot = new Vector2(0.5f, labelBelow ? 1f : 0f);
                    plr.anchoredPosition = pp + new Vector2(0f, labelBelow ? -h * 0.015f : h * 0.015f); plr.sizeDelta = new Vector2(h * 1.2f, h * 0.045f); pl.alignment = TextAlignmentOptions.Center;
                }
            }
            if (globeZoom >= 8)
            {
                // scale bar, like Apple Maps: a round length that fits about a sixth of the screen
                bool mi = !c; double m = 6371000.0 / rs, unit = mi ? 1609.344 : 1000.0, target = m * h * 0.17 / unit, p10 = Math.Pow(10.0, Math.Floor(Math.Log10(target)));
                double nice = target / p10 >= 5 ? 5 * p10 : target / p10 >= 2 ? 2 * p10 : p10;
                float wUi = body.rect.width > 1f ? body.rect.width : h * 1.78f, frac = (float) (nice * unit / m) / wUi;
                Box(space, "Scale", 0.02f, 0.935f, 0.02f + frac, 0.94f, new Color(1f, 1f, 1f, 0.9f));
                Label(space, nice.ToString("0.###") + (mi ? " mi" : " km"), 0.026f, white, TextAlignmentOptions.Left, 0.02f + frac + 0.008f, 0.915f, 0.3f, 0.96f);
            }
            if (globeZoom >= TownZoom && cityWx.Count == 0) Label(space, "Getting the weather around the world...", 0.04f, white, TextAlignmentOptions.Center, 0f, 0.02f, 1f, 0.12f);
            else if (townCity >= 0 && globeZoom >= TownZoom && cityWx.TryGetValue(townCity, out var tw))
            {
                // the town just visited, said plainly at the top
                Box(space, "TownPill", 0.22f, 0.86f, 0.78f, 0.99f, new Color(0.02f, 0.1f, 0.4f, 0.85f));
                Label(space, TNm(townCity) + "   " + Deg((float) tw["temperature_2m"], c) + "   " + Describe((int) tw["weather_code"]), 0.05f, white, TextAlignmentOptions.Center, 0.22f, 0.86f, 0.78f, 0.99f).fontStyle = FontStyles.Bold;
            }
            else if (globeZoom < 3 || (globeZoom >= TownZoom && globeZoom < 10))
            {
                Box(space, "HintBar", 0.2f, 0.08f, 0.8f, 0.135f, new Color(0.02f, 0.1f, 0.4f, 0.72f));
                Label(space, globeZoom < TownZoom ? "Press  My place  or  Zoom in  to bring the towns up close" : "Zoom in more to see the smaller towns", 0.03f, new Color(1f, 1f, 1f, 0.95f), TextAlignmentOptions.Center, 0.2f, 0.08f, 0.8f, 0.135f);
            }
        }

        private void Change(int which)
        {
            AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
            if (which == 0)
            {
                AskZip(); // a new ZIP code (the globe's "My place" is left alone)
                return;
            }
            string key = which == 1 ? UnitKey : WindKey;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) == 1 ? 0 : 1);
            Render();
        }

        private float iconBase;

        private void Update()
        {
            t += Time.unscaledDeltaTime;
            PollZip();
            if (zipPad != null)
            {
                MovedAt = Time.unscaledTime;
                for (int d = 0; d < 10; d++) if (Input.GetKeyDown(KeyCode.Alpha0 + d) || Input.GetKeyDown(KeyCode.Keypad0 + d)) ZipKey_(d.ToString()); // the remote's number keys work too
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) ZipKey_("Erase");
            }
            GlobeHold();
            if (spinning || zoomHolding || holdTurning || findKb != null) MovedAt = Time.unscaledTime; // TVPerf keeps the full frame rate
            if (iconRt != null) iconRt.anchoredPosition = new Vector2(0f, iconBase + Mathf.Sin(t * 1.3f) * h * 0.012f);
            for (int i = 0; i < clouds.Count; i++)
            {
                var cl = clouds[i];
                if (cl == null) continue;
                var p = cl.anchoredPosition;
                p.x += (0.012f + 0.006f * i) * h * Time.unscaledDeltaTime;
                if (p.x > h * 1.9f) p.x = -h * 0.5f;
                cl.anchoredPosition = p;
            }
            if (falling || snowing)
            {
                float fall = (snowing ? 0.12f : 0.62f) * h * Time.unscaledDeltaTime;
                for (int i = 0; i < drops.Count; i++)
                {
                    var d = drops[i];
                    if (d == null) continue;
                    var p = d.anchoredPosition;
                    p.y -= fall * (0.8f + 0.4f * ((i * 37) % 10) / 10f);
                    if (snowing) p.x += Mathf.Sin(t * 1.5f + i) * h * 0.02f * Time.unscaledDeltaTime;
                    if (p.y < h * 0.1f) p.y = h * 0.8f;
                    d.anchoredPosition = p;
                }
            }
        }
    }

    // ---------------------------------------------------------------- drawn art

    private static readonly Dictionary<bool, Texture2D> skies = new Dictionary<bool, Texture2D>();
    // light at the left, deep blue at the right and the foot, like the Wii's forecast pages
    private static Texture2D Sky(bool day)
    {
        if (skies.TryGetValue(day, out var t) && t != null) return t;
        const int w = 64, hh = 32;
        var tex = new Texture2D(w, hh, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color left = day ? new Color(0.35f, 0.72f, 1f) : new Color(0.12f, 0.2f, 0.45f);
        Color right = day ? new Color(0.02f, 0.14f, 0.62f) : new Color(0.02f, 0.04f, 0.2f);
        for (int x = 0; x < w; x++)
        for (int y = 0; y < hh; y++)
        {
            var c = Color.Lerp(left, right, Mathf.Pow(x / (float) (w - 1), 0.9f));
            c = Color.Lerp(c, c * 0.8f, 1f - y / (float) (hh - 1));
            c.a = 1f;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        skies[day] = tex;
        return tex;
    }

    private static readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();
    private const int S = 256; // sharp at 1080 (128 looked blurry when the big icon was drawn 560 px wide)

    private static Texture2D Icon(int code, bool day)
    {
        int key = code * 2 + (day ? 1 : 0);
        if (icons.TryGetValue(key, out var t) && t != null) return t;
        var px = new Color[S * S];
        bool clear = code <= 1, partly = code == 2;
        bool fog = code == 45 || code == 48;
        bool rain = (code >= 51 && code <= 67) || (code >= 80 && code <= 82);
        bool snow = (code >= 71 && code <= 77) || code == 85 || code == 86;
        bool storm = code >= 95;
        bool wet = rain || snow || storm;
        // painted like a weather photo: lit, puffy clouds (dark rain clouds behind a white one), a glowing sun, streaks of rain
        if (clear) { if (day) Sun(px, 0.5f, 0.5f, 0.2f); else Moon(px, 0.5f, 0.5f, 0.24f); }
        else if (partly)
        {
            if (day) Sun(px, 0.34f, 0.62f, 0.15f); else Moon(px, 0.34f, 0.64f, 0.17f);
            Puffs(px, 0.56f, 0.42f, 0.95f, new Color(1f, 1f, 1f), 3);
        }
        else if (wet)
        {
            var dark = storm ? new Color(0.42f, 0.44f, 0.5f) : new Color(0.6f, 0.62f, 0.68f);
            Puffs(px, 0.4f, 0.6f, 0.95f, dark, 7);
            if (!storm) Puffs(px, 0.62f, 0.54f, 0.72f, new Color(1f, 1f, 1f), 11);
        }
        else if (fog) Puffs(px, 0.5f, 0.56f, 1f, new Color(0.86f, 0.88f, 0.92f), 5);
        else { Puffs(px, 0.38f, 0.56f, 0.75f, new Color(0.82f, 0.84f, 0.9f), 13); Puffs(px, 0.6f, 0.48f, 0.85f, new Color(1f, 1f, 1f), 3); }
        if (rain || storm)
        {
            var rnd = new System.Random(code);
            for (int i = 0; i < 26; i++)
            {
                float rx = 0.24f + (float) rnd.NextDouble() * 0.58f, ry = 0.47f - (float) rnd.NextDouble() * 0.08f, len = 0.1f + (float) rnd.NextDouble() * 0.1f;
                Capsule(px, rx, ry, rx - len * 0.35f, ry - len, 0.0045f, new Color(0.78f, 0.86f, 1f, 0.75f));
            }
        }
        if (snow)
        {
            var rnd = new System.Random(code + 7);
            for (int i = 0; i < 18; i++) Disc(px, 0.24f + (float) rnd.NextDouble() * 0.56f, 0.12f + (float) rnd.NextDouble() * 0.24f, 0.012f + (float) rnd.NextDouble() * 0.01f, new Color(1f, 1f, 1f, 0.95f));
        }
        if (storm) Bolt(px);
        if (fog) for (int i = 0; i < 3; i++) Capsule(px, 0.2f, 0.3f - i * 0.08f, 0.8f, 0.3f - i * 0.08f, 0.018f, new Color(0.92f, 0.94f, 0.97f, 0.85f));
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels(px);
        tex.Apply(); // stays readable: the tile and preview pictures are drawn from it
        icons[key] = tex;
        return tex;
    }

    // a cloud made of round puffs, each lit from the upper left (a ball's shading), with a flatter, greyer bottom
    private static void Puffs(Color[] px, float cx, float cy, float k, Color c, int seed)
    {
        var rnd = new System.Random(seed);
        var list = new List<Vector3>();
        // a row along the bottom, a taller hump on top, and a few random ones for a natural edge
        for (int i = 0; i < 5; i++) list.Add(new Vector3(cx + (-0.24f + i * 0.12f) * k, cy - 0.06f * k, (0.09f + (float) rnd.NextDouble() * 0.03f) * k));
        list.Add(new Vector3(cx - 0.1f * k, cy + 0.05f * k, 0.13f * k));
        list.Add(new Vector3(cx + 0.06f * k, cy + 0.08f * k, 0.15f * k));
        list.Add(new Vector3(cx + 0.18f * k, cy + 0.02f * k, 0.1f * k));
        for (int i = 0; i < 12; i++) list.Add(new Vector3(cx + ((float) rnd.NextDouble() - 0.5f) * 0.46f * k, cy + ((float) rnd.NextDouble() - 0.3f) * 0.12f * k, (0.06f + (float) rnd.NextDouble() * 0.05f) * k));
        list.Sort((p, q) => q.y.CompareTo(p.y)); // the top ones first, the lower ones in front
        var L = new Vector3(-0.45f, 0.6f, 0.66f).normalized;
        float bottom = (cy - 0.13f * k) * S;
        foreach (var p in list)
        {
            float pr = p.z * S, pcx = p.x * S, pcy = p.y * S;
            int x0 = Mathf.Max(0, (int) (pcx - pr - 2)), x1 = Mathf.Min(S - 1, (int) (pcx + pr + 2));
            int y0 = Mathf.Max(0, (int) (pcy - pr - 2)), y1 = Mathf.Min(S - 1, (int) (pcy + pr + 2));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - pcx) / pr, dy = (y + 0.5f - pcy) / pr, d2 = dx * dx + dy * dy;
                if (d2 > 1.1f) continue;
                float a = Mathf.Clamp01((1f - Mathf.Sqrt(d2)) * 5f); // soft edges: puffs melt into each other
                a *= Mathf.Clamp01((y - bottom) / 3f + 1f); // the flat underside
                if (a <= 0f) continue;
                var n = new Vector3(dx, dy, Mathf.Sqrt(Mathf.Max(0f, 1f - d2)));
                float lit = 0.8f + 0.26f * Mathf.Max(0f, Vector3.Dot(n, L)) - 0.08f * (1f - n.z);
                float under = Mathf.Clamp01((y - bottom) / (pr * 1.4f)); // greyer toward the bottom
                var col = new Color(c.r * lit, c.g * lit, c.b * Mathf.Min(1.05f, lit + 0.05f), 1f);
                col = Color.Lerp(col * new Color(0.78f, 0.8f, 0.88f, 1f), col, 0.35f + 0.65f * under);
                col.r = Mathf.Min(1f, col.r); col.g = Mathf.Min(1f, col.g); col.b = Mathf.Min(1f, col.b); col.a = 1f;
                Blend(px, x, y, col, a);
            }
        }
    }

    private static void Blend(Color[] px, int x, int y, Color c, float a)
    {
        if (x < 0 || y < 0 || x >= S || y >= S || a <= 0f) return;
        var d = px[y * S + x];
        float ca = c.a * a;
        float oa = ca + d.a * (1f - ca);
        if (oa <= 0f) return;
        px[y * S + x] = new Color((c.r * ca + d.r * d.a * (1f - ca)) / oa, (c.g * ca + d.g * d.a * (1f - ca)) / oa, (c.b * ca + d.b * d.a * (1f - ca)) / oa, oa);
    }

    private static void Disc(Color[] px, float cx, float cy, float r, Color c)
    {
        float pr = r * S, pcx = cx * S, pcy = cy * S;
        int x0 = Mathf.Max(0, (int) (pcx - pr - 2)), x1 = Mathf.Min(S - 1, (int) (pcx + pr + 2));
        int y0 = Mathf.Max(0, (int) (pcy - pr - 2)), y1 = Mathf.Min(S - 1, (int) (pcy + pr + 2));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            Blend(px, x, y, c, Mathf.Clamp01(pr - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(pcx, pcy)) + 0.5f));
    }

    private static void Capsule(Color[] px, float ax, float ay, float bx, float by, float r, Color c)
    {
        Vector2 a = new Vector2(ax * S, ay * S), b = new Vector2(bx * S, by * S);
        float pr = r * S;
        int xa = Mathf.Max(0, (int) (Mathf.Min(a.x, b.x) - pr - 2)), xb = Mathf.Min(S - 1, (int) (Mathf.Max(a.x, b.x) + pr + 2));
        int ya = Mathf.Max(0, (int) (Mathf.Min(a.y, b.y) - pr - 2)), yb = Mathf.Min(S - 1, (int) (Mathf.Max(a.y, b.y) + pr + 2));
        for (int y = ya; y <= yb; y++)
        for (int x = xa; x <= xb; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            Blend(px, x, y, c, Mathf.Clamp01(pr - Vector2.Distance(p, a + ab * t) + 0.5f));
        }
    }

    private static void Sun(Color[] px, float cx, float cy, float r)
    {
        float pr = r * S, pcx = cx * S, pcy = cy * S, R = pr * 2.3f;
        for (int y = Mathf.Max(0, (int) (pcy - R)); y <= Mathf.Min(S - 1, (int) (pcy + R)); y++)
        for (int x = Mathf.Max(0, (int) (pcx - R)); x <= Mathf.Min(S - 1, (int) (pcx + R)); x++)
        {
            float dx = x + 0.5f - pcx, dy = y + 0.5f - pcy, d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > R) continue;
            if (d > pr)
            {
                // the glow and its rays (16 soft spikes that fade out)
                float ang = Mathf.Atan2(dy, dx), ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 8f)), 6f);
                float f = 1f - (d - pr) / (R - pr);
                float glow = f * f * 0.55f + ray * f * 0.7f;
                Blend(px, x, y, new Color(1f, 0.82f, 0.25f), Mathf.Clamp01(glow));
            }
            float a = Mathf.Clamp01(pr - d + 0.5f);
            if (a > 0f)
            {
                float hl = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(pcx - pr * 0.3f, pcy + pr * 0.35f)) / (pr * 1.5f);
                Blend(px, x, y, Color.Lerp(new Color(1f, 0.98f, 0.7f), new Color(1f, 0.62f, 0.1f), Mathf.Clamp01(hl)), a);
            }
        }
    }

    private static void Moon(Color[] px, float cx, float cy, float r)
    {
        Disc(px, cx, cy, r, new Color(0.96f, 0.95f, 0.82f));
        // a bite out of it
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2((cx + r * 0.55f) * S, (cy + r * 0.35f) * S));
            float cut = Mathf.Clamp01(r * 0.85f * S - d + 0.5f);
            if (cut > 0f) { var p = px[y * S + x]; p.a *= 1f - cut; px[y * S + x] = p; }
        }
    }

    private static void Cloud(Color[] px, float cx, float cy, float k, Color c)
    {
        // the shape first (on its own), then filled with a glossy top-to-bottom shade and a soft grey-blue edge
        var m = new Color[S * S];
        Disc(m, cx - 0.17f * k, cy - 0.02f * k, 0.15f * k, Color.white);
        Disc(m, cx + 0.2f * k, cy - 0.02f * k, 0.13f * k, Color.white);
        Disc(m, cx - 0.02f * k, cy + 0.08f * k, 0.2f * k, Color.white);
        Capsule(m, cx - 0.2f * k, cy - 0.08f * k, cx + 0.22f * k, cy - 0.08f * k, 0.09f * k, Color.white);
        float top = (cy + 0.28f * k) * S, bot = (cy - 0.17f * k) * S;
        var edge = new Color(c.r * 0.55f, c.g * 0.62f, c.b * 0.78f, 1f);
        for (int y = 1; y < S - 1; y++)
        for (int x = 1; x < S - 1; x++)
        {
            float a = m[y * S + x].a; if (a <= 0f) continue;
            float t = Mathf.Clamp01((y - bot) / Mathf.Max(1f, top - bot));
            var col = Color.Lerp(new Color(c.r * 0.72f, c.g * 0.78f, c.b * 0.9f), Color.Lerp(c, Color.white, 0.6f), Mathf.Pow(t, 0.8f));
            // near the outline (a neighbour outside the shape): darker, so it reads like the glossy icons in the old channel
            float out4 = 4f - m[y * S + x - 1].a - m[y * S + x + 1].a - m[(y - 1) * S + x].a - m[(y + 1) * S + x].a;
            float rim = Mathf.Clamp01(out4 * 0.5f);
            for (int d = 2; d <= 3 && rim < 0.5f; d++) if (y - d >= 0 && m[(y - d) * S + x].a < 0.5f) rim = Mathf.Max(rim, 0.5f / d);
            Blend(px, x, y, Color.Lerp(col, edge, rim), a);
        }
    }

    private static void Bolt(Color[] px)
    {
        var y = new Color(1f, 0.85f, 0.15f);
        Capsule(px, 0.54f, 0.38f, 0.44f, 0.2f, 0.03f, y);
        Capsule(px, 0.44f, 0.2f, 0.56f, 0.2f, 0.03f, y);
        Capsule(px, 0.56f, 0.2f, 0.46f, 0.04f, 0.03f, y);
    }

    private static byte[] bannerPng;
    /// <summary>The channel's preview picture: the same sky with the sun and cloud big in the middle.</summary>
    public static byte[] BannerPng()
    {
        if (bannerPng != null) return bannerPng;
        const int w = 1280, hgt = 720, size = 440;
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
        for (int y = 0; y < hgt; y++)
        {
            var row = Color.Lerp(new Color(0.78f, 0.92f, 1f), new Color(0.25f, 0.6f, 0.95f), y / (float) hgt);
            for (int x = 0; x < w; x++) tex.SetPixel(x, y, row);
        }
        var ip = Icon(2, true).GetPixels();
        int ox = (w - size) / 2, oy = (hgt - size) / 2;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x * (S - 1) / (float) (size - 1), v = y * (S - 1) / (float) (size - 1);
            int x0 = (int) u, y0 = (int) v, x1 = Mathf.Min(S - 1, x0 + 1), y1 = Mathf.Min(S - 1, y0 + 1);
            var c = Color.Lerp(Color.Lerp(ip[y0 * S + x0], ip[y0 * S + x1], u - x0), Color.Lerp(ip[y1 * S + x0], ip[y1 * S + x1], u - x0), v - y0);
            if (c.a <= 0.01f) continue;
            tex.SetPixel(ox + x, oy + y, Color.Lerp(tex.GetPixel(ox + x, oy + y), c, c.a));
        }
        tex.Apply();
        bannerPng = tex.EncodeToPNG();
        UnityEngine.Object.Destroy(tex);
        return bannerPng;
    }

    private static byte[] iconPng;
    /// <summary>The channel's tile picture: a bright sky with a sun peeking out behind a cloud.</summary>
    public static byte[] IconPng()
    {
        if (iconPng != null) return iconPng;
        const int w = 470, hgt = 344;
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
        for (int y = 0; y < hgt; y++)
        {
            var row = Color.Lerp(new Color(0.78f, 0.92f, 1f), new Color(0.25f, 0.6f, 0.95f), y / (float) hgt);
            for (int x = 0; x < w; x++) tex.SetPixel(x, y, row);
        }
        var icon = Icon(2, true);
        var ip = icon.GetPixels();
        const int N = 256; // drawn 256 px wide, whatever size the icons are made at
        int off = (hgt - N) / 2;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            var c = ip[Mathf.Min(S - 1, y * S / N) * S + Mathf.Min(S - 1, x * S / N)];
            if (c.a <= 0.01f) continue;
            var d = tex.GetPixel(x + (w - N) / 2, y + off);
            tex.SetPixel(x + (w - N) / 2, y + off, Color.Lerp(d, c, c.a));
        }
        tex.Apply();
        iconPng = tex.EncodeToPNG();
        UnityEngine.Object.Destroy(tex);
        return iconPng;
    }

    // a short, soft four-note chime made from sine waves (original, nothing sampled)
    private static AudioClip Chime()
    {
        const int rate = 22050;
        float[] notes = { 659.25f, 783.99f, 987.77f, 1318.51f };
        int len = (int) (rate * 2.2f);
        var data = new float[len];
        for (int n = 0; n < notes.Length; n++)
        {
            int start = (int) (rate * 0.18f * n);
            for (int i = start; i < len; i++)
            {
                float t = (i - start) / (float) rate;
                float env = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 80f);
                data[i] += (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * notes[n] * t)) * env * 0.16f;
            }
        }
        var clip = AudioClip.Create("WeatherChime", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------------------------------------------------------- music
    // "Local Forecast - Elevator" by Kevin MacLeod (incompetech.com), CC BY 4.0: calm, jazzy and made for weather screens.
    // Plays on its own source after the chime and loops until the channel closes.
    private class WeatherMusic : MonoBehaviour
    {
        private AudioSource src;

        private void Start()
        {
            var clip = Resources.Load<AudioClip>("Music/forecast");
            if (clip == null) return;
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.loop = true;
            src.clip = clip;
            src.volume = Mathf.Clamp01(PREFS.MusicVolume.GetFloat()) * 0.8f;
            src.PlayDelayed(2.4f); // after the chime
        }

        private void OnDestroy() { if (src != null) src.Stop(); }
    }
}
