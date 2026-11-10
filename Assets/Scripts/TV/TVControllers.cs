using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controller settings for Disc Channel games (the "Controllers" button on the game list). Lists the TV remote and every game
/// controller that's connected, sets each up in one press (Quick setup) or button by button (press the button you want), and
/// writes the result where the TV's Dolphin reads its controls: the [Android] section of dolphin-emu/Config/Dolphin.ini
/// ("InputA_0 = Device '&lt;descriptor&gt;'-Button 96", sticks as "-Axis 0+"). That Dolphin build (the 32-bit one) reads GameCube
/// controls only: it has no Wii Remote buttons or motion input, which this screen says plainly.
/// </summary>
public static class TVControllers
{
    private const string Act = "com.karuzohikari.revotv.RevoActivity";
    public static string IniPath => "/storage/emulated/0/dolphin-emu/Config/Dolphin.ini";

    // the GameCube controller's inputs, in the order the one-by-one setup asks for them
    private static readonly (string key, string label)[] Inputs =
    {
        ("InputA", "A"), ("InputB", "B"), ("InputX", "X"), ("InputY", "Y"), ("InputZ", "Z"), ("InputStart", "Start"),
        ("InputL", "L trigger"), ("InputR", "R trigger"),
        ("MainUp", "Stick up"), ("MainDown", "Stick down"), ("MainLeft", "Stick left"), ("MainRight", "Stick right"),
        ("CStickUp", "C-stick up"), ("CStickDown", "C-stick down"), ("CStickLeft", "C-stick left"), ("CStickRight", "C-stick right"),
        ("DPadUp", "D-pad up"), ("DPadDown", "D-pad down"), ("DPadLeft", "D-pad left"), ("DPadRight", "D-pad right"),
    };

    public class Device { public string desc, name, kind; public bool motion; public HashSet<int> axes = new HashSet<int>(), keys = new HashSet<int>(); }

    private static GameObject root;
    public static bool Open => root != null;

    public static void Show()
    {
        if (Open) return;
        var app = AppController._instance;
        if (app == null || app.overlayCanvas == null) return;
        root = new GameObject("TV Controllers", typeof(RectTransform));
        var rt = (RectTransform) root.transform;
        rt.SetParent(app.overlayCanvas, false);
        rt.SetAsFirstSibling();
        TVServerScreen.Stretch(rt);
        TVGameScreen.SetHidden(true);
        if (File.Exists(IniPath)) WriteWiimote(); // Wii games answer player 1's buttons too
        root.AddComponent<Runner>().Build(rt, app.overlayCanvas.rect.height > 1f ? app.overlayCanvas.rect.height : 1080f);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    /// <summary>Back: stop listening, or from a controller's page to the list, or close.</summary>
    public static void Back()
    {
        if (!Open) return;
        var r = root.GetComponent<Runner>();
        if (r != null && r.Back()) return;
        UnityEngine.Object.Destroy(root); root = null;
        TVGameScreen.SetHidden(false);
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
    }

    // ---- the TV side
    public static List<Device> Devices()
    {
        var list = new List<Device>();
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var a = new AndroidJavaClass(Act);
            foreach (var line in a.CallStatic<string[]>("inputDevices") ?? new string[0])
            {
                var p = line.Split('|'); if (p.Length < 6) continue;
                var d = new Device { desc = p[0], name = p[1].Trim(), kind = p[2], motion = p[3] == "1" };
                foreach (var x in p[4].Split(',')) if (int.TryParse(x, out int v)) d.axes.Add(v);
                foreach (var x in p[5].Split(',')) if (int.TryParse(x, out int v)) d.keys.Add(v);
                list.Add(d);
            }
        }
        catch (Exception e) { Debug.LogWarning("[Pads] " + e.Message); }
#endif
        list.Sort((x, y) => (x.kind == "pad" ? 0 : 1).CompareTo(y.kind == "pad" ? 0 : 1)); // controllers first
        var remotes = list.FindAll(x => x.kind != "pad");
        if (remotes.Count > 1)
        {
            string used = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using var a = new AndroidJavaClass(Act); used = a.CallStatic<string>("lastRemote"); } catch (Exception) { }
#endif
            var keep = remotes.Find(x => x.desc == used) ?? remotes.Find(x => !x.name.ToLowerInvariant().Contains("ir receiver")) ?? remotes[0];
            list.RemoveAll(x => x.kind != "pad" && x != keep);
        }
        return list;
    }

    private static string JavaStr(string method, int v)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { using var a = new AndroidJavaClass(Act); return a.CallStatic<string>(method, v); } catch (Exception) { }
#endif
        return method == "keyName" ? "KEYCODE_" + v : "AXIS_" + v;
    }

    // "Device 'x'-Button 96" -> "Button A" (a name a person recognises)
    public static string Describe(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "None") return "not set";
        int b = value.LastIndexOf("-Button ", StringComparison.Ordinal), a = value.LastIndexOf("-Axis ", StringComparison.Ordinal);
        if (b >= 0 && int.TryParse(value.Substring(b + 8), out int code)) return KeyLabel(code);
        if (a >= 0)
        {
            string rest = value.Substring(a + 6); char sign = rest.Length > 0 ? rest[rest.Length - 1] : '+';
            if (int.TryParse(rest.TrimEnd('+', '-'), out int ax)) return AxisLabel(ax, sign);
        }
        return value;
    }

    private static string KeyLabel(int code)
    {
        switch (code)
        {
            case 23: return "OK"; case 66: return "Enter"; case 82: return "Menu"; case 19: return "Arrow up"; case 20: return "Arrow down"; case 21: return "Arrow left"; case 22: return "Arrow right";
            case 102: return "L1"; case 103: return "R1"; case 104: return "L2"; case 105: return "R2"; case 106: return "Left stick press"; case 107: return "Right stick press";
            case 108: return "Start"; case 109: return "Select";
        }
        if (code >= 7 && code <= 16) return "Number " + (code - 7);
        string s = JavaStr("keyName", code).Replace("KEYCODE_", "").Replace("BUTTON_", "Button ").Replace('_', ' ').ToLowerInvariant();
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
    }

    private static string AxisLabel(int ax, char sign)
    {
        bool plus = sign != '-';
        switch (ax)
        {
            case 0: return plus ? "Left stick right" : "Left stick left";
            case 1: return plus ? "Left stick down" : "Left stick up";
            case 11: return plus ? "Right stick right" : "Right stick left";
            case 14: return plus ? "Right stick down" : "Right stick up";
            case 12: return plus ? "Right stick down" : "Right stick up";
            case 15: return plus ? "D-pad right" : "D-pad left";
            case 16: return plus ? "D-pad down" : "D-pad up";
            case 17: case 23: return "Left trigger";
            case 18: case 22: return "Right trigger";
        }
        return JavaStr("axisName", ax).Replace("AXIS_", "Axis ") + " " + sign;
    }

    // ---- Dolphin.ini
    public static Dictionary<string, string> ReadBindings()
    {
        var d = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(IniPath)) return d;
            bool inAndroid = false;
            foreach (var raw in File.ReadAllLines(IniPath))
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { inAndroid = l == "[Android]"; continue; }
                int eq = l.IndexOf('='); if (!inAndroid || eq < 0) continue;
                d[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
        }
        catch (Exception e) { Debug.LogWarning("[Pads] read: " + e.Message); }
        return d;
    }

    /// <summary>Sets (value) or clears (null) keys of the [Android] section, keeping everything else in the file.</summary>
    public static string WriteBindings(Dictionary<string, string> changes)
    {
        try
        {
            var lines = File.Exists(IniPath) ? new List<string>(File.ReadAllLines(IniPath)) : new List<string>();
            int start = lines.FindIndex(x => x.Trim() == "[Android]");
            if (start < 0) { lines.Add("[Android]"); start = lines.Count - 1; }
            int end = lines.FindIndex(start + 1, x => x.TrimStart().StartsWith("["));
            if (end < 0) end = lines.Count;
            var left = new Dictionary<string, string>(changes);
            for (int i = end - 1; i > start; i--)
            {
                int eq = lines[i].IndexOf('='); if (eq < 0) continue;
                string k = lines[i].Substring(0, eq).Trim();
                if (!left.TryGetValue(k, out string v)) continue;
                left.Remove(k);
                if (v == null) { lines.RemoveAt(i); end--; } else lines[i] = k + " = " + v;
            }
            foreach (var kv in left) if (kv.Value != null) { lines.Insert(end, kv.Key + " = " + kv.Value); end++; }
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath));
            File.WriteAllText(IniPath, string.Join("\n", lines) + "\n");
            return null;
        }
        catch (Exception e) { Debug.LogWarning("[Pads] write: " + e.Message); return e.Message; }
    }

    /// <summary>The 32-bit Dolphin starts out drawing everything on the processor (very slow on a TV): graphics go to the GPU,
    /// and the game gets its own core. Only settings still on those slow defaults are changed.</summary>
    public static void TuneDolphin()
    {
        try
        {
            if (!File.Exists(IniPath)) return;
            var lines = new List<string>(File.ReadAllLines(IniPath)); bool changed = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t == "GFXBackend = Software Renderer") { lines[i] = "GFXBackend = OGL"; changed = true; }
                else if (t == "CPUThread = False") { lines[i] = "CPUThread = True"; changed = true; }
            }
            if (changed) { File.WriteAllText(IniPath, string.Join("\n", lines) + "\n"); Debug.Log("[Pads] Dolphin set to GPU graphics + dual core"); }
            // graphics at the console's own size with the usual speed settings (only when the person has no graphics settings of their own)
            string gfx = Path.Combine(Path.GetDirectoryName(IniPath), "gfx_opengl.ini");
            if (!File.Exists(gfx))
                File.WriteAllText(gfx, "[Settings]\nEFBScale = 2\nMSAA = 0\n[Enhancements]\nMaxAnisotropy = 0\nForceFiltering = False\n" +
                                       "[Hacks]\nEFBAccessEnable = False\nEFBToTextureEnable = True\nEFBCopyEnable = True\nEFBScaledCopy = False\nEFBCopyCacheEnable = True\nEFBEmulateFormatChanges = False\n");
        }
        catch (Exception e) { Debug.LogWarning("[Pads] tune: " + e.Message); }
    }

    // what Dolphin calls each GameCube input on its "Android/0/Touchscreen" device (the presses Revo binds above land there)
    private static readonly Dictionary<string, string> GcId = new Dictionary<string, string>
    {
        ["InputA"] = "`Button 0`", ["InputB"] = "`Button 1`", ["InputStart"] = "`Button 2`", ["InputX"] = "`Button 3`", ["InputY"] = "`Button 4`", ["InputZ"] = "`Button 5`",
        ["DPadUp"] = "`Button 6`", ["DPadDown"] = "`Button 7`", ["DPadLeft"] = "`Button 8`", ["DPadRight"] = "`Button 9`",
        ["MainUp"] = "`Axis 11`", ["MainDown"] = "`Axis 12`", ["MainLeft"] = "`Axis 13`", ["MainRight"] = "`Axis 14`",
        ["CStickUp"] = "`Axis 16`", ["CStickDown"] = "`Axis 17`", ["CStickLeft"] = "`Axis 18`", ["CStickRight"] = "`Axis 19`",
        ["InputL"] = "`Axis 20`", ["InputR"] = "`Axis 21`",
    };

    public static bool WiiSideways { get => PlayerPrefs.GetInt("tv.wii.sideways", 1) == 1; set { PlayerPrefs.SetInt("tv.wii.sideways", value ? 1 : 0); PlayerPrefs.Save(); WriteWiimote(); } }

    /// <summary>Wii button -> the GameCube input that drives it. Sideways (Mario, Kirby...): arrows = D-pad, OK = 2 and A,
    /// channel up = 1, channel down = B. Pointing: arrows move the pointer, OK = A, channel down = B, channel up = shake.</summary>
    public static List<(string wii, string gc)> WiiLayout(bool sideways) => sideways
        ? new List<(string, string)> { ("Buttons/2", "InputA"), ("Buttons/A", "InputA"), ("Buttons/1", "InputR"), ("Buttons/B", "InputL"), ("Buttons/+", "InputStart"), ("Buttons/-", "InputZ"), ("Buttons/Home", "InputY"),
              ("D-Pad/Up", "MainUp"), ("D-Pad/Down", "MainDown"), ("D-Pad/Left", "MainLeft"), ("D-Pad/Right", "MainRight"), ("Shake/X", "InputX"), ("Shake/Y", "InputX"), ("Shake/Z", "InputX") }
        : new List<(string, string)> { ("Buttons/A", "InputA"), ("Buttons/B", "InputL"), ("Buttons/1", "InputX"), ("Buttons/2", "InputY"), ("Buttons/+", "InputStart"), ("Buttons/-", "InputZ"), ("Buttons/Home", "InputB"),
              ("IR/Up", "MainUp"), ("IR/Down", "MainDown"), ("IR/Left", "MainLeft"), ("IR/Right", "MainRight"), ("D-Pad/Up", "DPadUp"), ("D-Pad/Down", "DPadDown"), ("D-Pad/Left", "DPadLeft"), ("D-Pad/Right", "DPadRight"),
              ("Shake/X", "InputR"), ("Shake/Y", "InputR"), ("Shake/Z", "InputR") };

    /// <summary>Writes Dolphin's Wii Remote 1 (emulated) onto player 1's buttons, so Wii games answer the remote too.</summary>
    public static void WriteWiimote()
    {
        try
        {
            string dir = Path.GetDirectoryName(IniPath);
            if (!Directory.Exists(dir)) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("[Wiimote1]\nDevice = Android/0/Touchscreen\nSource = 1\nExtension = None\n");
            foreach (var (wii, gc) in WiiLayout(WiiSideways)) sb.Append(wii).Append(" = ").Append(GcId[gc]).Append('\n');
            sb.Append("Options/Sideways Wiimote = ").Append(WiiSideways ? 1 : 0).Append('\n');
            sb.Append("[Wiimote2]\nSource = 0\n[Wiimote3]\nSource = 0\n[Wiimote4]\nSource = 0\n");
            File.WriteAllText(Path.Combine(dir, "WiimoteNew.ini"), sb.ToString());
        }
        catch (Exception e) { Debug.LogWarning("[Pads] wiimote: " + e.Message); }
    }

    public static string Bind(string desc, int key) => "Device '" + desc + "'-Button " + key;
    public static string BindAxis(string desc, int axis, char sign) => "Device '" + desc + "'-Axis " + axis + sign;

    /// <summary>The whole controller in one go: a game controller as a GameCube pad, the remote as a simple one (OK = A).</summary>
    public static Dictionary<string, string> QuickSetup(Device d, int player)
    {
        var m = new Dictionary<string, string>(); string s = "_" + player;
        void K(string k, int code) => m[k + s] = Bind(d.desc, code);
        void X(string k, int ax, char sign) => m[k + s] = BindAxis(d.desc, ax, sign);
        if (d.kind == "pad")
        {
            K("InputA", 96); K("InputB", 97); K("InputX", 99); K("InputY", 100); K("InputZ", 103); K("InputStart", 108);
            if (d.axes.Contains(17)) X("InputL", 17, '+'); else if (d.axes.Contains(23)) X("InputL", 23, '+'); else K("InputL", 104);
            if (d.axes.Contains(18)) X("InputR", 18, '+'); else if (d.axes.Contains(22)) X("InputR", 22, '+'); else K("InputR", 105);
            if (d.axes.Contains(0) && d.axes.Contains(1)) { X("MainUp", 1, '-'); X("MainDown", 1, '+'); X("MainLeft", 0, '-'); X("MainRight", 0, '+'); }
            else { K("MainUp", 19); K("MainDown", 20); K("MainLeft", 21); K("MainRight", 22); }
            int cy = d.axes.Contains(14) ? 14 : d.axes.Contains(12) ? 12 : -1;
            if (d.axes.Contains(11) && cy >= 0) { X("CStickUp", cy, '-'); X("CStickDown", cy, '+'); X("CStickLeft", 11, '-'); X("CStickRight", 11, '+'); }
            if (d.axes.Contains(15) && d.axes.Contains(16)) { X("DPadUp", 16, '-'); X("DPadDown", 16, '+'); X("DPadLeft", 15, '-'); X("DPadRight", 15, '+'); }
            else if (d.axes.Contains(0)) { K("DPadUp", 19); K("DPadDown", 20); K("DPadLeft", 21); K("DPadRight", 22); }
        }
        else
        {
            // a remote: arrows steer, OK is A, Menu is Start, the number keys (when it has them) are the other buttons
            K("MainUp", 19); K("MainDown", 20); K("MainLeft", 21); K("MainRight", 22); K("InputA", 23); K("InputStart", 82);
            int[] nums = { 8, 9, 10, 11, 12, 13 }; string[] to = { "InputB", "InputX", "InputY", "InputZ", "InputL", "InputR" };
            for (int i = 0; i < nums.Length; i++) if (d.keys.Contains(nums[i])) K(to[i], nums[i]);
            int[] colours = { 183, 184, 185, 186 }; string[] cto = { "InputB", "InputX", "InputY", "InputZ" };
            for (int i = 0; i < colours.Length; i++) if (!m.ContainsKey(cto[i] + s) && d.keys.Contains(colours[i])) K(cto[i], colours[i]);
            if (!m.ContainsKey("InputL" + s)) K("InputL", 167); // channel down (most remotes have it, even when the TV doesn't say)
            if (!m.ContainsKey("InputR" + s)) K("InputR", 166); // channel up
        }
        return m;
    }

    public static int PlayerOf(Device d, Dictionary<string, string> b)
    {
        for (int p = 0; p < 4; p++) foreach (var i in Inputs) if (b.TryGetValue(i.key + "_" + p, out string v) && v.Contains("'" + d.desc + "'")) return p;
        return -1;
    }

    // ---- the screen
    private class Runner : MonoBehaviour
    {
        private RectTransform rt, content; private float h; private TMP_FontAsset font; private TextMeshProUGUI status, ask;
        private Device dev; private int player; private int captureIndex = -1; private bool walkAll; private float statusUntil;

        public void Build(RectTransform r, float hh)
        {
            rt = r; h = hh; font = TVServerScreen.SettingsFont();
            var bg = TVServerScreen.Child(rt, "Background").gameObject.AddComponent<Image>(); bg.color = new Color(0.035f, 0.035f, 0.04f, 1f);
            var band = TVServerScreen.Child(rt, "Band"); band.anchorMin = new Vector2(0f, 0.2f); band.anchorMax = new Vector2(1f, 0.8f);
            var stripes = band.gameObject.AddComponent<RawImage>(); stripes.texture = TVServerScreen.StripeTex(); stripes.uvRect = new Rect(0f, 0f, 1f, h * 0.6f / 6f); stripes.raycastTarget = false;
            TVServerScreen.Line(rt, 0.8f, h); TVServerScreen.Line(rt, 0.2f, h);
            var tab = TVServerScreen.Child(rt, "Tab"); tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f); tab.pivot = new Vector2(0f, 1f);
            tab.anchoredPosition = new Vector2(h * 0.045f, -h * 0.06f); tab.sizeDelta = new Vector2(h * 0.62f, h * 0.105f);
            var ti = tab.gameObject.AddComponent<Image>(); ti.sprite = TVServerScreen.TabSprite(); ti.type = Image.Type.Sliced; ti.raycastTarget = false;
            var title = TVServerScreen.Text(tab, "Controller settings", font, h * 0.05f, new Color(0.2f, 0.2f, 0.22f)); title.alignment = TextAlignmentOptions.MidlineLeft;
            TVServerScreen.Inset((RectTransform) title.transform, h * 0.045f, 0f);

            var view = TVServerScreen.Child(rt, "List"); view.anchorMin = new Vector2(0.1f, 0.215f); view.anchorMax = new Vector2(0.9f, 0.785f); view.offsetMin = view.offsetMax = Vector2.zero;
            view.gameObject.AddComponent<RectMask2D>();
            content = TVServerScreen.Child(view, "Content"); content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f); content.offsetMin = content.offsetMax = Vector2.zero;
            var lg = content.gameObject.AddComponent<VerticalLayoutGroup>(); lg.spacing = h * 0.014f;
            lg.padding = new RectOffset((int) (h * 0.01f), (int) (h * 0.01f), (int) (h * 0.012f), (int) (h * 0.012f));
            lg.childControlWidth = true; lg.childControlHeight = false; lg.childForceExpandWidth = true; lg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sc = view.gameObject.AddComponent<ScrollRect>(); sc.viewport = view; sc.content = content; sc.horizontal = false; sc.movementType = ScrollRect.MovementType.Clamped;

            status = TVServerScreen.Text(rt, "", font, h * 0.03f, new Color(0.75f, 0.9f, 1f));
            var srt = (RectTransform) status.transform; srt.anchorMin = new Vector2(0.3f, 0f); srt.anchorMax = new Vector2(0.97f, 0f); srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = new Vector2(0f, h * 0.07f); srt.sizeDelta = new Vector2(0f, h * 0.09f); status.alignment = TextAlignmentOptions.MidlineRight; status.enableWordWrapping = true;

            // the "press a button" card, over everything while listening
            var card = TVServerScreen.Child(rt, "Ask"); card.anchorMin = new Vector2(0.2f, 0.33f); card.anchorMax = new Vector2(0.8f, 0.67f); card.offsetMin = card.offsetMax = Vector2.zero;
            var ci = card.gameObject.AddComponent<Image>(); ci.sprite = TVServerScreen.PillSprite(); ci.type = Image.Type.Sliced; ci.color = new Color(0.86f, 0.93f, 1f);
            ask = TVServerScreen.Text(card, "", font, h * 0.045f, new Color(0.1f, 0.12f, 0.16f)); ask.enableWordWrapping = true; TVServerScreen.Inset((RectTransform) ask.transform, h * 0.04f, h * 0.02f);
            card.gameObject.SetActive(false);

            var back = TVServerScreen.Child(rt, "Pill Back"); back.anchorMin = back.anchorMax = Vector2.zero; back.pivot = Vector2.zero;
            back.anchoredPosition = new Vector2(h * 0.065f, h * 0.06f); back.sizeDelta = new Vector2(h * 0.36f, h * 0.1f);
            var bi = back.gameObject.AddComponent<Image>(); bi.sprite = TVServerScreen.PillSprite(); bi.type = Image.Type.Sliced;
            var bb = back.gameObject.AddComponent<Button>(); bb.targetGraphic = bi; bb.onClick.AddListener(TVControllers.Back); backPill = bb;
            TVServerScreen.Text(back, "← Back", font, h * 0.045f, new Color(0.15f, 0.15f, 0.17f));
            ShowList();
        }

        private void Say(string s, float secs = 6f) { status.text = s; statusUntil = Time.unscaledTime + secs; }

        private void Clear() { for (int i = content.childCount - 1; i >= 0; i--) DestroyImmediate(content.GetChild(i).gameObject); }

        private RectTransform Row(string main, string sub, Color badge, string badgeText, UnityEngine.Events.UnityAction click, float scale = 1f)
        {
            float rowH = h * 0.115f * scale;
            var r = TVServerScreen.Child(content, "Row " + main);
            r.sizeDelta = new Vector2(0f, rowH);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = rowH;
            var img = r.gameObject.AddComponent<Image>(); img.sprite = TVServerScreen.PillSprite(); img.type = Image.Type.Sliced;
            if (click != null) { var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(click); }
            else img.color = new Color(1f, 1f, 1f, 0.82f);
            float left = h * 0.03f;
            if (badgeText != null)
            {
                var bd = TVServerScreen.Child(r, "Badge"); bd.anchorMin = bd.anchorMax = new Vector2(0f, 0.5f); bd.pivot = new Vector2(0f, 0.5f);
                bd.anchoredPosition = new Vector2(h * 0.022f, 0f); bd.sizeDelta = Vector2.one * rowH * 0.72f;
                var bim = bd.gameObject.AddComponent<Image>(); bim.sprite = TVServerScreen.DotSprite(); bim.color = badge; bim.raycastTarget = false;
                var bt = TVServerScreen.Text(bd, badgeText, font, rowH * 0.26f, Color.white); bt.fontStyle = FontStyles.Bold;
                left = h * 0.04f + rowH * 0.72f;
            }
            var t = TVServerScreen.Text(r, main, font, h * 0.038f * Mathf.Clamp(scale, 0.85f, 1f), new Color(0.13f, 0.13f, 0.15f));
            t.fontStyle = FontStyles.Bold; t.alignment = sub != null ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
            var trt = (RectTransform) t.transform; trt.anchorMin = new Vector2(0f, sub != null ? (scale > 1f ? 0.64f : 0.48f) : 0f); trt.anchorMax = new Vector2(1f, sub != null ? 0.95f : 1f); trt.offsetMin = new Vector2(left, 0f); trt.offsetMax = new Vector2(-h * 0.03f, 0f);
            if (sub != null)
            {
                var s = TVServerScreen.Text(r, sub, font, h * 0.026f, new Color(0.36f, 0.38f, 0.43f));
                s.alignment = TextAlignmentOptions.TopLeft; s.enableWordWrapping = scale > 1f; s.overflowMode = TextOverflowModes.Ellipsis;
                var sr = (RectTransform) s.transform; sr.anchorMin = new Vector2(0f, 0.06f); sr.anchorMax = new Vector2(1f, scale > 1f ? 0.6f : 0.46f); sr.offsetMin = new Vector2(left, 0f); sr.offsetMax = new Vector2(-h * 0.03f, 0f);
            }
            return r;
        }

        // the list: every remote and controller connected now
        private void ShowList()
        {
            dev = null; Clear();
            var devs = Devices(); var b = ReadBindings();
            if (devs.Count == 0) Row("No remote or controller found", "Connect a game controller (Bluetooth or USB), then come back here", new Color(0.45f, 0.47f, 0.52f), "?", null);
            foreach (var d in devs)
            {
                var dd = d; int p = PlayerOf(d, b);
                string sub = (d.kind == "pad" ? "Game controller" : "TV remote") + (d.motion ? "  ·  has motion sensors" : "") + "  ·  " + (p >= 0 ? "Player " + (p + 1) + ", set up" : "not set up yet: press OK");
                string nm = d.kind != "pad" ? "TV remote" : d.name.Length > 0 ? d.name : "Controller";
                Row(nm, sub, d.kind == "pad" ? new Color(0.35f, 0.62f, 0.25f) : new Color(0.1f, 0.5f, 0.85f), d.kind == "pad" ? "Pad" : "TV", () => ShowDevice(dd));
            }
            bool helper = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using var rv = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV"); helper = rv.CallStatic<bool>("isHomeServiceAllowed", AndroidLinker.currentActivity); } catch (Exception) { }
#endif
            Row(helper ? "Quitting a game: hold Back for 7 seconds" : "Quitting a game: turn on Revo's TV helper",
                helper ? "A quick Back still opens the emulator's own menu. Holding Back for 7 seconds asks if you want to quit to Revo. (Dolphin keeps Back for its menu, so Back can't be a game button.)"
                       : "Press OK, then turn on \"Revo\" in the TV's Accessibility settings. After that, holding Back for 7 seconds in a game asks if you want to quit to Revo.",
                new Color(0.2f, 0.6f, 0.45f), "Back", helper ? null : (UnityEngine.Events.UnityAction) (() =>
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    try { using var rv = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV"); rv.CallStatic<bool>("openAccessibilitySettings", AndroidLinker.currentActivity); } catch (Exception) { }
#endif
                }), 1.5f);
            bool is64 = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using var rv = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV"); is64 = rv.CallStatic<bool>("is64Bit"); } catch (Exception) { }
#endif
            if (is64)
                Row("Wii motion with a controller", "Press OK to open Dolphin. There: Settings > Wii Input > Wii Remote 1 > Emulated, pick your controller, then under Motion Input choose its motion sensors (any controller with a gyro works). Tilting and shaking it then moves the Wii Remote.",
                    new Color(0.55f, 0.45f, 0.85f), "Wii", () => AndroidLinker.LaunchApp(TVDisc.Package, false), 1.5f);
            else
                Row("About Wii games", "This TV's Dolphin (the 32-bit one) reads GameCube controls only: it has no Wii Remote buttons or motion, so most Wii games can't be played here (they start but don't respond). GameCube games work fully.", new Color(0.55f, 0.45f, 0.85f), "i", null, 1.5f);
            Say(devs.Count > 0 ? "Pick a remote or controller to set it up" : "", 30f);
            Focus();
        }

        private void ShowDevice(Device d)
        {
            if (dev != d) focusRow = 0;
            dev = d; Clear();
            var b = ReadBindings(); int p = PlayerOf(d, b);
            if (p < 0) p = FreePlayer(b, d);
            player = p;
            Row("Quick setup", d.kind == "pad" ? "Sets every button the usual way (A, B, X, Y, sticks, triggers) in one press" : "Arrows steer, OK is A, Menu is Start, the number keys are B, X, Y, Z, L, R",
                new Color(0.95f, 0.65f, 0.1f), "Go", () => { focusRow = 0; Apply(Fresh(QuickSetup(dev, player))); Say("Set up as Player " + (player + 1) + ". It's ready in your games."); ShowDevice(dev); });
            Row("Player " + (player + 1), "Press OK to choose which player this is (1 to 4)", new Color(0.3f, 0.3f, 0.36f), "P" + (player + 1), () => { focusRow = 1; PickPlayer(); });
            Row("Test your buttons", "A GameCube controller and a Wii Remote light up as you press each button", new Color(0.15f, 0.65f, 0.55f), "Test", () => { focusRow = 2; ShowTester(); });
            Row(WiiSideways ? "Wii games: remote held sideways" : "Wii games: remote pointing", WiiSideways ? "Like Mario and Kirby: arrows are the D-pad, OK is 2 (and A), CH+ is 1, CH- is B. Press OK for pointing games" : "For games with a pointer: arrows move it, OK is A, CH- is B, CH+ shakes. Press OK for sideways games",
                new Color(0.1f, 0.6f, 0.9f), "Wii", () => { focusRow = 3; WiiSideways = !WiiSideways; Say(WiiSideways ? "Wii games: held sideways" : "Wii games: pointing"); ShowDevice(dev); });
            Row("Set up every button, one by one", "You're asked for each button in turn: press the one you want (Back stops)", new Color(0.2f, 0.55f, 0.75f), "1-2", () => { focusRow = 4; walkAll = true; Listen(0); });
            for (int i = 0; i < Inputs.Length; i++)
            {
                int k = i; b.TryGetValue(Inputs[i].key + "_" + player, out string v);
                bool mine = v != null && v.Contains("'" + d.desc + "'");
                Row(Inputs[i].label + "   =   " + (mine ? Describe(v) : v != null && v != "None" ? "another controller" : "not set"), null, new Color(0.42f, 0.33f, 0.75f), Inputs[i].label.Length <= 5 ? Inputs[i].label : "•", () => { focusRow = 5 + k; walkAll = false; Listen(k); }, 0.72f);
            }
            Row("Clear this controller", "Removes its buttons from the games", new Color(0.7f, 0.25f, 0.25f), "X", () =>
            {
                focusRow = Inputs.Length + 5;
                var m = new Dictionary<string, string>(); var bb = ReadBindings();
                foreach (var kv in bb) if (kv.Value.Contains("'" + dev.desc + "'")) m[kv.Key] = null;
                Apply(m); Say("Cleared"); ShowDevice(dev);
            });
            Focus();
        }

        private Dictionary<string, string> Fresh(Dictionary<string, string> m)
        {
            var b = ReadBindings(); var others = Devices().FindAll(o => o.desc != dev.desc);
            foreach (var kv in b)
            {
                if (m.ContainsKey(kv.Key)) continue;
                bool mine = kv.Value.Contains("'" + dev.desc + "'"), thisPlayer = kv.Key.EndsWith("_" + player);
                bool otherLive = others.Exists(o => kv.Value.Contains("'" + o.desc + "'"));
                if (mine || (thisPlayer && !otherLive)) m[kv.Key] = null;
            }
            return m;
        }

        private int FreePlayer(Dictionary<string, string> b, Device d)
        {
            var others = Devices().FindAll(o => o.desc != d.desc);
            for (int p = 0; p < 4; p++)
            {
                bool used = false;
                foreach (var i in Inputs) if (b.TryGetValue(i.key + "_" + p, out string v) && others.Exists(o => v.Contains("'" + o.desc + "'"))) { used = true; break; }
                if (!used) return p;
            }
            return 0;
        }

        private bool picking, testing;
        private readonly Dictionary<string, Image> testDots = new Dictionary<string, Image>(); // "gc:InputA" / "wii:Buttons/A" -> its dot
        private readonly Dictionary<Image, float> litUntil = new Dictionary<Image, float>();
        private readonly Dictionary<Image, Color> baseColor = new Dictionary<Image, Color>();
        private RectTransform testRoot;

        private void ShowTester()
        {
            captureIndex = -1; walkAll = false;
            testing = true; Clear(); testDots.Clear(); litUntil.Clear();
            ask.transform.parent.gameObject.SetActive(false);
            var holder = TVServerScreen.Child(content, "Tester");
            holder.sizeDelta = new Vector2(0f, h * 0.53f); // the list sizes rows by their own height
            var le = holder.gameObject.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = h * 0.53f;
            testRoot = holder;
            // the GameCube controller, left
            var gc = Part(holder, "GC", new Vector2(0.25f, 0.45f), new Vector2(h * 0.62f, h * 0.36f), new Color(0.33f, 0.28f, 0.6f), true);
            Label(gc, "GameCube", new Vector2(0.45f, 0.9f), 0.032f);
            Dot(gc, "gc:InputL", "L", new Vector2(0.18f, 1.06f), 0.07f); Dot(gc, "gc:InputR", "R", new Vector2(0.82f, 1.06f), 0.07f); Dot(gc, "gc:InputZ", "Z", new Vector2(0.93f, 0.9f), 0.05f);
            Dot(gc, "gc:MainUp", "", new Vector2(0.22f, 0.7f), 0.045f); Dot(gc, "gc:MainDown", "", new Vector2(0.22f, 0.44f), 0.045f); Dot(gc, "gc:MainLeft", "", new Vector2(0.13f, 0.57f), 0.045f); Dot(gc, "gc:MainRight", "", new Vector2(0.31f, 0.57f), 0.045f);
            Label(gc, "stick", new Vector2(0.22f, 0.57f), 0.022f);
            Dot(gc, "gc:DPadUp", "", new Vector2(0.33f, 0.33f), 0.04f); Dot(gc, "gc:DPadDown", "", new Vector2(0.33f, 0.13f), 0.04f); Dot(gc, "gc:DPadLeft", "", new Vector2(0.26f, 0.23f), 0.04f); Dot(gc, "gc:DPadRight", "", new Vector2(0.4f, 0.23f), 0.04f);
            Dot(gc, "gc:InputStart", "Start", new Vector2(0.5f, 0.6f), 0.06f);
            Dot(gc, "gc:InputA", "A", new Vector2(0.78f, 0.58f), 0.1f, new Color(0.2f, 0.75f, 0.45f)); Dot(gc, "gc:InputB", "B", new Vector2(0.66f, 0.45f), 0.065f, new Color(0.85f, 0.3f, 0.3f));
            Dot(gc, "gc:InputX", "X", new Vector2(0.92f, 0.6f), 0.06f); Dot(gc, "gc:InputY", "Y", new Vector2(0.7f, 0.82f), 0.06f);
            Dot(gc, "gc:CStickUp", "", new Vector2(0.67f, 0.3f), 0.035f); Dot(gc, "gc:CStickDown", "", new Vector2(0.67f, 0.12f), 0.035f); Dot(gc, "gc:CStickLeft", "", new Vector2(0.6f, 0.21f), 0.035f); Dot(gc, "gc:CStickRight", "", new Vector2(0.74f, 0.21f), 0.035f);
            Label(gc, "C", new Vector2(0.67f, 0.21f), 0.022f);
            // the Wii Remote, right (held the way the Wii style says)
            bool side = WiiSideways;
            var wr = Part(holder, "Wii", new Vector2(0.73f, 0.47f), side ? new Vector2(h * 0.7f, h * 0.2f) : new Vector2(h * 0.2f, h * 0.46f), new Color(0.93f, 0.94f, 0.96f), true);
            Label(wr, side ? "Wii Remote (sideways)" : "Wii Remote", side ? new Vector2(0.5f, 1.25f) : new Vector2(0.5f, 1.07f), 0.03f, Color.white);
            Vector2 P(float along, float across) => side ? new Vector2(along, 1f - across) : new Vector2(across, 1f - along); // along the remote from its top
            Dot(wr, "wii:D-Pad/Up", "", P(0.08f, side ? 0.3f : 0.5f), 0.04f); Dot(wr, "wii:D-Pad/Down", "", P(side ? 0.08f : 0.24f, side ? 0.7f : 0.5f), 0.04f);
            Dot(wr, "wii:D-Pad/Left", "", P(side ? 0.03f : 0.16f, side ? 0.5f : 0.3f), 0.04f); Dot(wr, "wii:D-Pad/Right", "", P(side ? 0.13f : 0.16f, side ? 0.5f : 0.7f), 0.04f);
            Dot(wr, "wii:Buttons/A", "A", P(0.34f, 0.5f), 0.07f); Dot(wr, "wii:Buttons/B", "B", P(0.34f, side ? 0.08f : 0.92f), 0.05f, new Color(0.5f, 0.5f, 0.55f));
            Dot(wr, "wii:Buttons/-", "-", P(0.52f, 0.25f), 0.045f); Dot(wr, "wii:Buttons/Home", "H", P(0.52f, 0.5f), 0.045f); Dot(wr, "wii:Buttons/+", "+", P(0.52f, 0.75f), 0.045f);
            Dot(wr, "wii:Buttons/1", "1", P(0.76f, 0.5f), 0.055f); Dot(wr, "wii:Buttons/2", "2", P(0.9f, 0.5f), 0.055f);
            Dot(wr, "wii:Shake/X", "shake", side ? new Vector2(0.5f, -0.35f) : new Vector2(0.5f, -0.08f), 0.06f, new Color(0.95f, 0.65f, 0.1f));
            if (!side) Dot(wr, "wii:IR/Up", "pointer", new Vector2(0.5f, 1.17f), 0.05f, new Color(0.3f, 0.6f, 1f));
#if UNITY_ANDROID && !UNITY_EDITOR
            using var a = new AndroidJavaClass(Act); a.CallStatic("startCapture");
#endif
            Say("Press any button: the parts it works as light up. Back goes back", 600f);
        }

        private RectTransform Part(RectTransform parent, string name, Vector2 at, Vector2 size, Color c, bool body)
        {
            var r = TVServerScreen.Child(parent, name); r.anchorMin = r.anchorMax = at; r.sizeDelta = size;
            var im = r.gameObject.AddComponent<Image>(); im.sprite = TVServerScreen.PillSprite(); im.type = Image.Type.Sliced; im.color = c; im.raycastTarget = false;
            return r;
        }

        private void Label(RectTransform parent, string text, Vector2 at, float size, Color? col = null)
        {
            var t = TVServerScreen.Text(parent, text, font, h * size, col ?? new Color(1f, 1f, 1f, 0.85f));
            var r = (RectTransform) t.transform; r.anchorMin = r.anchorMax = at; r.sizeDelta = new Vector2(h * 0.5f, h * size * 1.6f);
            t.raycastTarget = false; t.enableWordWrapping = false;
        }

        private void Dot(RectTransform parent, string key, string text, Vector2 at, float size, Color? col = null)
        {
            var r = TVServerScreen.Child(parent, key); r.anchorMin = r.anchorMax = at; r.sizeDelta = Vector2.one * h * size;
            var im = r.gameObject.AddComponent<Image>(); im.sprite = TVServerScreen.DotSprite(); im.raycastTarget = false;
            im.color = col ?? new Color(0.55f, 0.57f, 0.62f);
            if (text.Length > 0) { var t = TVServerScreen.Text(r, text, font, h * size * (text.Length > 2 ? 0.28f : 0.5f), Color.white); t.raycastTarget = false; t.fontStyle = FontStyles.Bold; t.enableWordWrapping = false; }
            testDots[key] = im; im.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0f);
        }

        // a press while testing: every GameCube input it is bound to lights up, and every Wii button driven by those
        private void LightFor(string got)
        {
            var p = got.Split('|'); if (p.Length < 3) return;
            string val = p[1] == "Button" ? Bind(p[0], int.Parse(p[2])) : p.Length >= 4 ? BindAxis(p[0], int.Parse(p[2]), p[3][0]) : null;
            var b = ReadBindings(); bool any = false;
            foreach (var i in Inputs)
            {
                if (!b.TryGetValue(i.key + "_0", out string v) || v != val) continue;
                any = true; Light("gc:" + i.key);
                foreach (var (wii, gc) in WiiLayout(WiiSideways)) if (gc == i.key) Light("wii:" + (wii.StartsWith("Shake") ? "Shake/X" : wii.StartsWith("IR/") ? "IR/Up" : wii));
            }
            Say(any ? "Works" : "That button isn't set for Player 1 yet (Quick setup, or set it one by one)", any ? 1.5f : 4f);
        }

        private void Light(string key)
        {
            if (!testDots.TryGetValue(key, out var im)) return;
            if (!litUntil.ContainsKey(im)) baseColor[im] = im.color;
            litUntil[im] = Time.unscaledTime + 0.9f;
            var o = im.GetComponent<Outline>(); o.effectColor = new Color(1f, 1f, 1f, 1f); o.effectDistance = Vector2.one * h * 0.005f;
            im.color = new Color(1f, 0.85f, 0.1f); im.transform.localScale = Vector3.one * 1.35f; // pressed: bright yellow and bigger
        }

        private void FadeLights()
        {
            if (litUntil.Count == 0) return;
            var done = new List<Image>();
            foreach (var kv in litUntil) if (Time.unscaledTime > kv.Value && kv.Key != null) { kv.Key.transform.localScale = Vector3.one; if (baseColor.TryGetValue(kv.Key, out var bc)) kv.Key.color = bc; kv.Key.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0f); done.Add(kv.Key); }
            foreach (var d in done) litUntil.Remove(d);
        }
        private void PickPlayer()
        {
            picking = true; Clear();
            for (int p = 0; p < 4; p++)
            {
                int k = p;
                Row("Player " + (p + 1), p == player ? "Now" : null, p == player ? new Color(0.95f, 0.65f, 0.1f) : new Color(0.3f, 0.3f, 0.36f), (p + 1).ToString(), () => { picking = false; focusRow = 1; MovePlayer(k); });
            }
            focusRow = player; Focus();
        }

        // the controller's buttons move to the player number picked
        private void MovePlayer(int to)
        {
            var b = ReadBindings(); var m = new Dictionary<string, string>();
            if (to == player) { ShowDevice(dev); return; }
            foreach (var i in Inputs)
            {
                string from = i.key + "_" + player, dest = i.key + "_" + to;
                if (b.TryGetValue(from, out string v) && v.Contains("'" + dev.desc + "'")) { m[from] = null; m[dest] = v; }
            }
            player = to;
            if (m.Count > 0) Apply(m);
            Say("Now Player " + (to + 1)); ShowDevice(dev);
        }

        private void Apply(Dictionary<string, string> m)
        {
            string err = WriteBindings(m);
            if (err != null) Say("Couldn't save for Dolphin: " + err, 10f);
            else WriteWiimote();
        }

        // waiting for one button
        private void Listen(int i)
        {
            if (i >= Inputs.Length) { captureIndex = -1; ask.transform.parent.gameObject.SetActive(false); Say("All set. It's ready in your games."); ShowDevice(dev); return; }
            captureIndex = i;
            ask.text = "<size=70%>" + (walkAll ? (i + 1) + " of " + Inputs.Length + "   ·   " : "") + "Player " + (player + 1) + "</size>\nPress the button for <b>" + Inputs[i].label + "</b>\n<size=60%>on the remote or controller you want.  Back stops</size>";
            ask.transform.parent.gameObject.SetActive(true);
#if UNITY_ANDROID && !UNITY_EDITOR
            using var a = new AndroidJavaClass(Act); a.CallStatic("startCapture");
#endif
        }

        public bool Back()
        {
            if (captureIndex >= 0)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var a = new AndroidJavaClass(Act); a.CallStatic("stopCapture");
#endif
                captureIndex = -1; ask.transform.parent.gameObject.SetActive(false); return true;
            }
            if (testing) { StopTesting(); return true; }
            if (picking) { picking = false; ShowDevice(dev); return true; }
            if (dev != null) { ShowList(); return true; }
            return false;
        }

        private void StopTesting()
        {
            testing = false; litUntil.Clear(); Say("", 0.1f);
#if UNITY_ANDROID && !UNITY_EDITOR
            using var a = new AndroidJavaClass(Act); a.CallStatic("stopCapture");
#endif
            if (dev != null) ShowDevice(dev); else ShowList();
        }

        private float listCheck;
        private int lastCount = -1;
        private void Update()
        {
            if (statusUntil > 0 && Time.unscaledTime > statusUntil) { status.text = ""; statusUntil = 0; }
            // a controller switched on or off: the list follows
            if (dev == null && captureIndex < 0 && Time.unscaledTime > listCheck)
            {
                listCheck = Time.unscaledTime + 2f; int n = Devices().Count;
                if (lastCount >= 0 && n != lastCount) ShowList();
                lastCount = n;
            }
            if (testing)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { StopTesting(); return; }
                FadeLights();
                string tg = null;
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var a = new AndroidJavaClass(Act)) { tg = a.CallStatic<string>("pollCapture"); if (tg != null && tg != "cancel") a.CallStatic("startCapture"); }
#endif
                if (tg == "cancel") StopTesting(); else if (tg != null) LightFor(tg);
                return;
            }
            if (captureIndex < 0 && Input.GetKeyDown(KeyCode.LeftArrow) && backPill != null && TVNav.Focused != null && TVNav.Focused.transform.IsChildOf(content)) TVNav.FocusButton(backPill.gameObject);
            if (captureIndex < 0) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }
            string got = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var a = new AndroidJavaClass(Act)) got = a.CallStatic<string>("pollCapture");
#endif
            if (got == null) return;
            int at = captureIndex;
            if (got == "cancel") { walkAll = false; captureIndex = -1; ask.transform.parent.gameObject.SetActive(false); Say("Stopped. What was set so far is kept."); ShowDevice(dev); return; }
            {
                var p = got.Split('|');
                string val = p.Length >= 3 && p[1] == "Button" ? Bind(p[0], int.Parse(p[2])) : p.Length >= 4 ? BindAxis(p[0], int.Parse(p[2]), p[3][0]) : null;
                if (val != null)
                {
                    if (p[0] != dev.desc) Say("That press came from another device: it's used anyway");
                    Apply(new Dictionary<string, string> { [Inputs[at].key + "_" + player] = val });
                    AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
                }
            }
            if (walkAll) Listen(at + 1);
            else { captureIndex = -1; ask.transform.parent.gameObject.SetActive(false); ShowDevice(dev); }
        }

        private int focusRow; private Button backPill;
        private void Focus() { StopAllCoroutines(); StartCoroutine(FocusSoon()); }
        private System.Collections.IEnumerator FocusSoon()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            // after OK has been let go (the press's own release handling puts the light under the old press point)
            yield return new WaitForSecondsRealtime(0.35f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var all = content.GetComponentsInChildren<Button>();
            if (all.Length == 0) yield break;
            var want = all[Mathf.Clamp(dev == null ? 0 : focusRow, 0, all.Length - 1)].gameObject;
            if (picking) want = all[Mathf.Clamp(player, 0, all.Length - 1)].gameObject;
            // the light stays on that row for a moment (the menu's own settling can move it), unless a key is pressed
            for (float until = Time.unscaledTime + 1.2f; Time.unscaledTime < until && want != null; )
            {
                if (Input.anyKeyDown) yield break;
                if (TVNav.Focused != want) TVNav.FocusButton(want);
                yield return null;
            }
        }
    }
}
