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
        apiKey = null;
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string keyPath = Path.Combine(projectRoot, ".secrets", "jev.local.json");
            if (!File.Exists(keyPath))
            {
                Debug.LogError("Jev API key file is missing. Create .secrets/jev.local.json in the project root.");
                return false;
            }

            KeyFile file = JsonUtility.FromJson<KeyFile>(File.ReadAllText(keyPath));
            if (file == null || string.IsNullOrWhiteSpace(file.apiKey))
            {
                Debug.LogError("Jev API key is empty or missing in .secrets/jev.local.json.");
                return false;
            }

            apiKey = file.apiKey;
            return true;
        }
        catch (Exception)
        {
            Debug.LogError("Could not read .secrets/jev.local.json. Check that it contains valid JSON and is readable.");
            return false;
        }
    }

}
