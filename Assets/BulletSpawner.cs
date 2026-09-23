using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BulletSpawner : MonoBehaviour
{
    public int ActiveCount => bullets.Count;
    public event Action<int> CountChanged;

    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Sprite bulletSprite;
    [SerializeField] private float spawnInterval = 0.3f;

    private const int MaxBullets = 100;
    private readonly HashSet<TestBullet> bullets = new HashSet<TestBullet>();
    private Camera playCamera;
    private float elapsed;

    private void Awake() => playCamera = GetComponent<Camera>();

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed < spawnInterval)
            return;

        elapsed = 0f;
        if (bullets.Count < MaxBullets)
            Spawn();
    }

    private void Spawn()
    {
        const float scale = 0.35f;
        const float margin = 0.02f;
        float depth = playCamera.WorldToViewportPoint(playerMovement.transform.position).z;
        float halfWidth = bulletSprite.bounds.extents.x * scale / (2f * playCamera.orthographicSize * playCamera.aspect);
        float halfHeight = bulletSprite.bounds.extents.y * scale / (2f * playCamera.orthographicSize);
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

        GameObject bulletObject = new GameObject("Enemy Bullet", typeof(SpriteRenderer), typeof(CircleCollider2D));
        bulletObject.transform.position = worldStart;
        bulletObject.transform.localScale = new Vector3(scale, scale, 1f);
        SpriteRenderer renderer = bulletObject.GetComponent<SpriteRenderer>();
        renderer.sprite = bulletSprite;
        renderer.color = new Color(1f, 0.25f, 0.25f);
        CircleCollider2D collider = bulletObject.GetComponent<CircleCollider2D>();
        collider.isTrigger = true;
        TestBullet bullet = bulletObject.AddComponent<TestBullet>();
        bullet.Initialize(this, playCamera, direction, speed);

        bullets.Add(bullet);
        CountChanged?.Invoke(bullets.Count);
    }

    public void NotifyRemoved(TestBullet bullet)
    {
        if (bullets.Remove(bullet))
            CountChanged?.Invoke(bullets.Count);
    }
}
