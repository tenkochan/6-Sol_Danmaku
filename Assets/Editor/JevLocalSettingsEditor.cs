using UnityEditor;
using UnityEngine;

public static class JevLocalSettingsEditor
{
    [MenuItem("Tools/Jev/Check Local API Key")]
    private static void CheckLocalApiKey()
    {
        if (JevLocalSettings.TryGetApiKey(out _))
            Debug.Log("Jev API key is configured locally.");
    }
}
