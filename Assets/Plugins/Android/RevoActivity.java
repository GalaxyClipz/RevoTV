package com.karuzohikari.revotv;

import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;

import android.hardware.Sensor;
import android.os.Build;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;

import com.unity3d.player.UnityPlayerActivity;

/**
 * Revo's main screen: Unity's own activity plus two things done outside the engine.
 * 1. The graphics driver, chosen before the engine starts. Measured on the projector (2026-11-07): with a big custom theme,
 *    OpenGL ES uses ~128 MB less memory than Vulkan for ~3% fewer frames, and running out of memory caused the worst stalls;
 *    with the built-in look Vulkan is faster. The C# side (TVGfx) stores the choice; a "-force-..." test switch always wins.
 * 2. Holding Back in Photos: on Google TV the held Back is also the TV's own "go to its home screen" shortcut, which opens the
 *    stock launcher right after Revo came back. For a moment after returning from Photos, Revo takes the screen back
 *    (Android allows an app that was just on screen to do this).
 */
public class RevoActivity extends UnityPlayerActivity {
    private static final String PREFS = "revo_tv";
    private static final String KEY_GLES = "use_gles";
    private static final long RECLAIM_MS = 5000;
    private int reclaims;

    private long backFromPhotosAt;
    private final Handler handler = new Handler(Looper.getMainLooper());

    public static void setGles(Context c, boolean on) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY_GLES, on).commit();
    }

    public static boolean usesGles(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(KEY_GLES, false);
    }

    @Override
    protected String updateUnityCommandLineArguments(String cmdLine) {
        if (cmdLine != null && cmdLine.contains("-force-")) return cmdLine; // a test switch decides
        if (!usesGles(this)) return cmdLine;
        return cmdLine == null || cmdLine.isEmpty() ? "-force-gles30" : cmdLine + " -force-gles30";
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        me = this;
        noteIntent(getIntent());
        showSplash();
    }

    // ---- start-up cover: the engine needs a few seconds (much longer right after the TV boots) before its first frame, which
    // looked like a dead black screen. Revo's banner sits on top until the menu is drawn (C#: TVPerf calls hideSplash).
    private android.view.View splash;
    private void showSplash() {
        try {
            // drawn live (no video file): smooth at the screen's own rate, loops forever, sized for a TV across the room
            // the room and the words are still; the ring and the dots move on Android's render thread (AnimatedVectorDrawable),
            // so they stay smooth even while the engine keeps the app's own thread busy (it looked like 5 frames a second)
            // the activity is set up without hardware drawing (Unity's default): the cover's animations then ran on the busy app
            // thread in software (choppy). The window gets it before it is first shown; the engine's own surface is unaffected
            getWindow().setFlags(android.view.WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED, android.view.WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
            android.widget.FrameLayout f = new android.widget.FrameLayout(this);
            f.addView(new LoadingView(this), new android.widget.FrameLayout.LayoutParams(-1, -1));
            android.util.DisplayMetrics dm = getResources().getDisplayMetrics();
            float u = Math.min(dm.heightPixels, dm.widthPixels * 9f / 16f);
            int ring = (int) (u * 0.155f * 2f * 100f / 84f);
            addSpin(f, "revotv_ring_anim", ring, ring, (int) (dm.heightPixels * 0.42f - ring / 2f));
            int dw = (int) (u * 0.032f * 7f), dh = dw * 20 / 140;
            addSpin(f, "revotv_dots_anim", dw, dh, (int) (dm.heightPixels * 0.765f - dh / 2f));
            addContentView(f, new android.view.ViewGroup.LayoutParams(-1, -1));
            splash = f;
            handler.postDelayed(RevoActivity::hideSplash, 120000); // never stays forever
        } catch (Throwable ignored) { }
    }
    private void addSpin(android.widget.FrameLayout f, String res, int w, int h, int top) {
        int id = getResources().getIdentifier(res, "drawable", getPackageName());
        if (id == 0) return;
        android.widget.ImageView v = new android.widget.ImageView(this);
        android.graphics.drawable.Drawable d = getDrawable(id);
        v.setImageDrawable(d);
        android.widget.FrameLayout.LayoutParams lp = new android.widget.FrameLayout.LayoutParams(w, h, android.view.Gravity.CENTER_HORIZONTAL | android.view.Gravity.TOP);
        lp.topMargin = top;
        f.addView(v, lp);
        if (d instanceof android.graphics.drawable.Animatable) ((android.graphics.drawable.Animatable) d).start();
    }

    /** Revo's start-up screen: soft white-blue room, a glowing ring with a light that circles it, "Revo", and a wave of dots. */
    private static final class LoadingView extends android.view.View {
        private final android.graphics.Paint p = new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        private final long t0 = SystemClock.uptimeMillis();
        private android.graphics.Shader bg, glow;
        private int bw, bh;
        LoadingView(Context c) { super(c); }

        @Override protected void onDraw(android.graphics.Canvas c) {
            int w = getWidth(), h = getHeight();
            if (w == 0 || h == 0) return;
            float t = (SystemClock.uptimeMillis() - t0) / 1000f;
            float cx = w / 2f, cy = h * 0.42f, u = Math.min(h, w * 9f / 16f);
            if (bw != w || bh != h) {
                bw = w; bh = h;
                bg = new android.graphics.LinearGradient(0, 0, 0, h, new int[] { 0xFFF4F9FE, 0xFFDDEBF7, 0xFFC4D9EE }, new float[] { 0f, 0.6f, 1f }, android.graphics.Shader.TileMode.CLAMP);
                glow = new android.graphics.RadialGradient(cx, cy, u * 0.42f, new int[] { 0xCCFFFFFF, 0x44FFFFFF, 0x00FFFFFF }, new float[] { 0f, 0.55f, 1f }, android.graphics.Shader.TileMode.CLAMP);
            }
            p.setStyle(android.graphics.Paint.Style.FILL);
            p.setColor(0xFFFFFFFF); // a shader still uses the paint's alpha (the last dot's): the room came out grey
            p.setShader(bg); c.drawRect(0, 0, w, h, p);
            // soft floor and side walls, like the console menus' rooms
            p.setShader(null);
            p.setColor(0x22FFFFFF); c.drawOval(cx - w * 0.42f, h * 0.70f, cx + w * 0.42f, h * 0.98f, p);
            p.setStyle(android.graphics.Paint.Style.STROKE); p.setStrokeWidth(u * 0.035f); p.setColor(0x1C3C8FD6);
            c.drawArc(-w * 0.18f, -h * 0.1f, w * 0.16f, h * 1.1f, -70, 140, false, p);
            c.drawArc(w * 0.84f, -h * 0.1f, w * 1.18f, h * 1.1f, 110, 140, false, p);
            p.setStyle(android.graphics.Paint.Style.FILL);
            p.setColor(0xFFFFFFFF);
            p.setShader(glow); c.drawCircle(cx, cy, u * 0.42f, p); p.setShader(null);

            // words
            p.setStyle(android.graphics.Paint.Style.FILL);
            p.setTextAlign(android.graphics.Paint.Align.CENTER);
            p.setTypeface(android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.BOLD));
            p.setTextSize(u * 0.085f); p.setColor(0xFF6B7584);
            c.drawText("Revo", cx, cy + u * 0.03f, p);
            p.setTypeface(android.graphics.Typeface.create("sans-serif", android.graphics.Typeface.NORMAL));
            p.setTextSize(u * 0.038f); p.setColor(0xFF5F6D80);
            c.drawText("Loading...", cx, h * 0.70f, p);

        }
    }

    public static void hideSplash() {
        final RevoActivity a = me;
        if (a == null) return;
        a.runOnUiThread(() -> {
            final android.view.View s = a.splash;
            if (s == null) return;
            a.splash = null;
            s.animate().alpha(0f).setDuration(250).withEndAction(() -> {
                try { ((android.view.ViewGroup) s.getParent()).removeView(s); } catch (Throwable ignored) { }
            }).start();
        });
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        noteIntent(intent);
    }

    private void noteIntent(Intent i) {
        if (i != null && i.getBooleanExtra("homeKey", false)) { i.removeExtra("homeKey"); if (hasWindowFocus()) homePressed = true; }
        if (i != null && i.getBooleanExtra("fromPhotos", false)) {
            backFromPhotosAt = SystemClock.elapsedRealtime(); reclaims = 0;
            i.removeExtra("fromPhotos");
        }
    }

    // Home pressed while the TV showed an HDMI input: Revo came up, then the TV's input player put the HDMI picture straight back
    // over it (seen while Revo was still loading). Right after Revo comes to the front, if the HDMI player takes over without the
    // person touching Revo, Revo takes the screen back (twice at most).
    private long frontAt, touchedAt; private int hdmiReclaims;
    @Override
    public void onUserInteraction() { super.onUserInteraction(); touchedAt = SystemClock.elapsedRealtime(); }

    @Override
    protected void onPause() {
        super.onPause();
        final long now = SystemClock.elapsedRealtime();
        if (now - frontAt < 15000 && now - touchedAt > 2500 && hdmiReclaims < 2 && !isFinishing()) {
            handler.postDelayed(() -> {
                String f = HomeService.front();
                if (f == null || !f.contains("inputplayer") && !f.contains("tvinput") && !f.contains("passthrough")) return;
                hdmiReclaims++;
                try {
                    Intent again = new Intent(this, RevoActivity.class);
                    again.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
                    again.putExtra("resume", true);
                    startActivity(again);
                } catch (Exception ignored) { }
            }, 700);
        }
        if (SystemClock.elapsedRealtime() - backFromPhotosAt < RECLAIM_MS && !isFinishing() && reclaims < 3) {
            reclaims++;
            // the TV's own home screen pushed in right after Photos: come straight back to the front
            handler.postDelayed(() -> {
                try {
                    Intent again = new Intent(this, RevoActivity.class);
                    again.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
                    again.putExtra("resume", true);
                    startActivity(again);
                } catch (Exception ignored) {
                }
            }, 250);
        }
    }

    // ---- controller setup (TVControllers): while capturing, the next button press or stick push on any device is recorded
    // ("descriptor|Button|96" or "descriptor|Axis|0|+") and kept from the menu. Back cancels ("cancel").
    private static volatile boolean capturing;
    private static volatile String captured;
    private static long captureFrom;
    // Android 13+ hands the Back key to "back callbacks" before any key handling: while listening, Revo's own callback turns it into "cancel"
    private static RevoActivity me;
    private static Object backCallback;
    private static void listenForBack(final boolean on) {
        final RevoActivity a = me;
        if (a == null || Build.VERSION.SDK_INT < 33) return;
        a.runOnUiThread(() -> {
            try {
                android.window.OnBackInvokedDispatcher d = a.getOnBackInvokedDispatcher();
                if (backCallback != null) { d.unregisterOnBackInvokedCallback((android.window.OnBackInvokedCallback) backCallback); backCallback = null; }
                if (on) {
                    android.window.OnBackInvokedCallback cb = () -> { if (capturing) { capturing = false; captured = "cancel"; listenForBack(false); } };
                    d.registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_OVERLAY, cb);
                    backCallback = cb;
                }
            } catch (Throwable ignored) { }
        });
    }
    public static void startCapture() { captured = null; captureFrom = SystemClock.uptimeMillis() + 250; capturing = true; listenForBack(true); }
    public static void stopCapture() { capturing = false; listenForBack(false); }
    /** Keys that belong to the TV: they are never offered as game buttons. */
    public static boolean isTvKey(int kc) {
        switch (kc) {
            case KeyEvent.KEYCODE_HOME: case KeyEvent.KEYCODE_VOLUME_UP: case KeyEvent.KEYCODE_VOLUME_DOWN: case KeyEvent.KEYCODE_VOLUME_MUTE:
            case KeyEvent.KEYCODE_MUTE: case KeyEvent.KEYCODE_POWER: case KeyEvent.KEYCODE_SETTINGS: case KeyEvent.KEYCODE_TV_INPUT:
            case KeyEvent.KEYCODE_GUIDE: case KeyEvent.KEYCODE_SEARCH: case KeyEvent.KEYCODE_ASSIST: case KeyEvent.KEYCODE_VOICE_ASSIST:
            case KeyEvent.KEYCODE_TV_POWER: case KeyEvent.KEYCODE_SLEEP: case KeyEvent.KEYCODE_WAKEUP: case KeyEvent.KEYCODE_APP_SWITCH:
            case KeyEvent.KEYCODE_ALL_APPS: case KeyEvent.KEYCODE_BRIGHTNESS_UP: case KeyEvent.KEYCODE_BRIGHTNESS_DOWN: case KeyEvent.KEYCODE_NOTIFICATION:
                return true;
        }
        return false;
    }

    public static String pollCapture() { String c = captured; if (c != null) captured = null; return c; }

    // the device the last arrow / OK press came from (a TV can list two "remotes": the one really in use is this one)
    private static volatile String lastRemote;
    public static String lastRemote() { return lastRemote; }
    // the remote's Home pressed while Revo was already on screen (Revo's TV helper sends it here): the menu goes to page 1
    private static volatile boolean homePressed;
    public static boolean consumeHome() { boolean h = homePressed; homePressed = false; return h; }

    // a key pressed before Revo came to the front (the Back held to leave Photos, however long it stays held): its repeats and
    // its release belong to the screen it was pressed on, never to the menu (they could press whatever got focus, like HDMI)
    private long resumedAt;

    @Override
    protected void onResume() {
        super.onResume();
        resumedAt = SystemClock.uptimeMillis();
        long nowE = SystemClock.elapsedRealtime();
        if (nowE - frontAt > 20000) hdmiReclaims = 0;
        frontAt = nowE;
    }

    // Revo is the TV's home screen: Back never sends it away (Android's default for an unused Back is to put the app behind the
    // last one, which was the HDMI input). Revo's own Back handling (closing menus, pages) comes through the key itself.
    @Override
    public void onBackPressed() {
        android.util.Log.w("RevoGuard", "Back reached the activity: ignored (Revo stays the home screen)");
    }

    // nothing may push the home screen behind another app (Unity's own Back handling, or the system's default for an
    // unused Back, put Revo behind the HDMI input right after Photos closed)
    @Override
    public boolean moveTaskToBack(boolean nonRoot) {
        android.util.Log.w("RevoGuard", "moveTaskToBack refused", new Throwable());
        return false;
    }

    // Android's own Back handling (Activity$RequestFinishCallback -> finishAfterTransition, it skips onBackPressed) closed Revo
    // right after Photos and the TV showed HDMI. Revo is the home screen: it is never closed that way. A restart for a theme or
    // look ends the process itself (AndroidLinker.RestartApp kills it right after).
    @Override
    public void finish() {
        android.util.Log.w("RevoGuard", "finish refused (home screen)", new Throwable());
    }

    @Override
    public void finishAfterTransition() {
        android.util.Log.w("RevoGuard", "finishAfterTransition refused (home screen)");
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int kc = e.getKeyCode();
        if (!capturing && e.getDownTime() < resumedAt) return true;
        if (e.getAction() == KeyEvent.ACTION_DOWN && e.getDevice() != null && (kc == KeyEvent.KEYCODE_DPAD_CENTER || (kc >= KeyEvent.KEYCODE_DPAD_UP && kc <= KeyEvent.KEYCODE_DPAD_RIGHT)))
            lastRemote = e.getDevice().getDescriptor();
        if (capturing && isTvKey(kc)) return super.dispatchKeyEvent(e); // the TV keeps its own keys
        if (capturing && (kc == KeyEvent.KEYCODE_BACK || kc == KeyEvent.KEYCODE_ESCAPE)) {
            if (e.getAction() == KeyEvent.ACTION_DOWN) { capturing = false; captured = "cancel"; listenForBack(false); }
            return true;
        }
        if (capturing) {
            InputDevice d = e.getDevice();
            if (e.getAction() == KeyEvent.ACTION_DOWN && e.getRepeatCount() == 0 && SystemClock.uptimeMillis() >= captureFrom && d != null) {
                capturing = false; listenForBack(false);
                captured = d.getDescriptor() + "|Button|" + e.getKeyCode();
            }
            return true;
        }
        return super.dispatchKeyEvent(e);
    }

    @Override
    public boolean dispatchGenericMotionEvent(MotionEvent e) {
        if (capturing && SystemClock.uptimeMillis() >= captureFrom && e.getDevice() != null && (e.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) {
            for (InputDevice.MotionRange r : e.getDevice().getMotionRanges()) {
                int ax = r.getAxis(); float v = e.getAxisValue(ax);
                if (Math.abs(v) > 0.65f && r.getRange() > 0.5f) {
                    capturing = false; listenForBack(false);
                    captured = e.getDevice().getDescriptor() + "|Axis|" + ax + "|" + (v > 0 ? "+" : "-");
                    return true;
                }
            }
            return true;
        }
        return super.dispatchGenericMotionEvent(e);
    }

    /** One line per remote / controller: descriptor | name | kind (remote, pad) | motion (1/0) | axes (comma list) | keys present (comma list of checked codes). */
    public static String[] inputDevices() {
        java.util.ArrayList<String> out = new java.util.ArrayList<>();
        java.util.HashSet<String> seen = new java.util.HashSet<>();
        int[] probe = { 19, 20, 21, 22, 23, 66, 82, 96, 97, 99, 100, 102, 103, 104, 105, 108, 109, 8, 9, 10, 11, 12, 13, 166, 167, 183, 184, 185, 186 };
        for (int id : InputDevice.getDeviceIds()) {
            InputDevice d = InputDevice.getDevice(id);
            if (d == null || d.isVirtual()) continue;
            String name = d.getName() == null ? "" : d.getName(), low = name.toLowerCase();
            if (low.contains("virtual") || low.contains("touchpad") || low.contains("keypad") || low.contains("camera") || low.contains("uinput")) continue;
            int src = d.getSources();
            boolean pad = (src & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD || (src & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
            boolean dpad = (src & InputDevice.SOURCE_DPAD) == InputDevice.SOURCE_DPAD;
            if (!pad && !dpad) continue;
            if (!seen.add(d.getDescriptor())) continue;
            boolean motion = false;
            if (Build.VERSION.SDK_INT >= 31) {
                try { motion = !d.getSensorManager().getSensorList(Sensor.TYPE_GYROSCOPE).isEmpty() || !d.getSensorManager().getSensorList(Sensor.TYPE_ACCELEROMETER).isEmpty(); } catch (Throwable t) { }
            }
            StringBuilder axes = new StringBuilder();
            for (InputDevice.MotionRange r : d.getMotionRanges()) if ((r.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) axes.append(axes.length() > 0 ? "," : "").append(r.getAxis());
            StringBuilder keys = new StringBuilder();
            boolean[] has = d.hasKeys(probe);
            for (int i = 0; i < probe.length; i++) if (has[i]) keys.append(keys.length() > 0 ? "," : "").append(probe[i]);
            boolean gamepadKeys = keys.toString().contains("96");
            String kind = pad && (gamepadKeys || axes.length() > 0) && (src & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD ? "pad" : "remote";
            out.add(d.getDescriptor() + "|" + name.replace("|", " ") + "|" + kind + "|" + (motion ? 1 : 0) + "|" + axes + "|" + keys);
        }
        return out.toArray(new String[0]);
    }

    public static String keyName(int code) { String s = KeyEvent.keyCodeToString(code); return s == null ? ("Key " + code) : s; }
    public static String axisName(int axis) { String s = MotionEvent.axisToString(axis); return s == null ? ("Axis " + axis) : s; }
}
