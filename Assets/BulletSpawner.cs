using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BulletSpawner : MonoBehaviour
{
    public struct ScheduledBullet
    {
        public int id;
        public float spawnTime;
        public Vector2 position;
        public Vector2 velocity;
        public Vector2 direction;
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

    public const int MaxActiveBulletCount = 50;
    private const int MaxBullets = MaxActiveBulletCount;
    private const float BulletScale = 0.35f;
    private readonly HashSet<TestBullet> bullets = new HashSet<TestBullet>();
    private readonly Queue<TestBullet> availableBullets = new Queue<TestBullet>();
    private Camera playCamera;
    private Transform poolRoot;
    private float spawnCredit;
    private bool timerStopped;
    private System.Random random;
    private readonly List<ScheduledBullet> botSchedule = new List<ScheduledBullet>();
    private int nextBotSpawn;
    private bool botScheduleEnabled;
    private bool almightyScheduleEnabled;
    private bool challengeScheduleLoaded;

    private void Awake()
    {
        playCamera = GetComponent<Camera>();
        random = new System.Random(randomSeed);
        poolRoot = new GameObject("Enemy Bullet Pool").transform;
        for (int i = 0; i < MaxBullets; i++)
            CreatePooledBullet();
    }

    private void CreatePooledBullet()
    {
        GameObject bulletObject = new GameObject("Enemy Bullet");
        bulletObject.SetActive(false);
        bulletObject.transform.SetParent(poolRoot);
        bulletObject.transform.localScale = new Vector3(BulletScale, BulletScale, 1f);
        SpriteRenderer renderer = bulletObject.AddComponent<SpriteRenderer>();
        renderer.sprite = bulletSprite;
        renderer.color = new Color(1f, 0.25f, 0.25f);
        CircleCollider2D collider = bulletObject.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        availableBullets.Enqueue(bulletObject.AddComponent<TestBullet>());
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

    public void EnableAlmightySchedule()
    {
        botScheduleEnabled = true;
        almightyScheduleEnabled = true;
        EnsureBotSchedule(60f);
    }

    public void LoadChallengeSchedule(List<ScheduledBullet> imported)
    {
        botSchedule.Clear();
        botSchedule.AddRange(imported);
        nextBotSpawn = 0;
        challengeScheduleLoaded = true;
        botScheduleEnabled = true;
        almightyScheduleEnabled = true;
    }

    private void LateUpdate()
    {
        if (!almightyScheduleEnabled)
            return;
        foreach (TestBullet bullet in bullets)
            bullet.SyncScheduledPosition(SurvivalTime);
    }

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

    public void FillBotScheduleThrough(float untilTime, List<ScheduledBullet> destination)
    {
        EnsureBotSchedule(untilTime);
        destination.Clear();
        foreach (ScheduledBullet bullet in botSchedule)
        {
            if (bullet.spawnTime > untilTime)
                break;
            destination.Add(bullet);
        }
    }

    public void FillBotBulletsAt(float gameTime, List<ScheduledBullet> destination)
    {
        EnsureBotSchedule(gameTime);
        destination.Clear();
        foreach (ScheduledBullet bullet in botSchedule)
        {
            if (bullet.spawnTime > gameTime)
                break;
            if (IsScheduledBulletAliveAt(bullet, gameTime))
                destination.Add(bullet);
        }
    }

    public void FillBotScheduleForSimulation(float gameTime, float futureSeconds,
        List<ScheduledBullet> activeNow, List<ScheduledBullet> futureSpawns)
    {
        float untilTime = gameTime + futureSeconds;
        EnsureBotSchedule(untilTime);
        activeNow.Clear();
        futureSpawns.Clear();
        List<ScheduledBullet> active = new List<ScheduledBullet>(MaxBullets);
        bool capturedCurrent = false;

        foreach (ScheduledBullet bullet in botSchedule)
        {
            if (bullet.spawnTime >= untilTime)
                break;

            if (!capturedCurrent && bullet.spawnTime > gameTime)
            {
                RemoveExitedScheduledBullets(active, gameTime);
                activeNow.AddRange(active);
                capturedCurrent = true;
            }

            RemoveExitedScheduledBullets(active, bullet.spawnTime);
            if (active.Count >= MaxBullets)
                continue;

            active.Add(bullet);
            if (capturedCurrent)
                futureSpawns.Add(bullet);
        }

        if (!capturedCurrent)
        {
            RemoveExitedScheduledBullets(active, gameTime);
            activeNow.AddRange(active);
        }
    }

    private static void RemoveExitedScheduledBullets(List<ScheduledBullet> active, float gameTime)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (!IsScheduledBulletAliveAt(active[i], gameTime))
                active.RemoveAt(i);
        }
    }

    private static bool IsScheduledBulletAliveAt(ScheduledBullet bullet, float gameTime)
    {
        float elapsed = gameTime - bullet.spawnTime;
        Vector2 position = bullet.position + bullet.velocity * elapsed;
        return TestBullet.IsVisibleInViewport(position, bullet.radiusX, bullet.radiusY)
            || !HasEnteredViewport(bullet, elapsed);
    }

    private static bool HasEnteredViewport(ScheduledBullet bullet, float elapsed)
    {
        float enter = 0f;
        float exit = elapsed;
        if (!IntersectsAxis(bullet.position.x, bullet.velocity.x, -bullet.radiusX, 1f + bullet.radiusX,
                ref enter, ref exit))
            return false;
        return IntersectsAxis(bullet.position.y, bullet.velocity.y, -bullet.radiusY, 1f + bullet.radiusY,
            ref enter, ref exit);
    }

    private static bool IntersectsAxis(float start, float velocity, float minimum, float maximum,
        ref float enter, ref float exit)
    {
        if (Mathf.Approximately(velocity, 0f))
            return start >= minimum && start <= maximum;
        float first = (minimum - start) / velocity;
        float second = (maximum - start) / velocity;
        if (first > second)
        {
            float swap = first;
            first = second;
            second = swap;
        }
        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    private void EnsureBotSchedule(float untilTime)
    {
        if (challengeScheduleLoaded && untilTime <= AlmightyBotController.ScheduleSeconds)
            return;
        while (TimeForSpawn(botSchedule.Count + 1) <= untilTime)
        {
            float spawnTime = TimeForSpawn(botSchedule.Count + 1);
            botSchedule.Add(CreateScheduledBullet(botSchedule.Count + 1, spawnTime));
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

    private ScheduledBullet CreateScheduledBullet(int id, float spawnTime)
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
            id = id,
            spawnTime = spawnTime,
            position = start,
            velocity = new Vector2(velocityEnd.x - start.x, velocityEnd.y - start.y),
            direction = new Vector2(worldDirection.x, worldDirection.y),
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
        if (almightyScheduleEnabled)
            bullet.InitializeScheduled(this, playCamera, start, (end - start).normalized, scheduled.speed, scheduled.spawnTime);
        else
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
