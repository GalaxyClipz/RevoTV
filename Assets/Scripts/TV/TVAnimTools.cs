using Animations;
using UnityEngine;

// TV helpers for channel animations (icons and banners): asking before one is put on a channel, and deleting one by holding OK on it
public static class TVAnimTools
{
    // the pictures that come with RevoTV (StreamingAssets/DefaultAnimations): they can't be deleted, new channels and resets use them
    private static readonly string[] BuiltIn = { "default", "discchannel", "photochannel", "revoshop", "riichannel", "youtubechannel", "tvcustom", "a4ec5432-418c-4122-aadd-d2023cd2730d" };

    private static string Core(string file)
    {
        file = (file ?? "").ToLowerInvariant();
        if (file.EndsWith(".zip")) file = file.Substring(0, file.Length - 4);
        int u = file.IndexOf('_'); // banner_ / icon_
        return u >= 0 ? file.Substring(u + 1) : file;
    }

    public static bool IsBuiltIn(string file)
    {
        string c = Core(file);
        foreach (var b in BuiltIn) if (c.StartsWith(b)) return true;
        return false;
    }

    // the file name if a channel on the home menu shows this picture (null if none does)
    public static string UsedBy(string file)
    {
        string c = (file ?? "").ToLowerInvariant().Replace(".zip", "");
        var cc = ChannelController._instance;
        if (cc == null) return null;
        foreach (var ch in cc.loadedChannels)
        {
            if (ch == null) continue;
            foreach (var n in new[] { ch.iconName, ch.bannerName, ch.iconAnimation?.GetImportedFileName(), ch.bannerAnimation?.GetImportedFileName() })
                if (!string.IsNullOrEmpty(n) && n.ToLowerInvariant().Replace(".zip", "") == c)
                    return n;
        }
        return null;
    }

    // hold OK on an animation in Settings > Channel Pictures: "Delete it?" (the More > Delete route still works too)
    public static void AskDelete(AnimationBasicInfo info, CHANNELTYPE type)
    {
        if (info == null) return;
        string file = info.animationFileName ?? "";
        if (IsBuiltIn(file)) { PopupController.ShowPopup("tv.anim.builtin", PopupController.ClosePopup, true, new[] { info.animationName }); return; }
        string used = UsedBy(file);
        if (used != null) { PopupController.ShowPopup("tv.anim.inuse", PopupController.ClosePopup, true, new[] { info.animationName }); return; }
        PopupController.ShowPopup("popup.confirmdeleteanim", () =>
        {
            var anim = ChannelController._instance.GetOrLoadChannelAnimation(file);
            ChannelController._instance.DeleteChannelAnimation(anim);
            Debug.Log("[Anim] deleted " + file);
            PopupController.ShowPopup("popup.deletedanim", () => SettingsController._instance.channelEditSettingView.Show(type), true, new[] { info.animationName });
        }, PopupController.ClosePopup, new[] { info.animationName });
    }
}
