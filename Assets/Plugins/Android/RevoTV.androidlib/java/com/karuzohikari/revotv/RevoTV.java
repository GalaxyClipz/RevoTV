package com.karuzohikari.revotv;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** Small helpers the launcher calls from Unity (AndroidJavaClass "com.karuzohikari.revotv.RevoTV"). */
public final class RevoTV {
    private static final String PREFS = "revo_tv";
    private static final String KEY_HOME = "home_button";

    private RevoTV() {
    }

    /** The Photo Channel: Google Photos' website in a screen of Revo's own. */
    public static boolean openPhotos(Activity a, float volume) {
        try {
            a.startActivity(new Intent(a, PhotosActivity.class).putExtra(PhotosActivity.EXTRA_VOLUME, volume));
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    // ---------------------------------------------------------------- Home button opens Revo

    public static void setHomeOn(Context c, boolean on) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY_HOME, on).apply();
    }

    public static boolean isHomeOn(Context c) {
        SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        return p.getBoolean(KEY_HOME, false);
    }

    /** True once the person has allowed "Revo Home button" in the TV's Accessibility settings. */
    public static boolean isHomeServiceAllowed(Context c) {
        String list = Settings.Secure.getString(c.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
        if (list == null) return false;
        ComponentName me = new ComponentName(c, HomeService.class);
        for (String s : list.split(":")) {
            ComponentName cn = ComponentName.unflattenFromString(s);
            if (me.equals(cn)) return true;
        }
        return false;
    }

    /** Opens the TV's Accessibility settings (falls back to Settings where a TV hides that page). */
    public static boolean openAccessibilitySettings(Activity a) {
        for (String action : new String[]{Settings.ACTION_ACCESSIBILITY_SETTINGS, Settings.ACTION_SETTINGS}) {
            try {
                a.startActivity(new Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                return true;
            } catch (Exception ignored) {
            }
        }
        return false;
    }

    /** Opens the system "Home app" chooser where the TV has one (phones, plain Android TV boxes). */
    public static boolean openHomeSettings(Activity a) {
        try {
            Intent i = new Intent(Settings.ACTION_HOME_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            if (i.resolveActivity(a.getPackageManager()) == null) return false;
            a.startActivity(i);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** The packages of every other home screen on this TV (the stock launcher, partner launchers). */
    static Set<String> otherHomes(Context c) {
        Set<String> out = new HashSet<>();
        Intent home = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME);
        List<ResolveInfo> list = c.getPackageManager().queryIntentActivities(home, PackageManager.MATCH_ALL);
        for (ResolveInfo r : list) {
            if (r.activityInfo != null && !c.getPackageName().equals(r.activityInfo.packageName)) out.add(r.activityInfo.packageName);
        }
        // the common TV homes, in case one doesn't answer the HOME query
        for (String s : new String[]{"com.google.android.tvlauncher", "com.google.android.apps.tv.launcherx",
                "com.google.android.leanbacklauncher", "com.amazon.tv.launcher", "com.amazon.firehomestarter", "com.android.tv.launcher"}) out.add(s);
        // the settings and setup screens are not homes
        out.remove("com.android.tv.settings");
        out.remove("com.android.settings");
        out.remove("com.google.android.tungsten.setupwraith");
        return out;
    }

    /** Revo's "home screen" entry (the activity-alias the Make Home / launcher switch turns on). */
    static ComponentName homeAlias(Context c) { return new ComponentName(c.getPackageName(), "com.unity3d.player.launcher"); }

    public static boolean isHomeAliasOn(Context c) {
        try {
            int st = c.getPackageManager().getComponentEnabledSetting(homeAlias(c));
            return st == PackageManager.COMPONENT_ENABLED_STATE_ENABLED;
        } catch (Throwable t) { return false; }
    }

    /** A TV whose own home app is switched off (and Revo's home entry is off too, e.g. right after Revo was reinstalled) only has
     *  Android's empty fallback screen for the Home button: pressing Home there showed a black screen that nothing could leave.
     *  Revo then makes itself the home screen. Returns true when it did. */
    public static boolean ensureHome(Context c) {
        try {
            if (isHomeAliasOn(c)) return false;
            Intent home = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME);
            android.content.pm.ResolveInfo r = c.getPackageManager().resolveActivity(home, PackageManager.MATCH_DEFAULT_ONLY);
            String name = r != null && r.activityInfo != null ? r.activityInfo.name : null;
            String pkg = r != null && r.activityInfo != null ? r.activityInfo.packageName : null;
            boolean fallback = name == null || name.endsWith("FallbackHome")
                    || "com.android.tv.settings".equals(pkg) || "com.android.settings".equals(pkg)
                    || "android".equals(pkg); // the "choose an app" box: no home picked
            if (!fallback) return false;
            // only when no real launcher is left at all (a chooser with a real launcher in it is the person's choice)
            List<ResolveInfo> all = c.getPackageManager().queryIntentActivities(home, 0);
            for (ResolveInfo ri : all) {
                if (ri.activityInfo == null) continue;
                String p = ri.activityInfo.packageName, n = ri.activityInfo.name;
                if (p.equals(c.getPackageName()) || n.endsWith("FallbackHome") || p.equals("com.android.tv.settings") || p.equals("com.android.settings")) continue;
                return false;
            }
            c.getPackageManager().setComponentEnabledSetting(homeAlias(c), PackageManager.COMPONENT_ENABLED_STATE_ENABLED, PackageManager.DONT_KILL_APP);
            return true;
        } catch (Throwable t) { return false; }
    }

    /** How Revo starts itself again (a new look, an update): as the home screen when it is the TV's home, so the next Home
     *  press finds this same Revo (a plain app start put it in a second place; Home then made another Revo and the engine,
     *  which can only run once, crashed back to the HDMI input). */
    public static Intent restartIntent(Context c) {
        try {
            Intent home = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME);
            android.content.pm.ResolveInfo r = c.getPackageManager().resolveActivity(home, PackageManager.MATCH_DEFAULT_ONLY);
            if (r != null && r.activityInfo != null && c.getPackageName().equals(r.activityInfo.packageName)) {
                home.setPackage(c.getPackageName());
                return home;
            }
        } catch (Throwable ignored) { }
        // Revo itself (not the app list entry, which would come straight back here)
        return new Intent(Intent.ACTION_MAIN).setComponent(new ComponentName(c.getPackageName(), "com.karuzohikari.revotv.RevoActivity"));
    }

    static Intent launchIntent(Context c) {
        PackageManager pm = c.getPackageManager();
        Intent i = pm.getLeanbackLaunchIntentForPackage(c.getPackageName());
        if (i == null) i = pm.getLaunchIntentForPackage(c.getPackageName());
        return i;
    }

    // ---------------------------------------------------------------- network

    /** The TV's own address on its Wi-Fi or wired network (IPv4), or null when it isn't connected. */
    public static String localIp(Context c) {
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager) c.getSystemService(Context.CONNECTIVITY_SERVICE);
            android.net.Network n = cm.getActiveNetwork();
            if (n == null) return null;
            android.net.LinkProperties lp = cm.getLinkProperties(n);
            if (lp == null) return null;
            for (android.net.LinkAddress la : lp.getLinkAddresses()) {
                java.net.InetAddress a = la.getAddress();
                if (a instanceof java.net.Inet4Address && !a.isLoopbackAddress()) return a.getHostAddress();
            }
        } catch (Exception ignored) {
        }
        return null;
    }

    // ---------------------------------------------------------------- device facts

    // one picture from a video (a few seconds in), saved as a jpg no wider than 1280: the still wallpaper for video themes
    public static boolean videoFrame(String videoPath, String outPath) {
        android.media.MediaMetadataRetriever r = new android.media.MediaMetadataRetriever();
        try {
            r.setDataSource(videoPath);
            android.graphics.Bitmap b = r.getFrameAtTime(2000000L, android.media.MediaMetadataRetriever.OPTION_CLOSEST_SYNC);
            if (b == null) b = r.getFrameAtTime();
            if (b == null) return false;
            int w = b.getWidth(), h = b.getHeight();
            if (w > 1280) b = android.graphics.Bitmap.createScaledBitmap(b, 1280, Math.max(1, h * 1280 / w), true);
            java.io.FileOutputStream o = new java.io.FileOutputStream(outPath);
            b.compress(android.graphics.Bitmap.CompressFormat.JPEG, 88, o);
            o.close();
            return true;
        } catch (Throwable t) {
            return false;
        } finally {
            try { r.release(); } catch (Throwable t) { }
        }
    }

    /** True on a 64-bit ARM or x86 device (Dolphin only runs there). */
    public static boolean is64Bit() {
        return Build.SUPPORTED_64_BIT_ABIS != null && Build.SUPPORTED_64_BIT_ABIS.length > 0;
    }

    /** The package name inside an APK file (null if it isn't a readable APK). */
    public static String apkPackage(Context c, String path) {
        try {
            android.content.pm.PackageInfo info = c.getPackageManager().getPackageArchiveInfo(path, 0);
            return info != null ? info.packageName : null;
        } catch (Exception e) {
            return null;
        }
    }

    /** True when the given app holds the permission (an app built for old Android never asks for its own). */
    public static boolean hasPermission(Context c, String pkg, String permission) {
        try {
            return c.getPackageManager().checkPermission(permission, pkg) == PackageManager.PERMISSION_GRANTED;
        } catch (Exception e) {
            return true;
        }
    }

    /** Opens an app's own page in the TV's Settings (Permissions is on it). */
    public static boolean openAppSettings(Activity a, String pkg) {
        try {
            a.startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + pkg))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** True when the installed app is signed like the installer file (false: it's another build of the same app). */
    @SuppressWarnings("deprecation")
    public static boolean sameSigner(Context c, String pkg, String apkPath) {
        try {
            PackageManager pm = c.getPackageManager();
            android.content.pm.Signature[] a = pm.getPackageInfo(pkg, PackageManager.GET_SIGNATURES).signatures;
            android.content.pm.PackageInfo f = pm.getPackageArchiveInfo(apkPath, PackageManager.GET_SIGNATURES);
            if (a == null || f == null || f.signatures == null || a.length == 0 || f.signatures.length == 0) return true;
            return a[0].equals(f.signatures[0]);
        } catch (Exception e) { return true; }
    }

    /** Android's own "uninstall this app?" question. */
    public static boolean askUninstall(Activity a, String pkg) {
        try { a.startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + pkg)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); return true; }
        catch (Exception e) { return false; }
    }

    public static boolean isInstalled(Context c, String pkg) {
        try {
            c.getPackageManager().getPackageInfo(pkg, 0);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    // ---------------------------------------------------------------- game picker (Disc Channel on 32-bit TVs)

    private static final String[] GAME_EXT = {".iso", ".gcm", ".gcz", ".wbfs", ".rvz", ".ciso", ".dol", ".elf"};
    private static volatile String[] scanResult;
    private static volatile boolean scanning;

    /** True when Revo may read every file on shared storage and USB drives (Android's "All files access"). */
    public static boolean hasAllFilesAccess() {
        if (Build.VERSION.SDK_INT >= 30) return android.os.Environment.isExternalStorageManager();
        return true;
    }

    /** Opens the TV's "All files access" page for Revo (falls back to the app's own settings page). */
    public static boolean openAllFilesAccess(Activity a) {
        try {
            if (Build.VERSION.SDK_INT >= 30) {
                a.startActivity(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                        Uri.parse("package:" + a.getPackageName())).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                return true;
            }
        } catch (Exception ignored) {
        }
        return openAppSettings(a, a.getPackageName());
    }

    /** Looks for GameCube / Wii disc images on a background thread: gameScanDone() / gameScanResult() give the answer. */
    public static void startGameScan() {
        if (scanning) return;
        scanning = true;
        scanResult = null;
        new Thread(new Runnable() {
            @Override
            public void run() {
                java.util.ArrayList<String> found = new java.util.ArrayList<>();
                try {
                    java.util.ArrayList<File> roots = new java.util.ArrayList<>();
                    roots.add(android.os.Environment.getExternalStorageDirectory());
                    File[] vols = new File("/storage").listFiles();
                    if (vols != null) {
                        for (File v : vols) {
                            String n = v.getName();
                            if (n.equals("emulated") || n.equals("self")) continue;
                            roots.add(v);
                        }
                    }
                    long stop = System.currentTimeMillis() + 8000;
                    for (File r : roots) walk(r, 0, found, stop);
                } catch (Throwable ignored) {
                }
                java.util.Collections.sort(found, new java.util.Comparator<String>() {
                    @Override
                    public int compare(String x, String y) {
                        return new File(x).getName().compareToIgnoreCase(new File(y).getName());
                    }
                });
                scanResult = found.toArray(new String[0]);
                scanning = false;
            }
        }, "Revo game scan").start();
    }

    private static void walk(File dir, int depth, java.util.List<String> out, long stop) {
        if (depth > 5 || out.size() >= 400 || System.currentTimeMillis() > stop) return;
        File[] list = dir.listFiles();
        if (list == null) return;
        for (File f : list) {
            String n = f.getName();
            if (n.startsWith(".")) continue;
            if (f.isDirectory()) {
                // other apps' private folders and the system's caches hold no games
                if (depth == 0 && (n.equals("Android") || n.equals("DCIM") || n.equals("Pictures") || n.equals("Movies") || n.equals("Music"))) continue;
                walk(f, depth + 1, out, stop);
            } else {
                String l = n.toLowerCase(java.util.Locale.ROOT);
                for (String e : GAME_EXT) {
                    if (l.endsWith(e)) { out.add(f.getAbsolutePath()); break; }
                }
            }
        }
    }

    public static boolean gameScanDone() {
        return !scanning && scanResult != null;
    }

    public static String[] gameScanResult() {
        String[] r = scanResult;
        return r != null ? r : new String[0];
    }

    /** Starts a game straight in the bundled emulator (its own game screen takes the path as "SelectedGame"). */
    public static String launchGame(Activity a, String emulatorPackage, String path) {
        try {
            Intent i = new Intent();
            i.setClassName(emulatorPackage, "org.dolphinemu.dolphinemu.emulation.EmulationActivity");
            i.putExtra("SelectedGame", path);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            a.startActivity(i);
            return "ok";
        } catch (Exception e) {
            return "error: " + e.getMessage();
        }
    }

    // ---------------------------------------------------------------- installing a bundled app

    /**
     * Asks Android to install an APK file (the person confirms on the system's own screen). Before that Android may
     * first ask to allow Revo to install apps: that opens its own settings page, and this returns "allow".
     */
    /** Asks Android TV's Settings app to restart the TV (its own "Restart" does this). False when there is no such app. */
    public static boolean restart(Activity a) {
        try {
            android.content.Intent i = new android.content.Intent("com.android.tv.settings.REBOOT_DEVICE");
            i.setPackage("com.android.tv.settings");
            i.addFlags(android.content.Intent.FLAG_RECEIVER_FOREGROUND);
            a.sendBroadcast(i);
            return true;
        } catch (Throwable t) { return false; }
    }

    public static String installApk(Activity a, String path) {
        try {
            if (Build.VERSION.SDK_INT >= 26 && !a.getPackageManager().canRequestPackageInstalls()) {
                Intent allow = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + a.getPackageName()));
                a.startActivity(allow.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                return "allow";
            }
            File f = new File(path);
            Uri uri = androidx.core.content.FileProvider.getUriForFile(a, a.getPackageName() + ".fileprovider", f);
            Intent i = new Intent(Intent.ACTION_VIEW);
            i.setDataAndType(uri, "application/vnd.android.package-archive");
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            a.startActivity(i);
            return "ok";
        } catch (Exception e) {
            return "error: " + e.getMessage();
        }
    }
}
