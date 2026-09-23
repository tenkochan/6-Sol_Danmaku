using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class AlmightyChallengeFile
{
    public int version = 1;
    public string format = "6-sol-danmaku-almighty";
    public string challengeId;
    public float actionInterval;
    public int planningSteps;
    public float planningDuration;
    public float scheduleDuration;
    public float[] playerStartPosition;
    public float playerMovementSpeed;
    public float[] playerColliderRadius;
    public float[] playfieldBounds;
    public float[] viewportWorldSize;
    public string[] actions;
    public string movementRules;
    public int maxActiveBullets;
    public float[] bulletColliderRadius;
    public string bulletRules;
    public string[] bulletColumns;
    public int bulletCount;
    public float[] bulletSchedule;
}

[Serializable]
public sealed class AlmightyAnswerFile
{
    public int version;
    public string challengeId;
    public string name;
    public string[] actions;
}

public static class AlmightyChallengeExchange
{
    public static bool PendingExport { get; set; }
    public static string LastMessage { get; private set; }
    public static AlmightyAnswerFile SelectedAnswer { get; private set; }
    public static AlmightyChallengeFile SelectedChallenge { get; private set; }
    public static string SelectedFileName { get; private set; }
    public static bool HasSelectedAnswer => SelectedAnswer != null && SelectedChallenge != null;
    public static string ChallengeFolder => Path.Combine(Application.persistentDataPath, "almighty_challenges");
    public static string AnswerFolder => Path.Combine(Application.persistentDataPath, "almighty_answers");

    public static void ClearSelection()
    {
        SelectedAnswer = null;
        SelectedChallenge = null;
        SelectedFileName = null;
    }

    public static bool Export(PlayerMovement movement,
        List<BulletSpawner.ScheduledBullet> schedule, out string message)
    {
        try
        {
            Camera camera = movement.PlayCamera;
            Vector3 start = camera.WorldToViewportPoint(movement.transform.position);
            CircleCollider2D collider = movement.GetComponent<CircleCollider2D>();
            Vector3 hitCenter = camera.WorldToViewportPoint(collider.bounds.center);
            Vector3 hitCorner = camera.WorldToViewportPoint(collider.bounds.center + collider.bounds.extents);
            Vector3 spriteCorner = camera.WorldToViewportPoint(movement.transform.position
                + movement.GetComponent<SpriteRenderer>().bounds.extents);
            float marginX = Mathf.Abs(spriteCorner.x - start.x);
            float marginY = Mathf.Abs(spriteCorner.y - start.y);
            float[] packed = new float[schedule.Count * 8];
            for (int i = 0; i < schedule.Count; i++)
            {
                BulletSpawner.ScheduledBullet bullet = schedule[i];
                int offset = i * 8;
                packed[offset] = bullet.spawnTime;
                packed[offset + 1] = bullet.position.x;
                packed[offset + 2] = bullet.position.y;
                packed[offset + 3] = bullet.velocity.x;
                packed[offset + 4] = bullet.velocity.y;
                packed[offset + 5] = bullet.speed;
                packed[offset + 6] = bullet.direction.x;
                packed[offset + 7] = bullet.direction.y;
            }
            AlmightyChallengeFile challenge = new AlmightyChallengeFile
            {
                actionInterval = AlmightyBotController.ActionSeconds,
                planningSteps = AlmightyBotController.ActionCount,
                planningDuration = AlmightyBotController.ActionCount * AlmightyBotController.ActionSeconds,
                scheduleDuration = AlmightyBotController.ScheduleSeconds,
                playerStartPosition = new[] { start.x, start.y },
                playerMovementSpeed = movement.NormalSpeed,
                playerColliderRadius = new[] { Mathf.Abs(hitCorner.x - hitCenter.x), Mathf.Abs(hitCorner.y - hitCenter.y) },
                playfieldBounds = new[] { marginX, 1f - marginX, marginY, 1f - marginY },
                viewportWorldSize = new[] { 2f * camera.orthographicSize * camera.aspect, 2f * camera.orthographicSize },
                actions = new[] { "Stay", "Up", "Down", "Left", "Right", "UpLeft", "UpRight", "DownLeft", "DownRight" },
                movementRules = "Each action lasts 0.2 seconds. Move at playerMovementSpeed world units per second. Normalize diagonal input before moving. Clamp player center to playfieldBounds after movement. After step 199, Stay.",
                maxActiveBullets = BulletSpawner.MaxActiveBulletCount,
                bulletColliderRadius = schedule.Count > 0
                    ? new[] { schedule[0].radiusX, schedule[0].radiusY } : new[] { 0f, 0f },
                bulletRules = "Bullet rows are [spawnTime,x,y,vx,vy,speed,dirX,dirY]. Position and velocity are viewport coordinates and viewport units per second; speed is world units per second; direction is normalized world direction. ID is row index + 1. At time t >= spawnTime, position = (x,y) + (vx,vy) * (t-spawnTime). Spawn only if fewer than maxActiveBullets are active; skip a blocked spawn permanently. A bullet spawns just outside the top, left or right edge. It survives until it has entered the viewport and then leaves the viewport including its sprite radius, or hits the player. Future positions are not stored per step.",
                bulletColumns = new[] { "spawnTime", "x", "y", "vx", "vy", "speed", "dirX", "dirY" },
                bulletCount = schedule.Count,
                bulletSchedule = packed
            };
            challenge.challengeId = ComputeId(challenge);
            Directory.CreateDirectory(ChallengeFolder);
            string path = Path.Combine(ChallengeFolder, "challenge_" + challenge.challengeId.Substring(0, 16) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(challenge, true), Encoding.UTF8);
            message = "Challenge saved: " + path;
            LastMessage = message;
            return true;
        }
        catch (Exception error)
        {
            message = "Challenge export failed: " + error.Message;
            LastMessage = message;
            return false;
        }
    }

    public static bool SelectAnswer(string pathOrName, out string message)
    {
        ClearSelection();
        try
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                message = "Enter an Answer JSON path or a filename in " + AnswerFolder;
                return false;
            }
            string path = Path.IsPathRooted(pathOrName) ? pathOrName : Path.Combine(AnswerFolder, pathOrName);
            if (!File.Exists(path))
            {
                message = "Answer file not found: " + path;
                return false;
            }
            AlmightyAnswerFile answer = JsonUtility.FromJson<AlmightyAnswerFile>(File.ReadAllText(path));
            if (answer == null)
            {
                message = "Answer JSON parsing failed.";
                return false;
            }
            if (answer.version != 1)
            {
                message = "Unsupported Answer version: " + answer.version;
                return false;
            }
            if (answer.actions == null || answer.actions.Length != AlmightyBotController.ActionCount)
            {
                message = "Answer actions must contain exactly " + AlmightyBotController.ActionCount + " entries.";
                return false;
            }
            for (int i = 0; i < answer.actions.Length; i++)
            {
                if (!AlmightyBotController.IsValidAction(answer.actions[i]))
                {
                    message = "Invalid action at index " + i + ": " + answer.actions[i];
                    return false;
                }
            }
            if (string.IsNullOrWhiteSpace(answer.challengeId))
            {
                message = "Answer challengeId is missing.";
                return false;
            }
            if (!Directory.Exists(ChallengeFolder))
            {
                message = "Challenge folder not found: " + ChallengeFolder;
                return false;
            }
            foreach (string challengePath in Directory.GetFiles(ChallengeFolder, "challenge_*.json"))
            {
                AlmightyChallengeFile challenge;
                try { challenge = JsonUtility.FromJson<AlmightyChallengeFile>(File.ReadAllText(challengePath)); }
                catch { continue; }
                if (challenge == null || challenge.challengeId != answer.challengeId)
                    continue;
                if (!ValidateChallenge(challenge, out message))
                    return false;
                SelectedAnswer = answer;
                SelectedChallenge = challenge;
                SelectedFileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(answer.name))
                    answer.name = "External";
                message = $"Selected Plan: {SelectedFileName} ({answer.name}, {answer.actions.Length} actions)";
                return true;
            }
            message = "No matching Challenge JSON for challengeId " + answer.challengeId;
            return false;
        }
        catch (Exception error)
        {
            message = "Answer JSON validation failed: " + error.Message;
            return false;
        }
    }

    public static bool ValidateChallenge(AlmightyChallengeFile challenge, out string message)
    {
        if (challenge.version != 1 || challenge.format != "6-sol-danmaku-almighty"
            || challenge.planningSteps != AlmightyBotController.ActionCount
            || Mathf.Abs(challenge.actionInterval - AlmightyBotController.ActionSeconds) > 0.00001f
            || challenge.maxActiveBullets != BulletSpawner.MaxActiveBulletCount
            || challenge.bulletSchedule == null || challenge.bulletSchedule.Length != challenge.bulletCount * 8
            || challenge.bulletColliderRadius == null || challenge.bulletColliderRadius.Length != 2)
        {
            message = "Challenge format, rules, or bullet schedule is invalid.";
            return false;
        }
        if (challenge.challengeId != ComputeId(challenge))
        {
            message = "Challenge data does not match its challengeId.";
            return false;
        }
        message = null;
        return true;
    }

    public static List<BulletSpawner.ScheduledBullet> DecodeSchedule(AlmightyChallengeFile challenge,
        Camera camera)
    {
        List<BulletSpawner.ScheduledBullet> bullets = new List<BulletSpawner.ScheduledBullet>(challenge.bulletCount);
        for (int i = 0; i < challenge.bulletCount; i++)
        {
            int offset = i * 8;
            Vector2 velocity = new Vector2(challenge.bulletSchedule[offset + 3], challenge.bulletSchedule[offset + 4]);
            bullets.Add(new BulletSpawner.ScheduledBullet
            {
                id = i + 1,
                spawnTime = challenge.bulletSchedule[offset],
                position = new Vector2(challenge.bulletSchedule[offset + 1], challenge.bulletSchedule[offset + 2]),
                velocity = velocity,
                direction = new Vector2(challenge.bulletSchedule[offset + 6], challenge.bulletSchedule[offset + 7]),
                speed = challenge.bulletSchedule[offset + 5],
                radiusX = challenge.bulletColliderRadius[0],
                radiusY = challenge.bulletColliderRadius[1]
            });
        }
        return bullets;
    }

    public static bool MatchesScene(AlmightyChallengeFile challenge, PlayerMovement movement,
        out string message)
    {
        Camera camera = movement.PlayCamera;
        Vector3 start = camera.WorldToViewportPoint(movement.transform.position);
        Vector3 spriteCorner = camera.WorldToViewportPoint(movement.transform.position
            + movement.GetComponent<SpriteRenderer>().bounds.extents);
        CircleCollider2D collider = movement.GetComponent<CircleCollider2D>();
        Vector3 hitCenter = camera.WorldToViewportPoint(collider.bounds.center);
        Vector3 hitCorner = camera.WorldToViewportPoint(collider.bounds.center + collider.bounds.extents);
        float[] actual = { start.x, start.y, movement.NormalSpeed,
            Mathf.Abs(hitCorner.x - hitCenter.x), Mathf.Abs(hitCorner.y - hitCenter.y),
            Mathf.Abs(spriteCorner.x - start.x), 1f - Mathf.Abs(spriteCorner.x - start.x),
            Mathf.Abs(spriteCorner.y - start.y), 1f - Mathf.Abs(spriteCorner.y - start.y),
            2f * camera.orthographicSize * camera.aspect, 2f * camera.orthographicSize };
        float[] expected = { challenge.playerStartPosition[0], challenge.playerStartPosition[1],
            challenge.playerMovementSpeed, challenge.playerColliderRadius[0], challenge.playerColliderRadius[1],
            challenge.playfieldBounds[0], challenge.playfieldBounds[1],
            challenge.playfieldBounds[2], challenge.playfieldBounds[3],
            challenge.viewportWorldSize[0], challenge.viewportWorldSize[1] };
        for (int i = 0; i < actual.Length; i++)
        {
            if (Mathf.Abs(actual[i] - expected[i]) <= 0.0001f)
                continue;
            message = "Challenge player or camera settings differ from the current Main scene.";
            return false;
        }
        message = null;
        return true;
    }

    private static string ComputeId(AlmightyChallengeFile challenge)
    {
        string previous = challenge.challengeId;
        challenge.challengeId = "";
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(challenge));
        challenge.challengeId = previous;
        using (SHA256 hash = SHA256.Create())
        {
            byte[] digest = hash.ComputeHash(data);
            StringBuilder result = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
                result.Append(value.ToString("x2"));
            return result.ToString();
        }
    }
}
