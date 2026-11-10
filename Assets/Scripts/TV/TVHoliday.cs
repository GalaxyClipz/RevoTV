using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Little seasonal touches on the Home menu, only during each holiday's own week: snow at Christmas, gold stars at New Year,
/// pastel eggs at Easter, bats at Halloween, red-white-blue stars on Veterans Day, autumn leaves at Thanksgiving.
/// Drawn as tiny pixel sprites (point filtered, like the Wii's menu icons), a handful at a time, on their own overlay canvas
/// so the menu itself is never redrawn for them. Only on the Home menu; fades away for Settings, channels, popups and Weather.
/// Settings > App Settings > "Holiday decorations?" turns them off. Test switch: --es holiday christmas|newyear|easter|halloween|veterans|thanksgiving
/// </summary>
public class TVHoliday : MonoBehaviour
{
    public static string Forced;

    private enum Kind { None, Christmas, NewYear, Easter, Halloween, Veterans, Thanksgiving }
    private Kind kind = Kind.None;
    private CanvasGroup group;
    private RectTransform root;
    private readonly List<Bit> bits = new List<Bit>();
    private float checkAt, visible;

    private class Bit { public RectTransform rt; public RawImage img; public Vector2 pos, vel; public float phase, size, spin; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TVMode.IsTV) return;
        var go = new GameObject("TVHoliday");
        DontDestroyOnLoad(go);
        go.AddComponent<TVHoliday>();
    }

    private static Kind Today()
    {
        if (!string.IsNullOrEmpty(Forced))
            foreach (Kind k in Enum.GetValues(typeof(Kind))) if (string.Equals(k.ToString(), Forced, StringComparison.OrdinalIgnoreCase)) return k;
        var d = DateTime.Now.Date;
        int y = d.Year;
        bool In(DateTime a, DateTime b) => d >= a && d <= b;
        if (In(new DateTime(y, 12, 30), new DateTime(y, 12, 31)) || In(new DateTime(y, 1, 1), new DateTime(y, 1, 2))) return Kind.NewYear;
        if (In(new DateTime(y, 12, 18), new DateTime(y, 12, 27))) return Kind.Christmas;
        if (In(new DateTime(y, 10, 25), new DateTime(y, 10, 31))) return Kind.Halloween;
        var nov1 = new DateTime(y, 11, 1);
        var thanks = nov1.AddDays(((int) DayOfWeek.Thursday - (int) nov1.DayOfWeek + 7) % 7 + 21); // 4th Thursday
        if (In(thanks.AddDays(-3), thanks.AddDays(3))) return Kind.Thanksgiving;
        var easter = Easter(y);
        if (In(easter.AddDays(-3), easter.AddDays(1))) return Kind.Easter;
        return Kind.None;
    }

    // Easter Sunday (anonymous Gregorian algorithm)
    private static DateTime Easter(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(y, month, day);
    }

    private void Update()
    {
        if (Time.unscaledTime >= checkAt)
        {
            checkAt = Time.unscaledTime + 0.5f;
            var want = PREFS.TvHolidays.GetBool() ? Today() : Kind.None;
            if (want != kind) Build(want);
            if (kind == Kind.None) return;
            visible = OnHome() ? 1f : 0f;
        }
        me = this;
        if (kind == Kind.None || group == null || capturing) return;
        group.alpha = Mathf.MoveTowards(group.alpha, visible, Time.unscaledDeltaTime * 2.5f);
        if (group.alpha <= 0f) return;
        Animate(Time.unscaledDeltaTime);
    }

    private static bool OnHome()
    {
        var app = AppController._instance;
        if (app == null || app.mainView == null || !app.mainView.activeInHierarchy) return false;
        if (app.warningView != null && app.warningView.activeInHierarchy) return false;
        if (PopupController.IsPopupOpen()) return false;
        var cc = ChannelController._instance; if (cc != null && cc.currentChannel != null) return false;
        var sc = SettingsController._instance; if (sc != null && sc.settingsView != null && sc.settingsView.activeInHierarchy) return false;
        if (GameObject.Find("TV Weather Screen") != null) return false;
        return true;
    }

    // ---------------------------------------------------------------- building

    private void Build(Kind k)
    {
        kind = k;
        foreach (var b in bits) if (b.rt != null) Destroy(b.rt.gameObject);
        bits.Clear();
        if (k == Kind.None) { if (root != null) root.gameObject.SetActive(false); return; }
        if (root == null)
        {
            var go = new GameObject("TV Holiday", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            DontDestroyOnLoad(go);
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = -5; // under popups' own overlay layers
            root = (RectTransform) go.transform; group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false; group.alpha = 0f;
        }
        root.gameObject.SetActive(true);
        var sprites = Sprites(k);
        int count = k == Kind.Halloween ? 6 : k == Kind.Easter ? 8 : 16;
        float unit = Screen.height / 720f;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Bit", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform) go.transform; rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            var img = go.GetComponent<RawImage>(); img.raycastTarget = false; img.texture = sprites[i % sprites.Length];
            var b = new Bit { rt = rt, img = img, phase = UnityEngine.Random.value * 10f, spin = UnityEngine.Random.Range(-40f, 40f) };
            b.size = unit * (k == Kind.Halloween ? UnityEngine.Random.Range(30f, 42f) : UnityEngine.Random.Range(18f, 30f));
            rt.sizeDelta = new Vector2(b.size, b.size);
            Respawn(b, true);
            bits.Add(b);
        }
    }

    private void Respawn(Bit b, bool anywhere)
    {
        float w = Screen.width, h = Screen.height, unit = h / 720f;
        switch (kind)
        {
            case Kind.Halloween: // bats fly across, left to right or back
                bool right = UnityEngine.Random.value < 0.5f;
                b.pos = new Vector2(anywhere ? UnityEngine.Random.value * w : (right ? -60f : w + 60f), h * UnityEngine.Random.Range(0.45f, 0.95f));
                b.vel = new Vector2((right ? 1f : -1f) * unit * UnityEngine.Random.Range(50f, 90f), 0f);
                b.rt.localScale = new Vector3(right ? 1f : -1f, 1f, 1f);
                break;
            case Kind.Easter: // eggs drift gently upward
                b.pos = new Vector2(UnityEngine.Random.value * w, anywhere ? UnityEngine.Random.value * h : -40f);
                b.vel = new Vector2(0f, unit * UnityEngine.Random.Range(14f, 26f));
                break;
            default: // snow, stars and leaves fall
                b.pos = new Vector2(UnityEngine.Random.value * w, anywhere ? UnityEngine.Random.value * h : h + 40f);
                b.vel = new Vector2(unit * UnityEngine.Random.Range(-6f, 6f), -unit * (kind == Kind.Thanksgiving ? UnityEngine.Random.Range(30f, 50f) : UnityEngine.Random.Range(18f, 36f)));
                break;
        }
    }

    private void Animate(float dt)
    {
        float w = Screen.width, h = Screen.height, t = Time.unscaledTime;
        foreach (var b in bits)
        {
            b.pos += b.vel * dt;
            float sway = Mathf.Sin(t * 0.9f + b.phase) * b.size * (kind == Kind.Thanksgiving ? 1.2f : 0.5f);
            var p = new Vector2(b.pos.x + (kind == Kind.Halloween ? 0f : sway), b.pos.y + (kind == Kind.Halloween ? Mathf.Sin(t * 2.2f + b.phase) * b.size * 0.4f : 0f));
            b.rt.anchoredPosition = p;
            if (kind == Kind.Halloween)
            {
                // wing beat: the sprite squashes a little, like two-frame pixel art
                float flap = Mathf.Abs(Mathf.Sin(t * 7f + b.phase)) > 0.5f ? 1f : 0.7f;
                b.rt.localScale = new Vector3(Mathf.Sign(b.vel.x), flap, 1f);
            }
            else if (kind == Kind.Thanksgiving || kind == Kind.NewYear || kind == Kind.Veterans) b.rt.localEulerAngles = new Vector3(0f, 0f, t * b.spin + b.phase * 30f);
            // twinkle for the stars
            if (kind == Kind.NewYear || kind == Kind.Veterans) b.img.color = new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin(t * 3f + b.phase));
            bool gone = b.pos.y < -60f || b.pos.y > h + 60f || b.pos.x < -90f || b.pos.x > w + 90f;
            if (gone) Respawn(b, false);
        }
    }

    // ---------------------------------------------------------------- pixel sprites

    // the little drawings are made 8x bigger and softened (two blur passes, smooth filtering), so they look drawn, not blocky
    private static Texture2D Pixel(string[] rows, Dictionary<char, Color32> pal)
    {
        const int S = 8;
        int hgt = rows.Length, wid = 0; foreach (var r in rows) wid = Math.Max(wid, r.Length);
        int W = wid * S + 4, H = hgt * S + 4;
        var a = new Color[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int cx = (x - 2) / S, cy = (y - 2) / S;
            if (x < 2 || y < 2 || cx >= wid || cy >= hgt) continue;
            char c = cx < rows[cy].Length ? rows[cy][cx] : '.';
            if (pal.TryGetValue(c, out var col)) a[(H - 1 - y) * W + x] = col;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var b = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color sum = Color.clear; float n = 0;
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                    var c = a[yy * W + xx]; sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); n++;
                }
                b[y * W + x] = sum.a > 0.001f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, Mathf.Clamp01(sum.a / n * 1.15f)) : Color.clear;
            }
            a = b;
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(a); tex.Apply(false, true);
        return tex;
    }

    // the page-slide bar picture is taken without the decorations (else a frozen copy of them sat on the bar while the real ones moved)
    private static TVHoliday me;
    public static void HideForCapture(bool hide)
    {
        if (me == null || me.group == null) return;
        if (hide) { me.savedAlpha = me.group.alpha; me.group.alpha = 0f; me.capturing = true; }
        else if (me.capturing) { me.group.alpha = me.savedAlpha; me.capturing = false; }
    }
    private float savedAlpha; private bool capturing;

    private static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);

    private static readonly string[] Flake = {
        "....W.W.....", "..W..W..W...", "...W.W.W....", "W...WWW...W.", ".WW.W.W.WW..", "...WWWWW....",
        ".WW.W.W.WW..", "W...WWW...W.", "...W.W.W....", "..W..W..W...", "....W.W.....", "............" };
    private static readonly string[] Star = {
        ".....Y......", ".....Y......", "....YYY.....", "YYYYYYYYYY..", ".YYYYYYYY...", "..YYYYYY....",
        "..YYYYYY....", ".YYY..YYY...", ".YY....YY...", "YY......YY..", "............", "............" };
    private static readonly string[] Leaf = {
        ".....O......", "....OOO.....", ".O.OOOOO.O..", ".OOOOOOOOO..", "..OOOOOOO...", ".OOOOOOOOO..",
        "..OOOOOOO...", "....OOO.....", ".....B......", ".....B......", "............", "............" };
    private static readonly string[] Bat = {
        "............", "............", "K....K.K....K", "KK...KKK...KK", "KKK.KKKKK.KKK", "KKKKKKKKKKKKK",
        ".KKKKKKKKKKK.", "..KK.KKK.KK..", "......K......", "............", "............", "............" };
    private static readonly string[] Pumpkin = {
        ".....G......", "....GG......", "..OOOOOOO...", ".OOOOOOOOO..", "OOKOOOOKOOO.", "OOKKOOOKKOO.",
        "OOOOOKOOOOO.", "OOKOKOKOKOO.", ".OOKKKKKOO..", "..OOOOOOO...", "............", "............" };
    private static readonly string[] Egg = {
        "....PPP.....", "...PPPPP....", "..PPPPPPP...", "..PPPPPPP...", ".SPSPSPSPS..", ".PSPSPSPSP..",
        ".PPPPPPPPP..", ".PPPPPPPPP..", "..PPPPPPP...", "...PPPPP....", "............", "............" };

    private static Texture2D[] Sprites(Kind k)
    {
        var white = new Dictionary<char, Color32> { { 'W', C(255, 255, 255, 230) } };
        switch (k)
        {
            case Kind.Christmas:
                return new[] { Pixel(Flake, white), Pixel(Flake, new Dictionary<char, Color32> { { 'W', C(210, 236, 255, 220) } }) };
            case Kind.NewYear:
                return new[] { Pixel(Star, new Dictionary<char, Color32> { { 'Y', C(255, 214, 72) } }), Pixel(Star, new Dictionary<char, Color32> { { 'Y', C(230, 230, 240) } }) };
            case Kind.Veterans:
                return new[] { Pixel(Star, new Dictionary<char, Color32> { { 'Y', C(200, 30, 45) } }), Pixel(Star, new Dictionary<char, Color32> { { 'Y', C(250, 250, 250) } }), Pixel(Star, new Dictionary<char, Color32> { { 'Y', C(30, 60, 160) } }) };
            case Kind.Thanksgiving:
                return new[] { Pixel(Leaf, new Dictionary<char, Color32> { { 'O', C(230, 120, 30) }, { 'B', C(110, 60, 20) } }),
                               Pixel(Leaf, new Dictionary<char, Color32> { { 'O', C(200, 50, 30) }, { 'B', C(110, 60, 20) } }),
                               Pixel(Leaf, new Dictionary<char, Color32> { { 'O', C(235, 180, 40) }, { 'B', C(110, 60, 20) } }) };
            case Kind.Halloween:
                return new[] { Pixel(Bat, new Dictionary<char, Color32> { { 'K', C(40, 30, 50, 235) } }),
                               Pixel(Pumpkin, new Dictionary<char, Color32> { { 'O', C(245, 140, 30) }, { 'G', C(60, 140, 50) }, { 'K', C(40, 25, 10) } }) };
            case Kind.Easter:
                return new[] { Pixel(Egg, new Dictionary<char, Color32> { { 'P', C(255, 190, 220) }, { 'S', C(255, 255, 255) } }),
                               Pixel(Egg, new Dictionary<char, Color32> { { 'P', C(180, 225, 255) }, { 'S', C(255, 250, 180) } }),
                               Pixel(Egg, new Dictionary<char, Color32> { { 'P', C(200, 240, 190) }, { 'S', C(255, 210, 240) } }) };
        }
        return new Texture2D[0];
    }
}
