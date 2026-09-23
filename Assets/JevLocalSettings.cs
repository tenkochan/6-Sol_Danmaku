using System;
using System.IO;
using UnityEngine;

public static class JevLocalSettings
{
    [Serializable]
    private sealed class KeyFile
    {
        public string apiKey;
    }

    public static bool TryGetApiKey(out string apiKey)
    {
        bool loaded = TryGetApiKey(out apiKey, out string error);
        if (!loaded)
            Debug.LogError(error);
        return loaded;
    }

    public static bool TryGetApiKey(out string apiKey, out string error)
    {
        apiKey = null;
        error = null;
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string keyPath = Path.Combine(projectRoot, ".secrets", "jev.local.json");
            if (!File.Exists(keyPath))
            {
                error = "Jev API Key not found. See README for setup instructions.";
                return false;
            }

            KeyFile file = JsonUtility.FromJson<KeyFile>(File.ReadAllText(keyPath));
            if (file == null || string.IsNullOrWhiteSpace(file.apiKey))
            {
                error = "Jev API Key is empty or missing in .secrets/jev.local.json. See README for setup instructions.";
                return false;
            }

            apiKey = file.apiKey;
            return true;
        }
        catch (Exception)
        {
            error = "Could not read .secrets/jev.local.json. Check the JSON format and README setup instructions.";
            return false;
        }
    }

}
