package com.karuzohikari.revotv;

import android.app.Activity;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.media.MediaPlayer;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

/**
 * The Photo Channel: Google Photos' own website inside Revo, so no browser app is needed. The remote drives a pointer:
 * arrows move it (and scroll the page at the screen's edge), OK clicks, holding OK right-clicks, Back goes back a page
 * and holding Back leaves. Menu turns the music on or off. Nothing is kept on the TV except the sign-in cookie: the page
 * cache is turned off and emptied on leaving.
 */
public class PhotosActivity extends Activity {
    static final String EXTRA_URL = "url";
    static final String EXTRA_VOLUME = "volume";
    private static final String DEFAULT_URL = "https://www.icloud.com/photos/";
    // a current desktop Chrome, so Google serves the full Photos site (and doesn't refuse sign-in as an "embedded view")
    private static final String UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    private static final long HOLD_MS = 650, BACK_HOLD_MS = 1100;

    private WebView web;
    private ProgressBar bar;
    private PointerView pointer;
    private MediaPlayer music;
    private float volume = 0.3f;
    private boolean musicOn = true;
    private float px, py;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean holdFired;
    private boolean scrollMode;
    private TextView help;
    private TextView arrowFlash; // a quick arrow on screen the moment Left/Right is pressed (the page itself can take a while to turn)
    private long lastArrowAt; private boolean arrowForwarded;
    // the photo page changes its address the moment it turns to the next photo: until it has (or 0.8 s), another press is not
    // passed on, so pressing again on a slow turn no longer skips a photo
    private volatile long turnedAt; private long turnAsked;
    private boolean turning(long t) { return turnAsked > turnedAt && t - turnAsked < 800; }
    private final Runnable hideHelp = new Runnable() {
        @Override
        public void run() { if (help != null) help.setVisibility(View.GONE); }
    };
    private Runnable holdTask;

    /** The pointer: a white dot with a dark ring, drawn over the page. */
    private static class PointerView extends View {
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG), ring = new Paint(Paint.ANTI_ALIAS_FLAG);
        float x, y;
        boolean scroll;

        PointerView(Context c) {
            super(c);
            fill.setColor(Color.WHITE);
            ring.setColor(Color.argb(230, 20, 40, 70));
            ring.setStyle(Paint.Style.STROKE);
            ring.setStrokeWidth(5f);
        }

        @Override
        protected void onDraw(Canvas c) {
            float r = getResources().getDisplayMetrics().density * 9f;
            if (scroll) {
                fill.setColor(0xFF2A8CFF);
                ring.setColor(Color.WHITE);
                c.drawCircle(x, y, r * 1.35f, fill);
                c.drawCircle(x, y, r * 1.35f, ring);
                float a = r * 0.6f; // up and down arrows
                Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
                p.setColor(Color.WHITE);
                android.graphics.Path up = new android.graphics.Path();
                up.moveTo(x, y - a * 1.1f); up.lineTo(x - a * 0.6f, y - a * 0.2f); up.lineTo(x + a * 0.6f, y - a * 0.2f); up.close();
                android.graphics.Path dn = new android.graphics.Path();
                dn.moveTo(x, y + a * 1.1f); dn.lineTo(x - a * 0.6f, y + a * 0.2f); dn.lineTo(x + a * 0.6f, y + a * 0.2f); dn.close();
                c.drawPath(up, p);
                c.drawPath(dn, p);
                fill.setColor(Color.WHITE);
                ring.setColor(Color.argb(230, 20, 40, 70));
                return;
            }
            c.drawCircle(x, y, r, fill);
            c.drawCircle(x, y, r, ring);
        }
    }

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);
        web = new WebView(this);
        web.setBackgroundColor(Color.BLACK);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setUserAgentString(UA);
        s.setCacheMode(WebSettings.LOAD_DEFAULT); // photos seen once come back from the cache (Left was slow: everything was fetched again)
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(true);
        s.setMediaPlaybackRequiresUserGesture(true);
        web.setFocusable(true);
        web.setFocusableInTouchMode(true);
        web.setWebViewClient(new WebViewClient() {
            @Override
            public void onPageFinished(WebView v, String url) {
                bar.setVisibility(View.GONE);
            }

            @Override
            public void doUpdateVisitedHistory(WebView v, String url, boolean reload) {
                long now = SystemClock.uptimeMillis();
                if (turnAsked > turnedAt) android.util.Log.d("PhotosNav", "turned in " + (now - turnAsked) + " ms");
                turnedAt = now;
            }
        });
        // smoother turning on a small TV chip: the page keeps its off-screen parts drawn and its renderer gets priority
        s.setOffscreenPreRaster(true);
        if (android.os.Build.VERSION.SDK_INT >= 26) web.setRendererPriorityPolicy(WebView.RENDERER_PRIORITY_IMPORTANT, false);
        web.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView v, int p) {
                bar.setProgress(p);
                bar.setVisibility(p >= 100 ? View.GONE : View.VISIBLE);
            }
        });
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, true);
        bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100);
        pointer = new PointerView(this);
        root.addView(web, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        root.addView(pointer, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        root.addView(bar, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, 8));
        help = new TextView(this);
        showHelp();
        help.setTextColor(0xFFFFFFFF);
        help.setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 10.5f);
        help.setLineSpacing(0f, 1.05f);
        int pad = (int) (getResources().getDisplayMetrics().density * 6);
        help.setPadding(pad * 2, pad, pad * 2, pad);
        help.setAlpha(0.88f);
        android.graphics.drawable.GradientDrawable bgd = new android.graphics.drawable.GradientDrawable();
        bgd.setColor(0xD9102040);
        bgd.setCornerRadius(pad * 1.2f);
        help.setBackground(bgd);
        FrameLayout.LayoutParams hlp = new FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT);
        hlp.leftMargin = pad; hlp.topMargin = pad;
        root.addView(help, hlp);
        arrowFlash = new TextView(this);
        arrowFlash.setTextColor(0xFFFFFFFF); arrowFlash.setTextSize(54f); arrowFlash.setShadowLayer(12f, 0f, 0f, 0xFF000000);
        arrowFlash.setAlpha(0f);
        FrameLayout.LayoutParams alp = new FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, android.view.Gravity.CENTER_VERTICAL | android.view.Gravity.END);
        alp.setMargins(48, 0, 48, 0);
        root.addView(arrowFlash, alp);
        setContentView(root);
        px = getResources().getDisplayMetrics().widthPixels / 2f;
        py = getResources().getDisplayMetrics().heightPixels / 2f;
        movePointer(0, 0);
        volume = getIntent().getFloatExtra(EXTRA_VOLUME, 0.3f);
        try {
            int id = getResources().getIdentifier("photos_music", "raw", getPackageName());
            if (id != 0 && volume > 0.01f) {
                music = MediaPlayer.create(this, id);
                if (music != null) {
                    music.setLooping(true);
                    music.setVolume(volume * 0.12f, volume * 0.12f);
                }
            }
        } catch (Exception ignored) {
        }
        String url = getIntent().getStringExtra(EXTRA_URL);
        web.loadUrl(url == null || url.isEmpty() ? DEFAULT_URL : url);
        Toast.makeText(this, "Instructions are in the top-left corner", Toast.LENGTH_LONG).show();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (music != null && musicOn) {
            try { music.start(); } catch (Exception ignored) { }
        }
    }

    @Override
    protected void onPause() {
        if (music != null) {
            try { if (music.isPlaying()) music.pause(); } catch (Exception ignored) { }
        }
        super.onPause();
    }

    // ---------------------------------------------------------------- help + scroll mode

    // the instructions stay in the top-left corner the whole time; they change with the mode
    private void showHelp() {
        if (help == null) return;
        help.setText(scrollMode
                ? "SCROLL MODE\nUp / Down  =  scroll\nLeft / Right  =  previous / next photo\nOK or Back  =  stop scrolling"
                : "OK  =  click      hold OK  =  scroll mode\nArrows  =  move the pointer\nBack  =  back      hold Back  =  leave");
        android.graphics.drawable.GradientDrawable g = new android.graphics.drawable.GradientDrawable();
        g.setColor(scrollMode ? 0xE60B6BD6 : 0xD9102040);
        g.setCornerRadius(getResources().getDisplayMetrics().density * 7f);
        help.setBackground(g);
    }

    private void setScrollMode(boolean on) {
        scrollMode = on;
        stopScroll();
        if (pointer != null) { pointer.scroll = on; pointer.invalidate(); }
        showHelp();
    }

    // ---------------------------------------------------------------- pointer

    private void movePointer(float dx, float dy) {
        int w = web.getWidth() > 0 ? web.getWidth() : getResources().getDisplayMetrics().widthPixels;
        int h = web.getHeight() > 0 ? web.getHeight() : getResources().getDisplayMetrics().heightPixels;
        float edge = 140f;
        // pushing against the screen's edge scrolls the page instead
        if (dy < 0 && py <= edge) scrollPage(-1);
        else if (dy > 0 && py >= h - edge) scrollPage(1);
        px = Math.max(0, Math.min(w - 1, px + dx));
        py = Math.max(0, Math.min(h - 1, py + dy));
        pointer.x = px; pointer.y = py;
        pointer.invalidate();
    }

    // Scrolling is the mouse wheel: while the pointer is pushed against the top or bottom edge (or Up / Down is held in scroll mode)
    // a small wheel turn is sent every frame at the middle of the page. The page's own smooth scrolling does the rest, so it is steady,
    // never flings past where you stopped, and moves whatever panel is under the middle (the page, or iCloud's inner photo panel).
    private int scrollDir;
    private long scrollUntil;
    private final Runnable scrollTick = new Runnable() {
        @Override
        public void run() {
            if (scrollDir == 0 || web == null) return;
            if (SystemClock.uptimeMillis() > scrollUntil) { scrollDir = 0; return; }
            wheel(scrollDir > 0 ? -0.5f : 0.5f);
            handler.postDelayed(this, 16);
        }
    };

    private void wheel(float amount) {
        int w = web.getWidth() > 0 ? web.getWidth() : getResources().getDisplayMetrics().widthPixels;
        int h = web.getHeight() > 0 ? web.getHeight() : getResources().getDisplayMetrics().heightPixels;
        MotionEvent.PointerProperties[] pp = { new MotionEvent.PointerProperties() };
        pp[0].id = 0;
        pp[0].toolType = MotionEvent.TOOL_TYPE_MOUSE;
        MotionEvent.PointerCoords[] pc = { new MotionEvent.PointerCoords() };
        pc[0].x = scrollMode ? px : w * 0.5f; // scroll mode turns whatever panel the pointer is over
        pc[0].y = scrollMode ? py : h * 0.55f;
        pc[0].setAxisValue(MotionEvent.AXIS_VSCROLL, amount);
        long t = SystemClock.uptimeMillis();
        MotionEvent ev = MotionEvent.obtain(t, t, MotionEvent.ACTION_SCROLL, 1, pp, pc, 0, 0, 1f, 1f, 0, 0, android.view.InputDevice.SOURCE_MOUSE, 0);
        web.dispatchGenericMotionEvent(ev);
        ev.recycle();
    }

    private void scrollPage(int dir) {
        long now = SystemClock.uptimeMillis();
        boolean fresh = scrollDir != dir || now > scrollUntil;
        scrollDir = dir;
        // the first push covers the key's repeat delay; after that each repeat keeps it going, and letting go stops it
        scrollUntil = now + (fresh ? 600 : 160);
        if (fresh) { handler.removeCallbacks(scrollTick); handler.post(scrollTick); }
    }

    private void stopScroll() { scrollDir = 0; handler.removeCallbacks(scrollTick); }

    private void tap() {
        long t = SystemClock.uptimeMillis();
        MotionEvent down = MotionEvent.obtain(t, t, MotionEvent.ACTION_DOWN, px, py, 0);
        MotionEvent up = MotionEvent.obtain(t, t + 60, MotionEvent.ACTION_UP, px, py, 0);
        down.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);
        up.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);
        web.dispatchTouchEvent(down);
        web.dispatchTouchEvent(up);
        down.recycle();
        up.recycle();
    }

    private void rightClick() {
        String js = "(function(x,y){var r=window.devicePixelRatio||1;x=x/r;y=y/r;var e=document.elementFromPoint(x,y);if(!e)return;"
                + "['mousedown','mouseup','contextmenu'].forEach(function(n){e.dispatchEvent(new MouseEvent(n,{bubbles:true,cancelable:true,"
                + "view:window,clientX:x,clientY:y,button:2,buttons:2}));});})(" + (int) px + "," + (int) py + ");";
        web.evaluateJavascript(js, null);
    }

    // ---------------------------------------------------------------- keys

    private void startHold(final boolean forBack) {
        holdFired = false;
        pendingLeave = false;
        cancelHold();
        holdTask = new Runnable() {
            @Override
            public void run() {
                holdFired = true;
                if (forBack) leave(); // Revo ignores a key that was already down when it came back, so no need to wait for the release
                else setScrollMode(!scrollMode);
            }
        };
        handler.postDelayed(holdTask, forBack ? BACK_HOLD_MS : HOLD_MS); // a short Back held a bit long used to count as "leave"
    }

    private void cancelHold() {
        if (holdTask != null) handler.removeCallbacks(holdTask);
        holdTask = null;
    }

    private void flashArrow(boolean left) {
        if (arrowFlash == null) return;
        FrameLayout.LayoutParams lp = (FrameLayout.LayoutParams) arrowFlash.getLayoutParams();
        lp.gravity = android.view.Gravity.CENTER_VERTICAL | (left ? android.view.Gravity.START : android.view.Gravity.END);
        arrowFlash.setLayoutParams(lp);
        arrowFlash.setText(left ? "\u25C0" : "\u25B6");
        arrowFlash.animate().cancel();
        arrowFlash.setAlpha(0.9f);
        arrowFlash.animate().alpha(0f).setStartDelay(250).setDuration(300).start();
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        int code = e.getKeyCode();
        boolean down = e.getAction() == KeyEvent.ACTION_DOWN;
        switch (code) {
            case KeyEvent.KEYCODE_BACK:
                if (down) {
                    if (e.getRepeatCount() == 0) startHold(true);
                    else if (e.isLongPress() && !holdFired) { holdFired = true; cancelHold(); leave(); } // the system's own long press
                } else {
                    boolean fired = holdFired;
                    cancelHold();
                    if (pendingLeave) leave(); // the held Back is let go: only now does Revo take the screen, so no stray Back reaches it
                    else if (!fired) { if (scrollMode) setScrollMode(false); else goBackOnePage(); }
                }
                return true;
            case KeyEvent.KEYCODE_DPAD_CENTER:
            case KeyEvent.KEYCODE_ENTER:
            case KeyEvent.KEYCODE_NUMPAD_ENTER:
                if (down) {
                    if (e.getRepeatCount() == 0) startHold(false);
                } else {
                    boolean fired = holdFired;
                    cancelHold();
                    if (!fired) { if (scrollMode) setScrollMode(false); else tap(); }
                }
                return true;
            case KeyEvent.KEYCODE_DPAD_UP:
            case KeyEvent.KEYCODE_DPAD_DOWN:
            case KeyEvent.KEYCODE_DPAD_LEFT:
            case KeyEvent.KEYCODE_DPAD_RIGHT:
                if (!down) stopScroll();
                if (scrollMode && (code == KeyEvent.KEYCODE_DPAD_LEFT || code == KeyEvent.KEYCODE_DPAD_RIGHT)) {
                    // looking at photos one by one: Left / Right are the page's own arrow keys (previous / next photo).
                    // One press = one photo: the remote's auto-repeat and a second press while the page is still turning are
                    // ignored (they used to skip two photos), and an arrow shows at once so the press is seen to have counted
                    long t = SystemClock.uptimeMillis();
                    if (down) {
                        // held down: one photo every 0.4 s, steadily, for as long as it's held (the photo's preview shows at once)
                        if (e.getRepeatCount() > 0) {
                            if (t - lastArrowAt < 250 || turning(t)) return true;
                            lastArrowAt = t; turnAsked = t; flashArrow(code == KeyEvent.KEYCODE_DPAD_LEFT);
                            if (web != null) { web.dispatchKeyEvent(new KeyEvent(t, t, KeyEvent.ACTION_DOWN, code, 0)); web.dispatchKeyEvent(new KeyEvent(t, t, KeyEvent.ACTION_UP, code, 0)); }
                            return true;
                        }
                        if (t - lastArrowAt < 60 || turning(t)) return true; // a bounce of the same press, or the last turn is still on its way
                        lastArrowAt = t; turnAsked = t; arrowForwarded = true;
                        flashArrow(code == KeyEvent.KEYCODE_DPAD_LEFT);
                    } else {
                        if (!arrowForwarded) return true;
                        arrowForwarded = false;
                    }
                    if (web != null) web.dispatchKeyEvent(new KeyEvent(t, t, down ? KeyEvent.ACTION_DOWN : KeyEvent.ACTION_UP, code, 0));
                } else if (down && scrollMode && (code == KeyEvent.KEYCODE_DPAD_UP || code == KeyEvent.KEYCODE_DPAD_DOWN)) {
                    scrollPage(code == KeyEvent.KEYCODE_DPAD_UP ? -1 : 1);
                } else if (down) {
                    float step = getResources().getDisplayMetrics().density * (10f + Math.min(e.getRepeatCount() * 4f, 44f)); // quicker off the mark and faster when held
                    movePointer(code == KeyEvent.KEYCODE_DPAD_LEFT ? -step : code == KeyEvent.KEYCODE_DPAD_RIGHT ? step : 0,
                            code == KeyEvent.KEYCODE_DPAD_UP ? -step : code == KeyEvent.KEYCODE_DPAD_DOWN ? step : 0);
                }
                return true;
            case KeyEvent.KEYCODE_MENU:
                if (!down) {
                    setScrollMode(!scrollMode);
                }
                return true;
            case KeyEvent.KEYCODE_PAGE_UP:
            case KeyEvent.KEYCODE_CHANNEL_UP:
                if (down) scrollPage(-1);
                return true;
            case KeyEvent.KEYCODE_PAGE_DOWN:
            case KeyEvent.KEYCODE_CHANNEL_DOWN:
                if (down) scrollPage(1);
                return true;
            case KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE:
                if (!down && music != null) {
                    musicOn = !musicOn;
                    try { if (musicOn) music.start(); else music.pause(); } catch (Exception ignored) { }
                    Toast.makeText(this, musicOn ? "Music on" : "Music off", Toast.LENGTH_SHORT).show();
                }
                return true;
            default:
                return super.dispatchKeyEvent(e);
        }
    }

    // if the system delivers Back as a gesture instead of a key, it still only goes back a page; leaving needs the key held
    @Override
    public void onBackPressed() {
        if (web != null && web.canGoBack()) { web.goBack(); return; }
        long now = SystemClock.uptimeMillis();
        if (now - lastBack < 1500) leave(); // never trapped: twice in a row leaves
        else { lastBack = now; Toast.makeText(this, "Press Back again (or hold it) to leave Photos", Toast.LENGTH_SHORT).show(); }
    }

    private long lastBack;

    // one page back: the real history if there is one, else the page's own history (sign-in frames keep it there)
    private void goBackOnePage() {
        // iCloud's photo viewer is one page with no history step of its own: when Back changed nothing, Escape closes it
        final long asked = SystemClock.uptimeMillis();
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (turnedAt >= asked || isFinishing()) return;
                long t = SystemClock.uptimeMillis();
                web.dispatchKeyEvent(new KeyEvent(t, t, KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_ESCAPE, 0));
                web.dispatchKeyEvent(new KeyEvent(t, t, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_ESCAPE, 0));
                android.util.Log.d("PhotosNav", "Back changed nothing: Escape sent");
            }
        }, 450);
        if (web.canGoBack()) { web.goBack(); return; }
        web.evaluateJavascript("(function(){if(history.length>1){history.back();return 1;}return 0;})()", new android.webkit.ValueCallback<String>() {
            @Override
            public void onReceiveValue(String v) {
                // nothing left to go back to: Back leaves Photos (holding Back is the TV's own shortcut to its home screen)
                if (!"1".equals(v)) leave();
            }
        });
    }

    private boolean pendingLeave;

    // holding Back: Photos shuts at once, but Revo is only brought back when the key is let go (a Back still held would reach Revo's
    // home screen and push it away, to the HDMI input); never waits longer than 8 s
    private void leaveWhenReleased() {
        pendingLeave = true;
        stopScroll();
        if (web != null) { web.stopLoading(); web.loadUrl("about:blank"); web.setVisibility(View.INVISIBLE); }
        handler.postDelayed(new Runnable() {
            @Override
            public void run() { if (pendingLeave) leave(); }
        }, 8000);
    }

    // Android may have closed Revo while Photos was open: bring it back explicitly instead of dropping to the system home
    private void leave() {
        pendingLeave = false;
        stopScroll();
        if (web != null) { web.stopLoading(); web.loadUrl("about:blank"); web.setVisibility(View.INVISIBLE); } // the page stops working at once
        try {
            boolean alive = false;
            android.app.ActivityManager am = (android.app.ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
            java.util.List<android.app.ActivityManager.RunningAppProcessInfo> procs = am != null ? am.getRunningAppProcesses() : null;
            if (procs != null) for (android.app.ActivityManager.RunningAppProcessInfo p : procs) if (getPackageName().equals(p.processName)) alive = true;
            android.content.Intent i = RevoTV.restartIntent(this); // as the home screen when Revo is the home (see restartIntent)
            if (i != null) {
                i.putExtra("resume", true); // a restarted Revo goes straight to its menu, no "press A" page
                i.putExtra("fromPhotos", true); // Revo takes the screen back if the TV's home screen pushes in (RevoActivity)
                // still running: just bring it forward. Gone: start it fresh (reordering a dead one dropped to the HDMI input instead)
                i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK | (alive ? android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT : android.content.Intent.FLAG_ACTIVITY_CLEAR_TASK));
                startActivity(i);
            }
        } catch (Exception ignored) {
        }
        finish();
    }

    @Override
    protected void onDestroy() {
        cancelHold();
        if (music != null) {
            try { music.release(); } catch (Exception ignored) { }
            music = null;
        }
        if (web != null) {
            try {
                web.stopLoading();
                web.clearCache(true);
                web.clearHistory();
                CookieManager.getInstance().flush();
                web.destroy();
            } catch (Exception ignored) {
            }
            web = null;
        }
        super.onDestroy();
    }
}
