using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BulletSpawner : MonoBehaviour
{
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

    private const int MaxBullets = 100;
    private const float BulletScale = 0.35f;
    private readonly HashSet<TestBullet> bullets = new HashSet<TestBullet>();
    private readonly Queue<TestBullet> availableBullets = new Queue<TestBullet>();
    private Camera playCamera;
    private float spawnCredit;
    private bool timerStopped;

    private void Awake()
    {
        playCamera = GetComponent<Camera>();
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

    private void Spawn()
    {
        const float margin = 0.02f;
        float depth = playCamera.WorldToViewportPoint(playerMovement.transform.position).z;
        float halfWidth = bulletSprite.bounds.extents.x * BulletScale / (2f * playCamera.orthographicSize * playCamera.aspect);
        float halfHeight = bulletSprite.bounds.extents.y * BulletScale / (2f * playCamera.orthographicSize);
        Vector2 start;
        Vector2 target;
        float edgePosition = UnityEngine.Random.value;

        switch (UnityEngine.Random.Range(0, 3))
        {
            case 0: // Top
                start = new Vector2(edgePosition, 1f + halfHeight + margin);
                target = new Vector2(Mathf.Clamp01(edgePosition + UnityEngine.Random.Range(-0.25f, 0.25f)), 0.5f);
                break;
            case 1: // Left
                start = new Vector2(-halfWidth - margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + UnityEngine.Random.Range(-0.25f, 0.25f)));
                break;
            default: // Right
                start = new Vector2(1f + halfWidth + margin, edgePosition);
                target = new Vector2(0.5f, Mathf.Clamp01(edgePosition + UnityEngine.Random.Range(-0.25f, 0.25f)));
                break;
        }

        Vector3 worldStart = playCamera.ViewportToWorldPoint(new Vector3(start.x, start.y, depth));
        Vector3 worldTarget = playCamera.ViewportToWorldPoint(new Vector3(target.x, target.y, depth));
        Vector3 direction = (worldTarget - worldStart).normalized;
        float speed = UnityEngine.Random.Range(playerMovement.SlowSpeed, playerMovement.NormalSpeed * 2f);

        TestBullet bullet = availableBullets.Dequeue();
        bullet.transform.position = worldStart;
        bullet.Initialize(this, playCamera, direction, speed);
        bullets.Add(bullet);
        bullet.gameObject.SetActive(true);
        if (bullets.Count > MaxActiveBullets)
            MaxActiveBullets = bullets.Count;
        CountChanged?.Invoke(bullets.Count);
    }

    public void ReturnBullet(TestBullet bullet)
    {
        if (bullets.Remove(bullet))
        {
            bullet.ResetForPool();
            availableBullets.Enqueue(bullet);
            CountChanged?.Invoke(bullets.Count);
        }
    }
}
