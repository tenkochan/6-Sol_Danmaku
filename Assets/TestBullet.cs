using UnityEngine;

public sealed class TestBullet : MonoBehaviour
{
    public Vector3 Direction => direction;
    public float Speed => speed;

    private BulletSpawner spawner;
    private Camera playCamera;
    private Vector3 direction;
    private float speed;
    private bool enteredViewport;
    private SpriteRenderer spriteRenderer;
    private bool scheduledMotion;
    private Vector3 scheduledStart;
    private float scheduledSpawnTime;

    public void Initialize(BulletSpawner owner, Camera camera, Vector3 moveDirection, float moveSpeed)
    {
        spawner = owner;
        playCamera = camera;
        direction = moveDirection;
        speed = moveSpeed;
        enteredViewport = false;
        spriteRenderer = GetComponent<SpriteRenderer>();
        scheduledMotion = false;
    }

    public void InitializeScheduled(BulletSpawner owner, Camera camera, Vector3 start,
        Vector3 moveDirection, float moveSpeed, float spawnTime)
    {
        Initialize(owner, camera, moveDirection, moveSpeed);
        scheduledMotion = true;
        scheduledStart = start;
        scheduledSpawnTime = spawnTime;
    }

    public void SyncScheduledPosition(float gameTime)
    {
        if (scheduledMotion)
            transform.position = scheduledStart + direction * (speed * Mathf.Max(0f, gameTime - scheduledSpawnTime));
    }

    private void Update()
    {
        if (scheduledMotion)
            SyncScheduledPosition(spawner.SurvivalTime);
        else
            transform.position += direction * (speed * Time.deltaTime);
        Vector3 viewport = playCamera.WorldToViewportPoint(transform.position);
        Vector3 corner = playCamera.WorldToViewportPoint(transform.position + spriteRenderer.bounds.extents);
        float halfWidth = Mathf.Abs(corner.x - viewport.x);
        float halfHeight = Mathf.Abs(corner.y - viewport.y);
        bool visible = IsVisibleInViewport(new Vector2(viewport.x, viewport.y), halfWidth, halfHeight);
        if (visible)
            enteredViewport = true;
        else if (enteredViewport)
            spawner.ReturnBullet(this);
    }

    public static bool IsVisibleInViewport(Vector2 position, float halfWidth, float halfHeight)
    {
        return position.x + halfWidth >= 0f && position.x - halfWidth <= 1f
            && position.y + halfHeight >= 0f && position.y - halfHeight <= 1f;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerHealth player = other.GetComponent<PlayerHealth>();
        if (player == null)
            return;

        player.TakeHit();
        spawner.ReturnBullet(this);
    }

    public void ResetForPool()
    {
        gameObject.SetActive(false);
        transform.position = Vector3.zero;
        playCamera = null;
        direction = Vector3.zero;
        speed = 0f;
        enteredViewport = false;
        scheduledMotion = false;
    }
}
