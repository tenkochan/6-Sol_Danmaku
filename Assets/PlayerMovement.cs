using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerMovement : MonoBehaviour
{
    public bool CanMove { get; set; } = true;
    public Camera PlayCamera => playCamera;
    public float NormalSpeed => moveSpeed;
    public float SlowSpeed => moveSpeed * 0.4f;
    public bool UseBotInput { get; set; }
    public Vector2 BotDirection { get; set; }
    public float BotInputExpiresAt { get; set; }

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private Camera playCamera;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (playCamera == null)
            playCamera = Camera.main;
    }

    private void Update()
    {
        if (playCamera == null || !CanMove)
            return;

        Vector2 direction;
        float speed;
        float moveDeltaTime = Time.deltaTime;
        if (UseBotInput)
        {
            float remainingInputTime = BotInputExpiresAt - Time.time;
            direction = remainingInputTime > 0f ? BotDirection.normalized : Vector2.zero;
            moveDeltaTime = Mathf.Min(moveDeltaTime, Mathf.Max(0f, remainingInputTime));
            speed = NormalSpeed;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            direction = new Vector2(x, y).normalized;
            speed = keyboard.leftShiftKey.isPressed ? SlowSpeed : NormalSpeed;
        }
        Vector3 position = transform.position + (Vector3)(direction * speed * moveDeltaTime);

        Bounds bounds = spriteRenderer.bounds;
        Vector3 center = playCamera.WorldToViewportPoint(position);
        Vector3 corner = playCamera.WorldToViewportPoint(position + bounds.extents);
        Vector3 oppositeCorner = playCamera.WorldToViewportPoint(position - bounds.extents);
        float extentX = Mathf.Max(Mathf.Abs(corner.x - center.x), Mathf.Abs(oppositeCorner.x - center.x));
        float extentY = Mathf.Max(Mathf.Abs(corner.y - center.y), Mathf.Abs(oppositeCorner.y - center.y));

        center.x = Mathf.Clamp(center.x, extentX, 1f - extentX);
        center.y = Mathf.Clamp(center.y, extentY, 1f - extentY);
        transform.position = playCamera.ViewportToWorldPoint(center);
    }
}
