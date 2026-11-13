using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using ShellLink;
#endif
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Events;

public class AndroidLinker : MonoBehaviour
{
    public static AndroidJavaClass unityPlayer;
    public static AndroidJavaObject currentActivity;
    public static AndroidJavaObject packageManager;

    public static AndroidLinker _instance;
    private static AndroidJavaClass plugin;
    public static JavaPluginBridge pluginBridge;

    private double launchTime;
    private UnityAction relaunchEmulatorTask1;
    private UnityAction relaunchEmulatorTask2;

    private static string deletingApp;
    private static UnityAction deletedAppAction;
    
    public int numberOfDefaultApps;
    public List<AppInfo> apps = new List<AppInfo>();
    public Texture2D defaultIconTexture;
    private int cachedAppLength;

    public static AndroidJavaClass GetPlugin()
    {
        #if UNITY_ANDROID
        if (plugin == null)
        {
            plugin = new AndroidJavaClass("com.android.wiiplugin.Main");
        }
        #endif
        return plugin;
    }
    private void Awake()
    {
        _instance = this;
        
        #if UNITY_ANDROID
        if (Application.isMobilePlatform)
        {
            unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager");
        }
        DetectWrongLaunch();
        #endif
    }
    
    private void DetectWrongLaunch()
    {
        // we can't do the launcher version check because it'll be false in a fake /data/ launch!
        if (/*!PREFS.IsLauncherVersion.GetBool() || */!Application.isMobilePlatform)
        {
            return;
        }
        
        // apparently from a launcher, sometimes starting the app gives you a different persistent data path?
        // this might be risky.. but let's try
        string path = Application.persistentDataPath;
        Debug.Log("My current persistentDataPath is: " + path);
        if (path.StartsWith("/data/"))
        {
            // wrong place!
            Debug.Log("Wrong path!! Let's restart.");
            RestartApp();
        }
    }

    private void Start()
    {
        #if UNITY_ANDROID
        if (Application.isMobilePlatform)
        {
            pluginBridge = new JavaPluginBridge();
            pluginBridge.RegisterCallbacks();
        }
        #endif
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        Debug.Log("AndroidLinker: Called event! " + pauseStatus);
        
        // first we check if there's an app pending deletion
        if (!pauseStatus && !string.IsNullOrEmpty(deletingApp))
        {
            AppInfo info = GetAppInfo(deletingApp);
            if (info == null)
            {
                // app was deleted!
                deletedAppAction?.Invoke();
            }

            deletingApp = null;
            deletedAppAction = null;
        }
        
        // now we check for the emu tasks
        if (!pauseStatus && (relaunchEmulatorTask1 != null || relaunchEmulatorTask2 != null))
        {
            // we check if we need to relaunch or not
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            if (t.TotalSeconds - launchTime < 1f)
            {
                if (relaunchEmulatorTask1 != null)
                {
                    // we just opened the emulator and it brought us back here! we relaunch it
                    Debug.Log("EMU: Relaunching emulator!");
                    relaunchEmulatorTask1.Invoke();
                    relaunchEmulatorTask1 = null;
                }
                else if (relaunchEmulatorTask2 != null)
                {
                    // we already tried relaunching and it didn't work! let's refresh the app
                    Debug.Log("EMU: Relaunching emulator by opening regular app first!");
                    relaunchEmulatorTask2.Invoke();
                    relaunchEmulatorTask2 = null;
                }
            }
            else
            {
                Debug.Log("EMU: Timeout ended! We can null the task");
                relaunchEmulatorTask1 = null;
                relaunchEmulatorTask2 = null;
            }
        }
    }

    public static void UninstallApp(string packageName, UnityAction deletedAction)
    {
		#if UNITY_ANDROID
        // we save the actions in case the user actually deletes it
        deletingApp = packageName;
        deletedAppAction = deletedAction;
        
        // and we call the plugin to delete it
        GetPlugin().CallStatic("uninstallApp", currentActivity, packageName);
		#endif
    }

    public static List<string> GetPackageActivities(string packageName)
    {
		#if !UNITY_ANDROID
		packageName = null;
		#endif

        if (string.IsNullOrEmpty(packageName))
        {
            return new List<string>();
        }        
        return GetPlugin().CallStatic<string>("getPackageActivities", packageManager, packageName).Split(';').ToList();
    }

    public static void LaunchRetroArch(string package, string path, string core)
    {
        Debug.Log("EMU: Calling retroarch with content!");
        GetPlugin().CallStatic("launchRetroArch", currentActivity, package, path, core);
    }

    public static void LaunchEmulator(string package, string path, string activity, string extra, bool sendAsPath)
    {
        _instance.relaunchEmulatorTask1 = null;
        _instance.relaunchEmulatorTask2 = null;
        _instance.launchTime = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        
        _instance.relaunchEmulatorTask1 = () => { LaunchEmulatorWithContent(package, path, activity, extra, sendAsPath); };
        _instance.relaunchEmulatorTask2 = () =>
        {
            PopupController.ShowPopup("popup.emulatorbad");
        };
        _instance.relaunchEmulatorTask1.Invoke();
    }

    private static void LaunchEmulatorWithContent(string package, string path, string activity, string extra, bool sendAsPath)
    {
        Debug.Log("EMU: Calling launch with content!");
        GetPlugin().CallStatic("launchWithContent", packageManager, currentActivity, package, path, activity, extra, sendAsPath);
    }

    public static void LaunchApp(AppInfo appInfo, string activity = null, string data = null, string extra = null)
    {
        if (appInfo == null)
        {
            return;
        }
        
        // Now we check if it should be a different app
        string bundleId = appInfo.packageName;
        try
        {
            switch (bundleId)
            {
                case "wiiphone.contacts": case "revo.contacts":
                {
                    Application.OpenURL("https://getavataaars.com");
                    //OpenContacts();
                    break;
                }
                case "wiiphone.settings": case "revo.settings":
                {
                    _instance.OpenPhoneLauncherSettings();
                    break;
                }
                case "wiiphone.gallery": case "revo.gallery":
                {
                    AudioController.FadeOutHome();
                    OpenGallery();
                    break;
                }
                case "wiiphone.market": case "revo.market":
                {
                    OpenMarket();
                    break;
                }
                case "wiiphone.messaging": case "revo.messaging":
                {
                    OpenMessages();
                    break;
                }
                case "wiiphone.youtube": case "revo.youtube":
                {
                    OpenYoutube();
                    break;
                }
                case "revo.weather":
                {
                    Debug.Log("[Music] weather tile opened"); AudioController.FadeOutHome();
                    TVWeather.Show();
                    break;
                }
                case "wiiphone.disc": case "revo.disc":
                {
                    OpenDisc();
                    break;
                }
                default:
                {
                    LaunchApp(bundleId,activity,data,extra);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    public static void LaunchApp(string bundleId, bool openInPlayStore)
    {
        if (openInPlayStore)
        {
            LaunchApp(bundleId, actionIfNotAvailable: () =>
            {
                OpenInPlayStore(bundleId);
            });
        }
        else
        {
            LaunchApp(bundleId);
        }
    }

    public static void OpenInPlayStore(string bundleId)
    {
        Application.OpenURL("market://details?id=" + bundleId);
    }

    public static void LaunchApp(string bundleId, string activity = null, string data = null, string extra = null, Action actionIfNotAvailable = null)
    {
        if (string.IsNullOrEmpty(bundleId))
        {
            return;
        }

        if (!Application.isMobilePlatform)
        {
            if (File.Exists(bundleId))
            {
                System.Diagnostics.Process.Start(bundleId, data);
            }
            return;
        }

        if (!string.IsNullOrEmpty(activity) || !string.IsNullOrEmpty(data) || !string.IsNullOrEmpty(extra))
        {
            // not worth the trouble, let's delegate to the plugin
            GetPlugin().CallStatic("launchWithActivity", packageManager, currentActivity, bundleId, activity ?? "", data ?? "", extra ?? "");
        }
        else
        {
            // we launch it regularly directly from here

            AndroidJavaObject launchIntent = null;
            try
            {
                launchIntent = packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", bundleId);
                if (launchIntent == null)
                {
                    // TV-only apps only expose a leanback launcher
                    launchIntent = packageManager.Call<AndroidJavaObject>("getLeanbackLaunchIntentForPackage", bundleId);
                }
                currentActivity.Call("startActivity", launchIntent);
            }
            catch (Exception e)
            {
                Debug.Log("App not found! Perform action? " + (actionIfNotAvailable != null));
                actionIfNotAvailable?.Invoke();
            }

            launchIntent?.Dispose();
        }
    }

    public static void CheckPackagesPerm()
    {
        if (Application.isMobilePlatform)
        {
            if (!Permission.HasUserAuthorizedPermission("android.permission.QUERY_ALL_PACKAGES"))
            {
                Permission.RequestUserPermission("android.permission.QUERY_ALL_PACKAGES");
            }
        }
    }

    public void OpenQuickReview()
    {
        #if UNITY_ANDROID && false
        try
        {
            ReviewManager reviewManager = new ReviewManager();

            // start preloading the review prompt in the background
            var playReviewInfoAsyncOperation = reviewManager.RequestReviewFlow();

            // define a callback after the preloading is done
            playReviewInfoAsyncOperation.Completed += playReviewInfoAsync =>
            {
                if (playReviewInfoAsync.Error == ReviewErrorCode.NoError)
                {
                    // display the review prompt
                    var playReviewInfo = playReviewInfoAsync.GetResult();
                    reviewManager.LaunchReviewFlow(playReviewInfo);
                }
                else
                {
                    Debug.LogError("Error opening Quick Review!");
                    Application.OpenURL("market://details?id=com.karuzohikari.wiiphone");
                }
            };
        }
        catch (Exception e)
        {
            Application.OpenURL("market://details?id=com.karuzohikari.wiiphone");
        }
        #endif

        Application.OpenURL("market://details?id=com.karuzohikari.wiiphone");
    }

    public void RestartApp()
    {
        PlayerPrefs.Save();
        //PreferencesSerializer.Save();
        if (Application.isEditor)
        {
            return;
        }
        if (!Application.isMobilePlatform)
        {
            Application.Quit();
            return;
        }

        if (TVMode.IsTV) { TVGfx.Sync(); TVVideoWall.ClosingNormally(); } // the look that's starting decides the graphics driver (TVGfx)
        const int kIntent_FLAG_ACTIVITY_CLEAR_TASK = 0x00008000;
        const int kIntent_FLAG_ACTIVITY_NEW_TASK = 0x10000000;

        // todo should we use the launcher activity instead if the app is set as launcher? might not be needed since it's just an alias
        AndroidJavaObject intent = null;
        if (TVMode.IsTV) { try { using var rv = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV"); intent = rv.CallStatic<AndroidJavaObject>("restartIntent", currentActivity); } catch (Exception) { } }
        if (intent == null) intent = packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", Application.identifier);

        intent.Call<AndroidJavaObject>("putExtra", "restarted", true);
        intent.Call<AndroidJavaObject>("setFlags", kIntent_FLAG_ACTIVITY_NEW_TASK | kIntent_FLAG_ACTIVITY_CLEAR_TASK);
        currentActivity.Call("startActivity", intent);
        currentActivity.Call("finish");
        var process = new AndroidJavaClass("android.os.Process");
        int pid = process.CallStatic<int>("myPid");
        process.CallStatic("killProcess", pid);
    }
    
    public void SetLauncherVersion(bool isLauncher)
    {
        GetPlugin().CallStatic("setLauncherState", packageManager, AppController._instance.GetPackageName(), isLauncher);
        PREFS.IsLauncherVersion.SetBool(isLauncher);
    }

    public void OpenPhoneLauncherSettings()
    {
        LaunchIntent("android.settings.HOME_SETTINGS", true);
    }

    
    public void OpenPhoneGeneralSettings()
    {
        #if UNITY_IOS
        Application.OpenURL("prefs://");        
        #else
        LaunchIntent("android.settings.SETTINGS");
        #endif
    }

    public void OpenWifiSettings()
    {
        LaunchIntent("android.settings.WIFI_SETTINGS");
    }

    private static void LaunchIntent(string intentId, bool closeApp = false)
    {
        if (Application.isMobilePlatform)
        {
            try
            {
                AndroidJavaObject intentObject = new AndroidJavaObject("android.content.Intent", intentId);
                currentActivity.Call("startActivity", intentObject);
                if (closeApp)
                {
                    Application.Quit();
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
    }

    private static void OpenMarket()
    {
        #if UNITY_IOS
        Application.OpenURL("itms-apps://itunes.apple.com");
        #else
        // the device's own store first: Amazon's on Fire TV, otherwise Google Play, then the other makers' stores,
        // then whatever the system calls its store, then any app that opens market:// links
        bool amazon = false;
        try { using var build = new AndroidJavaClass("android.os.Build"); amazon = (build.GetStatic<string>("MANUFACTURER") ?? "").ToLowerInvariant().Contains("amazon"); }
        catch (Exception) { }
        var stores = new List<string> { "com.android.vending", "com.amazon.venezia", "com.huawei.appmarket", "com.sec.android.app.samsungapps", "com.xiaomi.mipicks", "com.xiaomi.market", "cm.aptoidetv.pt", "com.aurora.store" };
        if (amazon) { stores.Remove("com.amazon.venezia"); stores.Insert(0, "com.amazon.venezia"); }
        foreach (var store in stores)
        {
            if (TryLaunch(store)) return;
        }
        if (LaunchCategory("android.intent.category.APP_MARKET")) return;
        Application.OpenURL("market://search?q=apps");
        #endif
    }

    // starts a package's launcher (phone or TV) activity; false when it isn't installed or can't be opened
    private static bool TryLaunch(string bundleId)
    {
        if (!Application.isMobilePlatform) return false;
        try
        {
            using var intent = packageManager.Call<AndroidJavaObject>("getLeanbackLaunchIntentForPackage", bundleId)
                               ?? packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", bundleId);
            if (intent == null) return false;
            currentActivity.Call("startActivity", intent);
            return true;
        }
        catch (Exception e)
        {
            Debug.Log("[Launch] " + bundleId + " couldn't open: " + e.Message);
            return false;
        }
    }

    private static void OpenGallery()
    {
        #if UNITY_IOS
        Application.OpenURL("photos-redirect://");
        #else
        // a TV has no gallery app: the Photo Channel is Google Photos' website inside Revo
        if (TVMode.IsTV)
        {
            try
            {
                using var h = new AndroidJavaClass("com.karuzohikari.revotv.RevoTV");
                if (h.CallStatic<bool>("openPhotos", currentActivity, PREFS.MusicVolume.GetFloat())) return;
            }
            catch (System.Exception) { }
        }
        LaunchCategory("android.intent.category.APP_GALLERY");
        #endif
    }

    public static void OpenContacts()
    {
        LaunchCategory("android.intent.category.APP_CONTACTS");
    }
    
    public static void OpenMessages()
    {
        #if UNITY_IOS
        Application.OpenURL("messages://");
        #else
        LaunchCategory("android.intent.category.APP_MESSAGING");
        #endif
    }
    
    private static void OpenDisc()
    {
        #if UNITY_IOS
        Application.OpenURL("music://");
        #else
        TVDisc.Open(); // Dolphin itself, its bundled installer on 64-bit devices, or a plain "this TV can't run it"
        #endif
    }
    
    private static void OpenYoutube()
    {
        #if UNITY_IOS
        Application.OpenURL("youtube://");
        #else
        // We first try to launch Vanced cause, y'know
        LaunchApp("app.revanced.android.youtube", actionIfNotAvailable: () => {
            // Otherwise we launch regular YouTube
            // Android TV builds first (YouTube for TV, SmartTube), then the phone app
            LaunchApp("com.google.android.youtube.tv", actionIfNotAvailable: () => LaunchApp("com.teamsmart.videomanager.tv", actionIfNotAvailable: () => LaunchApp("com.google.android.youtube", true)));
        });
        #endif
    }

    private static bool LaunchCategory(string categoryId)
    {
        if (Application.isMobilePlatform)
        {
            try
            {
                AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent");
                AndroidJavaObject intentObject = intentClass.CallStatic<AndroidJavaObject>("makeMainSelectorActivity",
                    "android.intent.action.MAIN", categoryId);
                currentActivity.Call("startActivity", intentObject);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }
        return false;
    }
    
    // Regular launcher apps plus TV-only (leanback) apps, one entry per package
    private static List<AndroidJavaObject> QueryLauncherActivities()
    {
        var result = new List<AndroidJavaObject>();
        var seen = new HashSet<string>();
        foreach (string category in new[] { "android.intent.category.LAUNCHER", "android.intent.category.LEANBACK_LAUNCHER" })
        {
            AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MAIN");
            intent.Call<AndroidJavaObject>("addCategory", category);
            AndroidJavaObject list = packageManager.Call<AndroidJavaObject>("queryIntentActivities", intent, 0);
            int size = list.Call<int>("size");
            for (int i = 0; i < size; i++)
            {
                AndroidJavaObject info = list.Call<AndroidJavaObject>("get", i);
                string pkg = info.Get<AndroidJavaObject>("activityInfo").Get<string>("packageName");
                if (seen.Add(pkg))
                {
                    result.Add(info);
                }
            }
        }
        return result;
    }

    public bool IsAppCountAccurate()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // we check if it still matches the programs
        return cachedAppLength == GetWindowsPrograms().Count;
#endif
        
        if (!Application.isMobilePlatform || Application.isEditor)
        {
            return apps.Count > 0;
        }

        int count = QueryLauncherActivities().Count;
        return cachedAppLength == count;
    }
    
    public void FindAllApps()
    {
        try
        {
            
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // windows gets special treatment. we're gonna retrieve the start programs
            FindWindowsPrograms();
            if (cachedAppLength > 0)
            {
                return;   
            }
#endif
            
            if (!Application.isMobilePlatform || Application.isEditor)
            {
                // fake programs just to test
                if (apps.Count == 0)
                {
                    for (int i = 0; i < numberOfDefaultApps; i++)
                    {
                        string appName = "App nº" + i;
                        apps.Add(new AppInfo(appName, "com.test." + i, defaultIconTexture));
                    }
                }

                cachedAppLength = apps.Count;
                return;
            }

            // android programs
            List<AndroidJavaObject> packages = QueryLauncherActivities();
            int count = packages.Count;
            if (cachedAppLength == count)
            {
                // No changes have been made to the apps, no need to re-retrieve them
                return;
            }
            else
            {
                // We clear our current info because it's outdated
                apps.ForEach(app => app.Unload());
                apps.Clear();
            }

            cachedAppLength = count;
            int appNumber = 0;
            while (appNumber < count)
            {
                AndroidJavaObject currentObject = packages[appNumber];
                AppInfo appInfo = GetAppInfo(packageManager, currentObject);
                if (appInfo != null)
                {
                    apps.Add(appInfo);
                }

                appNumber++;
            }

            apps = apps.OrderBy(app => app.name).ToList();
            
            Debug.Log($"Finished adding apps! The number of system is: {amountOfSystem}");
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    private List<string> GetWindowsPrograms()
    {
        string allPath = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\";
        List<string> dirs = new List<string>(Directory.GetDirectories(allPath, "*", SearchOption.TopDirectoryOnly));
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft\Windows\Start Menu\Programs";
        dirs.AddRange(Directory.GetDirectories(roaming, "*", SearchOption.TopDirectoryOnly));
            
        List<string> programs = new List<string>();
        foreach (string s in dirs)
        {
            DirectoryInfo di = new DirectoryInfo(s);
            if (!di.Attributes.HasFlag(FileAttributes.ReparsePoint)) 
            {
                programs.AddRange(Directory.GetFiles(s, "*.lnk", SearchOption.AllDirectories));
            }
        }

        return programs;
    }

    private int amountOfSystem = 0;
    private AppInfo GetAppInfo(AndroidJavaObject pm, AndroidJavaObject resolveInfo)
    {
        // The object isn't application info, but rather ResolveInfo
        try
        {
            AndroidJavaObject applicationInfo = resolveInfo.Get<AndroidJavaObject>("activityInfo").Get<AndroidJavaObject>("applicationInfo");
            return GenerateAndroidAppInfo(pm, applicationInfo);
        }
        catch (Exception ignored)
        {
            Debug.LogError(ignored);
            return null;
        }
    }


    public AppInfo GetAppInfo(string packageName)
    {
        if (!Application.isMobilePlatform || Application.isEditor)
        {
            return null;
        }
        
        try
        {
            AndroidJavaObject pm = packageManager;
            AndroidJavaObject applicationInfo = pm.Call<AndroidJavaObject>("getApplicationInfo", packageName, 0);
            return GenerateAndroidAppInfo(pm, applicationInfo);
        }
        catch (Exception ignored)
        {
            return null;
        }
    }

    private AppInfo GenerateAndroidAppInfo(AndroidJavaObject pm, AndroidJavaObject applicationInfo)
    {
        if (pm == null || applicationInfo == null)
        {
            return null;
        }
        
        string packageName = applicationInfo.Get<string>("packageName");
        string labelName = pm.Call<string>("getApplicationLabel", applicationInfo);

        byte[] decodedBytes = GetPlugin().CallStatic<byte[]>("getIcon", pm, packageName);
        Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        texture.LoadImage(decodedBytes,true);

        return new AppInfo(labelName, packageName, texture);
    }

    
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private void FindWindowsPrograms()
    {
        // we iterate the path
        List<string> programs = GetWindowsPrograms();
        if (cachedAppLength == programs.Count)
        {
            // no new programs! no need to regenerate em
            return;
        }
        
        // we discard our app infos and create new ones
        apps.ForEach(app => app.Unload());
        apps.Clear();
        foreach (string program in programs)
        {
            // we have to retrieve the *actual* target of the program for the icon. stupid :')
            // edit not even that anymore
            string target = /*GetWindowsShortcutTarget(program)*/ program;
            if (!string.IsNullOrEmpty(target))
            {
                AppInfo info = GenerateWindowsAppInfo(program, target);
                if (info != null)
                {
                    apps.Add(info);
                }
            }
        }

        // and we order by name
        apps = apps.OrderBy(app => app.name).ToList();
    }

    private string GetWindowsShortcutTarget(string link)
    {
        try
        {
            string path = Shortcut.ReadFromFile(link).LinkTargetIDList.Path;
            Debug.Log(path);
            return path;
        }
        catch (Exception e)
        {
            // we ignore it
            return "";
        }
    }
    
    private AppInfo GenerateWindowsAppInfo(string path, string target)
    {
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
        {
            return null;
        }
        
        try
        {
            // we extract the icon
            // ok guess not
            /*byte[] buff;
            IconExtractor ie = new IconExtractor(path);
            Bitmap bmp = IconUtil.ToBitmap(ie.GetIcon(0));

            using (var stream = new MemoryStream())
            {
                bmp.Save(stream, ImageFormat.Png);
                buff = stream.ToArray();
                Debug.Log("Icon: " + buff.Length);
            }
            Texture2D texture = new Texture2D(1, 1);
            texture.LoadImage(buff);*/

            // and we return it
            AppInfo info = new AppInfo(Path.GetFileNameWithoutExtension(path), target, defaultIconTexture);
            info.isWindows = true;
            return info;
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            return null;
        }
    }
#endif
}