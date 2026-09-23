using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerMovement), typeof(SpriteRenderer))]
public sealed class PlayerHealth : MonoBehaviour
{
    public const int MaxLives = 3;
    public int CurrentLives { get; private set; } = MaxLives;
    public event System.Action<int> LivesChanged;

    private PlayerMovement movement;
    private SpriteRenderer spriteRenderer;
    private LifeDisplay lifeDisplay;
    private bool respawning;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        lifeDisplay = GetComponent<LifeDisplay>();
    }

    public void TakeHit()
    {
        if (respawning || CurrentLives == 0)
            return;

        CurrentLives--;
        LivesChanged?.Invoke(CurrentLives);
        movement.CanMove = false;
        respawning = true;
        DeathPieces.Create(spriteRenderer);

        if (CurrentLives > 0)
            StartCoroutine(Respawn());
        else
        {
            spriteRenderer.enabled = false;
            StartCoroutine(GameOverAfterEffect());
        }
    }

    private IEnumerator GameOverAfterEffect()
    {
        yield return new WaitForSeconds(DeathPieces.Duration);
        Time.timeScale = 0f;
        lifeDisplay.ShowGameOver();
    }

    private IEnumerator Respawn()
    {
        Camera camera = movement.PlayCamera;
        float depth = camera.WorldToViewportPoint(transform.position).z;
        Bounds bounds = spriteRenderer.bounds;
        Vector3 center = camera.WorldToViewportPoint(transform.position);
        Vector3 top = camera.WorldToViewportPoint(transform.position + Vector3.up * bounds.extents.y);
        float spriteHeight = Mathf.Abs(top.y - center.y);
        float startY = spriteHeight;
        float endY = Mathf.Clamp(0.25f, startY, 1f - spriteHeight);

        transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, startY, depth));

        const float duration = 1f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);
            float y = Mathf.Lerp(startY, endY, elapsed / duration);
            transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, y, depth));
            spriteRenderer.enabled = Mathf.FloorToInt(elapsed * 10f) % 2 == 0;
            yield return null;
        }

        spriteRenderer.enabled = true;
        movement.CanMove = true;
        respawning = false;
    }
}
