using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum PlayMode
{
    Human,
    Bot
}

[Serializable]
public sealed class PlayRecord
{
    public float firstLifeLostSeconds;
    public float secondLifeLostSeconds;
    public float thirdLifeLostSeconds;
    public string startedAtIso8601;
    public string mode;
}

[Serializable]
public sealed class PlayRecordCollection
{
    public List<PlayRecord> records = new List<PlayRecord>();
}

public static class PlayRecordStore
{
    private static string FilePath => Path.Combine(Application.persistentDataPath, "play_records.json");

    public static void Append(PlayRecord record)
    {
        try
        {
            if (!TryLoad(out PlayRecordCollection collection))
                return;

            collection.records.Add(record);
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(collection, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not save play record: {exception.Message}");
        }
    }

    public static List<PlayRecord> LoadRecords()
    {
        return TryLoad(out PlayRecordCollection collection)
            ? collection.records
            : new List<PlayRecord>();
    }

    private static bool TryLoad(out PlayRecordCollection collection)
    {
        collection = new PlayRecordCollection();
        try
        {
            if (!File.Exists(FilePath))
                return true;

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return true;

            PlayRecordCollection loaded = JsonUtility.FromJson<PlayRecordCollection>(json);
            if (loaded == null || loaded.records == null)
            {
                Debug.LogWarning("Play record file has an invalid format; existing file was left unchanged.");
                return false;
            }

            collection = loaded;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not read play records: {exception.Message}");
            return false;
        }
    }
}
