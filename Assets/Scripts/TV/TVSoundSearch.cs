using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Channel Server: find a sound or song to use as the Home music or a channel's preview sound, without hunting for files.
/// Searches Openverse (openverse.org: Creative Commons music and sound effects, free to reuse) through the TV, then the TV
/// downloads the one picked and puts it in place. YouTube isn't used: downloading from it breaks its terms and most of its
/// music is copyrighted.
/// </summary>
public static class TVSoundSearch
{
    private const int MaxBytes = 12 * 1024 * 1024;

    public static async Task<Dictionary<string, object>> Search(string q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return TVTiles.D("ok", false, "error", "Type what you're looking for");
        using var rq = UnityWebRequest.Get("https://api.openverse.org/v1/audio/?page_size=20&mature=false&q=" + UnityWebRequest.EscapeURL(q));
        rq.timeout = 15;
        rq.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
        await rq.SendWebRequest();
        if (rq.result != UnityWebRequest.Result.Success) return TVTiles.D("ok", false, "error", "The sound library didn't answer (" + rq.error + ")");
        var list = new List<object>();
        try
        {
            foreach (var r in (JArray) JObject.Parse(rq.downloadHandler.text)["results"])
            {
                string url = (string) r["url"], type = ((string) r["filetype"] ?? "").ToLowerInvariant();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("https://")) continue;
                if (!(type.StartsWith("mp3") || type == "ogg" || type == "wav")) continue;
                long size = r["filesize"] != null && r["filesize"].Type == JTokenType.Integer ? (long) r["filesize"] : 0;
                if (size > MaxBytes) continue;
                int ms = r["duration"] != null && r["duration"].Type == JTokenType.Integer ? (int) r["duration"] : 0;
                list.Add(TVTiles.D("title", (string) r["title"], "by", (string) r["creator"], "license", ((string) r["license"] ?? "").ToUpperInvariant(),
                    "seconds", ms / 1000, "url", url, "type", type.StartsWith("mp3") ? "mp3" : type, "page", (string) r["foreign_landing_url"]));
            }
        }
        catch (Exception e) { return TVTiles.D("ok", false, "error", "The answer couldn't be read: " + e.Message); }
        return TVTiles.D("ok", true, "results", list);
    }

    // downloads the picked sound and uses it: to = "home" (Home music) or "tile" (the preview sound of page/pos)
    public static async Task<string> Use(string url, string name, string to, int page, int pos)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://")) return "That sound's address isn't valid";
        using var rq = UnityWebRequest.Get(url);
        rq.timeout = 60;
        rq.SetRequestHeader("User-Agent", "RevoTV/1.0 (Android TV launcher)");
        var op = rq.SendWebRequest();
        while (!op.isDone)
        {
            if (rq.downloadedBytes > (ulong) MaxBytes) { rq.Abort(); return "That sound is too big (12 MB at most)"; }
            await Task.Yield();
        }
        if (rq.result != UnityWebRequest.Result.Success) return "The sound couldn't be downloaded (" + rq.error + ")";
        byte[] data = rq.downloadHandler.data;
        string ctype = (rq.GetResponseHeader("Content-Type") ?? "").ToLowerInvariant();
        string ext = ctype.Contains("ogg") ? "ogg" : ctype.Contains("wav") ? "wav" : "mp3";
        string file = (string.IsNullOrEmpty(name) ? "sound" : name) + "." + ext;
        Debug.Log("[Sounds] " + data.Length + " bytes (" + ctype + ") for " + to);
        if (to == "home") return TVHomeMusic.Save(data, file);
        if (to == "tile") return TVTiles.SetSound(page, pos, data, file);
        return "Pick where the sound goes";
    }
}
