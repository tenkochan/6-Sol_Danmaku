using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerMovement : MonoBehaviour
{
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
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || playCamera == null)
            return;

        float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);

        Vector2 direction = new Vector2(x, y).normalized;
        float speed = keyboard.leftShiftKey.isPressed ? moveSpeed * 0.4f : moveSpeed;
        Vector3 position = transform.position + (Vector3)(direction * speed * Time.deltaTime);

        float halfHeight = playCamera.orthographicSize;
        float halfWidth = halfHeight * playCamera.aspect;
        Vector3 cameraPosition = playCamera.transform.position;
        Vector3 spriteExtent = spriteRenderer.bounds.extents;

        position.x = Mathf.Clamp(position.x, cameraPosition.x - halfWidth + spriteExtent.x,
            cameraPosition.x + halfWidth - spriteExtent.x);
        position.y = Mathf.Clamp(position.y, cameraPosition.y - halfHeight + spriteExtent.y,
            cameraPosition.y + halfHeight - spriteExtent.y);
        transform.position = position;
    }
}
