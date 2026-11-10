package com.karuzohikari.revotv;

import android.accessibilityservice.AccessibilityService;
import android.app.ActivityManager;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.content.Intent;
import android.os.SystemClock;
import android.view.KeyEvent;
import android.view.accessibility.AccessibilityEvent;

import java.util.Set;

/**
 * "Make Home". Android TV, Google TV and Fire TV keep their own home screen no matter which app asks to be the
 * launcher. While the switch in Revo's Preferences is on, this service:
 *  - catches the remote's Home key itself and opens Revo instead (the way button mappers do it; every other key
 *    is passed on untouched), and
 *  - as a backup, when the TV's own home screen still comes to the front, brings Revo back over it (only the
 *    package name of the new window is read).
 * Does nothing while the switch is off.
 */
public class HomeService extends AccessibilityService {
    private long lastLaunch, burstStart; private boolean homeKey;
    private int burst;
    private Set<String> homes;

    @Override
    protected void onServiceConnected() {
        homes = null;
    }

    // ---- games: the emulator in front (Dolphin 32-bit fork or official Dolphin, any emulator package Revo knows)
    private String front = "";
    private long backDownAt = -1, injectedAt;
    private boolean backAsked;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private View quitView; private TextView[] quitButtons; private int quitPick;
    private static final long QUIT_HOLD_MS = 7000;

    private boolean inEmulator() {
        String f = front;
        return f.startsWith("org.dolphinemu") || f.contains("dolphin") || f.startsWith("com.retroarch") || f.contains(".ppsspp") || f.startsWith("org.citra") || f.contains("emulator");
    }

    private boolean gameKey(KeyEvent e) {
        if (quitView != null) {
            // the quit question is up: it takes the remote
            if (e.getAction() != KeyEvent.ACTION_DOWN) return true;
            int k = e.getKeyCode();
            if (k == KeyEvent.KEYCODE_DPAD_LEFT || k == KeyEvent.KEYCODE_DPAD_RIGHT || k == KeyEvent.KEYCODE_DPAD_UP || k == KeyEvent.KEYCODE_DPAD_DOWN) { quitPick = 1 - quitPick; paintQuit(); }
            else if (k == KeyEvent.KEYCODE_DPAD_CENTER || k == KeyEvent.KEYCODE_ENTER || k == KeyEvent.KEYCODE_BUTTON_A) { boolean q = quitPick == 1; hideQuit(); if (q) quitGame(); }
            else if (k == KeyEvent.KEYCODE_BACK || k == KeyEvent.KEYCODE_BUTTON_B) hideQuit();
            return true;
        }
        if (e.getKeyCode() != KeyEvent.KEYCODE_BACK || !inEmulator()) return false;
        if (SystemClock.uptimeMillis() - injectedAt < 400) return false; // our own re-sent Back: let it through
        if (e.getAction() == KeyEvent.ACTION_DOWN) {
            if (e.getRepeatCount() == 0) {
                backDownAt = e.getDownTime(); backAsked = false;
                ui.postDelayed(holdCheck, QUIT_HOLD_MS);
            }
            return true;
        }
        if (e.getAction() == KeyEvent.ACTION_UP) {
            ui.removeCallbacks(holdCheck);
            boolean quick = backDownAt >= 0 && e.getEventTime() - backDownAt < 600 && !backAsked;
            backDownAt = -1;
            // a quick press is the emulator's own Back (its menu), sent on as if it had never been caught
            if (quick) { injectedAt = SystemClock.uptimeMillis(); performGlobalAction(GLOBAL_ACTION_BACK); }
            return true;
        }
        return true;
    }

    private final Runnable holdCheck = new Runnable() {
        @Override public void run() { if (backDownAt >= 0 && !backAsked && inEmulator()) { backAsked = true; showQuit(); } }
    };

    private void showQuit() {
        if (quitView != null) return;
        try {
            float d = getResources().getDisplayMetrics().density;
            LinearLayout box = new LinearLayout(this); box.setOrientation(LinearLayout.VERTICAL); box.setGravity(Gravity.CENTER_HORIZONTAL);
            int pad = (int) (24 * d); box.setPadding(pad, pad, pad, pad);
            GradientDrawable bg = new GradientDrawable(); bg.setColor(Color.argb(240, 235, 240, 248)); bg.setCornerRadius(22 * d); bg.setStroke((int) (3 * d), Color.rgb(60, 170, 235));
            box.setBackground(bg);
            TextView t = new TextView(this); t.setText("Quit the game?"); t.setTextColor(Color.rgb(30, 34, 44)); t.setTextSize(26); t.setGravity(Gravity.CENTER);
            TextView s = new TextView(this); s.setText("Anything not saved in the game is lost."); s.setTextColor(Color.rgb(90, 96, 110)); s.setTextSize(15); s.setGravity(Gravity.CENTER);
            s.setPadding(0, (int) (6 * d), 0, (int) (16 * d));
            LinearLayout row = new LinearLayout(this); row.setOrientation(LinearLayout.HORIZONTAL); row.setGravity(Gravity.CENTER);
            quitButtons = new TextView[2];
            String[] names = { "Keep playing", "Quit to Revo" };
            for (int i = 0; i < 2; i++) {
                TextView b = new TextView(this); b.setText(names[i]); b.setTextSize(20); b.setGravity(Gravity.CENTER);
                b.setPadding((int) (26 * d), (int) (12 * d), (int) (26 * d), (int) (12 * d));
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
                lp.setMargins((int) (10 * d), 0, (int) (10 * d), 0);
                row.addView(b, lp); quitButtons[i] = b;
            }
            box.addView(t); box.addView(s); box.addView(row);
            quitPick = 0; paintQuit();
            WindowManager.LayoutParams p = new WindowManager.LayoutParams(WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
                    WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE, PixelFormat.TRANSLUCENT);
            p.gravity = Gravity.CENTER;
            ((WindowManager) getSystemService(WINDOW_SERVICE)).addView(box, p);
            quitView = box;
        } catch (Exception ignored) { quitView = null; }
    }

    private void paintQuit() {
        if (quitButtons == null) return;
        float d = getResources().getDisplayMetrics().density;
        for (int i = 0; i < 2; i++) {
            GradientDrawable g = new GradientDrawable(); g.setCornerRadius(30 * d);
            boolean on = i == quitPick;
            g.setColor(on ? Color.rgb(60, 170, 235) : Color.rgb(250, 251, 253)); g.setStroke((int) (2 * d), Color.rgb(160, 170, 185));
            quitButtons[i].setBackground(g); quitButtons[i].setTextColor(on ? Color.WHITE : Color.rgb(40, 44, 54));
        }
    }

    private void hideQuit() {
        if (quitView == null) return;
        try { ((WindowManager) getSystemService(WINDOW_SERVICE)).removeView(quitView); } catch (Exception ignored) { }
        quitView = null; quitButtons = null;
    }

    // back to Revo, then the emulator is closed once it's behind (it may only be closed while it isn't on screen)
    private void quitGame() {
        final String game = front;
        Intent i = RevoTV.launchIntent(this);
        if (i != null) {
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
            i.putExtra("resume", true);
            try { startActivity(i); } catch (Exception ignored) { }
        }
        ui.postDelayed(() -> {
            try { ((ActivityManager) getSystemService(ACTIVITY_SERVICE)).killBackgroundProcesses(game); } catch (Exception ignored) { }
        }, 1500);
    }

    @Override
    protected boolean onKeyEvent(KeyEvent e) {
        if (e == null) return false;
        if (gameKey(e)) return true;
        if (e.getKeyCode() != KeyEvent.KEYCODE_HOME || !RevoTV.isHomeOn(this)) return false;
        // the press is ours: the TV never sees it, so its own home screen doesn't open at all
        if (e.getAction() == KeyEvent.ACTION_UP && !e.isCanceled()) { homeKey = true; openRevo(false); homeKey = false; }
        return true;
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent e) {
        if (e == null || e.getEventType() != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return;
        CharSequence p = e.getPackageName();
        if (p != null && !p.toString().equals("com.android.systemui")) front = p.toString();
        if (p == null || !RevoTV.isHomeOn(this)) return;
        String pkg = p.toString();
        if (pkg.equals(getPackageName())) return;
        if (homes == null) homes = RevoTV.otherHomes(this);
        if (!homes.contains(pkg)) return;
        openRevo(true);
    }

    private void openRevo(boolean guarded) {
        long now = SystemClock.elapsedRealtime();
        if (guarded) {
            if (now - lastLaunch < 1500) return;
            // Revo closing again and again (a crash at start): leave the TV's own home alone instead of looping
            if (now - burstStart > 20000) { burstStart = now; burst = 0; }
            if (++burst > 4) return;
        }
        lastLaunch = now;

        Intent i = RevoTV.launchIntent(this);
        if (i == null) return;
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
        if (homeKey) i.putExtra("homeKey", true);
        try {
            startActivity(i);
        } catch (Exception ignored) {
        }
    }

    @Override
    public void onInterrupt() {
    }
}
