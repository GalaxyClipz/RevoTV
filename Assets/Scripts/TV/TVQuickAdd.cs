using Data.ChannelTargets;
using UnityEngine;

/// <summary>
/// Remote-friendly way to put an installed app on the launcher: hold OK on an empty slot, then its Add App button opens
/// the installed-app list; picking one makes the channel in that slot at once (the app's own icon on the "Default Static" artwork).
/// The phone's flow (hold, choose a type, choose an app, then an icon and a banner) is still there through Settings.
/// </summary>
public static class TVQuickAdd
{
    private const string Icon = "icon_defaultstatic_1630688976077.zip";
    private const string Banner = "banner_defaultstatic_1630695836374.zip";
    private static Channel pending;

    public static void Open(int gridNumber, int position)
    {
        var gc = GridController._instance;
        if (!TVMode.IsTV || SettingsController._instance == null || gc == null) return;
        if (AppController.IsTransitioning() || gc.isChangingGrid || PopupController.IsPopupOpen()) return;
        if (ChannelController._instance == null || ChannelController._instance.currentChannel != null) return;
        var channel = new Channel { gridNumber = gridNumber, position = position, isFullySetup = false };
        var target = new ChannelAppTarget();
        channel.target = target;
        target.LinkChannel(channel);
        pending = channel;
        AudioController.PlaySoundEffect(AudioLibrary._instance.CLICK_BUTTON);
        SettingsController._instance.setupAppSettingView.Show(channel);
    }

    // called when an app is picked from the list: true when this was a quick add and the channel is now made
    public static bool Finish(Channel channel, AppInfo app)
    {
        if (channel == null || channel != pending || app == null) return false;
        var icon = ChannelController._instance.GetOrLoadChannelAnimation(Icon);
        var banner = ChannelController._instance.GetOrLoadChannelAnimation(Banner);
        pending = null;
        if (icon == null || banner == null) return false; // artwork missing: carry on with the normal setup steps
        channel.info = app;
        channel.SetIconAnimation(icon);
        channel.SetBannerAnimation(banner);
        channel.isFullySetup = true;
        ChannelController._instance.InitializeNewChannel(channel);
        if (channel.appHandler != null) TVNav.ReturnTo(channel.appHandler.transform); // home again, the light on the new tile
        SettingsController._instance.ExitSettings(true, false);
        Debug.Log("[QuickAdd] " + app.packageName + " placed on page " + channel.gridNumber + " slot " + channel.position);
        return true;
    }

    // Back from the list: nothing was made, just go home
    public static bool Cancel(Channel channel)
    {
        if (channel == null || channel != pending) return false;
        pending = null;
        // the home grid is hidden while Settings shows, so look through hidden objects too
        foreach (var eh in Object.FindObjectsByType<EmptyAppHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (eh.gridNumber == channel.gridNumber && eh.position == channel.position) { TVNav.ReturnTo(eh.transform); break; }
        SettingsController._instance.ExitSettings(true, false);
        return true;
    }
}
