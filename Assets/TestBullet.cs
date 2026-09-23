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

    public void Initialize(BulletSpawner owner, Camera camera, Vector3 moveDirection, float moveSpeed)
    {
        spawner = owner;
        playCamera = camera;
        direction = moveDirection;
        speed = moveSpeed;
        enteredViewport = false;
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        transform.position += direction * (speed * Time.deltaTime);
        Vector3 viewport = playCamera.WorldToViewportPoint(transform.position);
        Vector3 corner = playCamera.WorldToViewportPoint(transform.position + spriteRenderer.bounds.extents);
        float halfWidth = Mathf.Abs(corner.x - viewport.x);
        float halfHeight = Mathf.Abs(corner.y - viewport.y);
        bool visible = viewport.x + halfWidth >= 0f && viewport.x - halfWidth <= 1f
            && viewport.y + halfHeight >= 0f && viewport.y - halfHeight <= 1f;
        if (visible)
            enteredViewport = true;
        else if (enteredViewport)
            spawner.ReturnBullet(this);
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
    }
}
