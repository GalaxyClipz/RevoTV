using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Switches an Android TV / projector to one of its HDMI passthrough inputs (HDMI 1 by default, set in Preferences).</summary>
public static class TVHdmi
{
    private const int TYPE_HDMI = 1007; // TvInputInfo.TYPE_HDMI

    private static bool? hasInput;

    /// <summary>True when the TV lists at least one HDMI passthrough input (asked once: inputs don't come and go).</summary>
    public static bool HasHdmiInput()
    {
        if (hasInput.HasValue) return hasInput.Value;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var tvm = AndroidLinker.currentActivity.Call<AndroidJavaObject>("getSystemService", "tv_input");
            bool found = false;
            if (tvm != null)
            {
                using var list = tvm.Call<AndroidJavaObject>("getTvInputList");
                int size = list.Call<int>("size");
                for (int i = 0; i < size && !found; i++)
                {
                    using var info = list.Call<AndroidJavaObject>("get", i);
                    found = info.Call<int>("getType") == TYPE_HDMI && info.Call<bool>("isPassthroughInput");
                }
            }
            hasInput = found;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HDMI] input check failed, keeping the button: " + e.Message);
            hasInput = true;
        }
#else
        hasInput = true;
#endif
        Debug.Log("[HDMI] HDMI input present: " + hasInput.Value);
        return hasInput.Value;
    }

    /// <returns>null on success, otherwise a short error message for the user.</returns>
    public static string LaunchHdmi()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            var activity = AndroidLinker.currentActivity;
            int port = Mathf.Clamp(PREFS.HdmiPort.GetInt(), 1, 8);
            using var tvm = activity.Call<AndroidJavaObject>("getSystemService", "tv_input");
            if (tvm == null) return "No TV input service";

            using var list = tvm.Call<AndroidJavaObject>("getTvInputList");
            int size = list.Call<int>("size");

            var ids = new List<string>();
            string byLabel = null;
            for (int i = 0; i < size; i++)
            {
                using var info = list.Call<AndroidJavaObject>("get", i);
                if (info.Call<int>("getType") != TYPE_HDMI) continue;
                if (!info.Call<bool>("isPassthroughInput")) continue; // skip CEC device entries

                string id = info.Call<string>("getId");
                ids.Add(id);

                string label = "";
                try
                {
                    using var cs = info.Call<AndroidJavaObject>("loadCustomLabel", activity)
                                   ?? info.Call<AndroidJavaObject>("loadLabel", activity);
                    label = cs?.Call<string>("toString") ?? "";
                }
                catch (Exception) { }

                // labels are normally "HDMI 1", "HDMI 2"...
                if (byLabel == null && label.Contains(port.ToString())) byLabel = id;
            }

            if (ids.Count == 0) return "No HDMI input found";
            ids.Sort(StringComparer.Ordinal);
            string inputId = byLabel ?? (port - 1 < ids.Count ? ids[port - 1] : ids[0]);

            using var tvContract = new AndroidJavaClass("android.media.tv.TvContract");
            using var uri = tvContract.CallStatic<AndroidJavaObject>("buildChannelUriForPassthroughInput", inputId);
            using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", uri);
            activity.Call("startActivity", intent);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError("HDMI launch failed: " + e);
            return "Couldn't open HDMI";
        }
#else
        Debug.Log("HDMI pressed (only works on an Android TV)");
        return "HDMI only works on Android TV";
#endif
    }
}
