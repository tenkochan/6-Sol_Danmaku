using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BulletSpawner : MonoBehaviour
{
    public struct ScheduledBullet
    {
        public float spawnTime;
        public Vector2 position;
        public Vector2 velocity;
        public float speed;
        public float radiusX;
        public float radiusY;
    }

    public int ActiveCount => bullets.Count;
    public int MaxActiveBullets { get; private set; }
    public float SurvivalTime { get; private set; }
    public event Action<int> CountChanged;
    public event Action<float> SurvivalTimeChanged;

    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Sprite bulletSprite;
    [SerializeField, Min(0.01f)] private float startBulletsPerSecond = 2f;
    [SerializeField, Min(0.01f)] private float maxBulletsPerSecond = 20f;
    [SerializeField, Min(0.01f)] private float secondsToMaxRate = 60f;
    [SerializeField] private int randomSeed = 12345;

    private const int MaxBullets = 100;
    private const float BulletScale = 0.35f;
    private readonly HashSet<TestBullet> bullets = new HashSet<TestBullet>();
    private readonly Queue<TestBullet> availableBullets = new Queue<TestBullet>();
    private Camera playCamera;
    private float spawnCredit;
    private bool timerStopped;
    private System.Random random;
    private readonly List<ScheduledBullet> botSchedule = new List<ScheduledBullet>();
    private int nextBotSpawn;
    private bool botScheduleEnabled;

    private void Awake()
    {
        playCamera = GetComponent<Camera>();
        random = new System.Random(randomSeed);
        GameObject poolRoot = new GameObject("Enemy Bullet Pool");
        for (int i = 0; i < MaxBullets; i++)
        {
            GameObject bulletObject = new GameObject("Enemy Bullet");
            bulletObject.SetActive(false);
            bulletObject.transform.SetParent(poolRoot.transform);
            bulletObject.transform.localScale = new Vector3(BulletScale, BulletScale, 1f);
            SpriteRenderer renderer = bulletObject.AddComponent<SpriteRenderer>();
            renderer.sprite = bulletSprite;
            renderer.color = new Color(1f, 0.25f, 0.25f);
            CircleCollider2D collider = bulletObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            availableBullets.Enqueue(bulletObject.AddComponent<TestBullet>());
        }
    }

    private void Update()
    {
        if (timerStopped)
            return;

        SurvivalTime += Time.deltaTime;
        SurvivalTimeChanged?.Invoke(SurvivalTime);

        if (botScheduleEnabled)
        {
            EnsureBotSchedule(SurvivalTime);
            while (nextBotSpawn < botSchedule.Count && botSchedule[nextBotSpawn].spawnTime <= SurvivalTime)
            {
                if (bullets.Count < MaxBullets)
                    SpawnScheduled(botSchedule[nextBotSpawn]);
                nextBotSpawn++;
            }
            return;
        }

        float startRate = Mathf.Max(0.01f, startBulletsPerSecond);
        float maxRate = Mathf.Max(startRate, maxBulletsPerSecond);
        float progress = Mathf.Clamp01(SurvivalTime / Mathf.Max(0.01f, secondsToMaxRate));
        float currentRate = Mathf.Lerp(startRate, maxRate, progress);

        if (bullets.Count >= MaxBullets)
        {
            spawnCredit = 0f;
            return;
        }

        spawnCredit += Time.deltaTime * currentRate;
        while (spawnCredit >= 1f && bullets.Count < MaxBullets)
        {
            Spawn();
            spawnCredit -= 1f;
        }

        if (bullets.Count >= MaxBullets)
            spawnCredit = 0f;
    }

    public void StopSurvivalTimer() => timerStopped = true;

    public void EnableBotSchedule() => botScheduleEnabled = true;

    public void FillBotSchedule(float fromTime, float untilTime, List<ScheduledBullet> destination)
    {
        EnsureBotSchedule(untilTime);
        destination.Clear();
        foreach (ScheduledBullet bullet in botSchedule)
        {
            if (bullet.spawnTime >= fromTime && bullet.spawnTime < untilTime)
                destination.Add(bullet);
        }
    }

    public void FillBotBulletsAt(float gameTime, List<ScheduledBullet> destination)
    {
        const float spawnMargin = 0.02f;
        EnsureBotSchedule(gameTime);
        destination.Clear();
        foreach (ScheduledBullet bullet in botSchedule)
        {
            if (bullet.spawnTime > gameTime)
                break;
            Vector2 position = bullet.position + bullet.velocity * (gameTime - bullet.spawnTime);
            if (position.x >= -bullet.radiusX - spawnMargin && position.x <= 1f + bullet.radiusX + spawnMargin
                && position.y >= -bullet.radiusY - spawnMargin && position.y <= 1f + bullet.radiusY + spawnMargin)
                destination.Add(bullet);
        }
    }

    private void EnsureBotSchedule(float untilTime)
    {
        while (TimeForSpawn(botSchedule.Count + 1) <= untilTime)
        {
            float spawnTime = TimeForSpawn(botSchedule.Count + 1);
            botSchedule.Add(CreateScheduledBullet(spawnTime));
        }
    }

    private float TimeForSpawn(int number)
    {
        float start = Mathf.Max(0.01f, startBulletsPerSecond);
        float max = Mathf.Max(start, maxBulletsPerSecond);
        float ramp = Mathf.Max(0.01f, secondsToMaxRate);
        float duringRamp = (start + max) * ramp * 0.5f;
        if (number >= duringRamp)
            return ramp + (number - duringRamp) / max;
        if (Mathf.Approximately(start, max))
            return number / start;
        float slope = (max - start) / ramp;
        return (-start + Mathf.Sqrt(start * start + 2f * slope * number)) / slope;
    }

    private ScheduledBullet CreateScheduledBullet(float spawnTime)
    {
        const float margin = 0.02f;
        float halfWidth = bulletSprite.bounds.extents.x * BulletScale / (2f * playCamera.orthographicSize * playCamera.aspect);
        float halfHeight = bulletSprite.bounds.extents.y * BulletScale / (2f * playCamera.orthographicSize);
        float edgePosition = (float)random.NextDouble();
        Vector2 start;
        Vector2 target;
        switch (random.Next(0, 3))
        {
            case 0:
                start = new Vector2(edgePosition, 1f + halfHeight + margin);
                target = new Vector2(Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)), 0.5f);
                break;
            case 1:
                start = new Vector2(-halfWidth - margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)));
                break;
            default:
                start = new Vector2(1f + halfWidth + margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)));
                break;
        }
        float depth = playCamera.WorldToViewportPoint(playerMovement.transform.position).z;
        Vector3 worldStart = playCamera.ViewportToWorldPoint(new Vector3(start.x, start.y, depth));
        Vector3 worldTarget = playCamera.ViewportToWorldPoint(new Vector3(target.x, target.y, depth));
        Vector3 worldDirection = (worldTarget - worldStart).normalized;
        float speed = RandomRange(playerMovement.SlowSpeed, playerMovement.NormalSpeed * 2f);
        Vector3 velocityEnd = playCamera.WorldToViewportPoint(worldStart + worldDirection * speed);
        return new ScheduledBullet
        {
            spawnTime = spawnTime,
            position = start,
            velocity = new Vector2(velocityEnd.x - start.x, velocityEnd.y - start.y),
            speed = speed,
            radiusX = halfWidth,
            radiusY = halfHeight
        };
    }

    private void SpawnScheduled(ScheduledBullet scheduled)
    {
        float depth = playCamera.WorldToViewportPoint(playerMovement.transform.position).z;
        Vector3 start = playCamera.ViewportToWorldPoint(new Vector3(scheduled.position.x, scheduled.position.y, depth));
        Vector3 end = playCamera.ViewportToWorldPoint(new Vector3(scheduled.position.x + scheduled.velocity.x,
            scheduled.position.y + scheduled.velocity.y, depth));
        TestBullet bullet = availableBullets.Dequeue();
        bullet.transform.position = start + (end - start) * Mathf.Max(0f, SurvivalTime - scheduled.spawnTime);
        bullet.Initialize(this, playCamera, (end - start).normalized, scheduled.speed);
        bullets.Add(bullet);
        bullet.gameObject.SetActive(true);
        MaxActiveBullets = Mathf.Max(MaxActiveBullets, bullets.Count);
        CountChanged?.Invoke(bullets.Count);
    }

    private void Spawn()
    {
        const float margin = 0.02f;
        float depth = playCamera.WorldToViewportPoint(playerMovement.transform.position).z;
        float halfWidth = bulletSprite.bounds.extents.x * BulletScale / (2f * playCamera.orthographicSize * playCamera.aspect);
        float halfHeight = bulletSprite.bounds.extents.y * BulletScale / (2f * playCamera.orthographicSize);
        Vector2 start;
        Vector2 target;
        float edgePosition = (float)random.NextDouble();

        switch (random.Next(0, 3))
        {
            case 0: // Top
                start = new Vector2(edgePosition, 1f + halfHeight + margin);
                target = new Vector2(Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)), 0.5f);
                break;
            case 1: // Left
                start = new Vector2(-halfWidth - margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)));
                break;
            default: // Right
                start = new Vector2(1f + halfWidth + margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + RandomRange(-0.25f, 0.25f)));
                break;
        }

        Vector3 worldStart = playCamera.ViewportToWorldPoint(new Vector3(start.x, start.y, depth));
        Vector3 worldTarget = playCamera.ViewportToWorldPoint(new Vector3(target.x, target.y, depth));
        Vector3 direction = (worldTarget - worldStart).normalized;
        float speed = RandomRange(playerMovement.SlowSpeed, playerMovement.NormalSpeed * 2f);

        TestBullet bullet = availableBullets.Dequeue();
        bullet.transform.position = worldStart;
        bullet.Initialize(this, playCamera, direction, speed);
        bullets.Add(bullet);
        bullet.gameObject.SetActive(true);
        if (bullets.Count > MaxActiveBullets)
            MaxActiveBullets = bullets.Count;
        CountChanged?.Invoke(bullets.Count);
    }

    private float RandomRange(float min, float max) => min + (float)random.NextDouble() * (max - min);

    public void ReturnBullet(TestBullet bullet)
    {
        if (bullets.Remove(bullet))
        {
            bullet.ResetForPool();
            availableBullets.Enqueue(bullet);
            CountChanged?.Invoke(bullets.Count);
        }
    }

    public void FillActiveBullets(List<TestBullet> destination)
    {
        destination.Clear();
        foreach (TestBullet bullet in bullets)
            destination.Add(bullet);
    }
}
