using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Layout numbers that can be nudged without a rebuild: lines of key=value in tune.txt next to the app's data. Missing file = the built-in defaults.</summary>
public static class TVTune
{
    private static Dictionary<string, float> values;

    public static float Get(string key, float fallback)
    {
        if (values == null)
        {
            values = new Dictionary<string, float>();
            try
            {
                string path = Application.persistentDataPath + "/tune.txt";
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var kv = line.Split('=');
                        if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) values[kv[0].Trim()] = v;
                    }
                }
            }
            catch { }
        }
        return values.TryGetValue(key, out var r) ? r : fallback;
    }
}
