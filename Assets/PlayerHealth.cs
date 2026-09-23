using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerMovement), typeof(SpriteRenderer))]
public sealed class PlayerHealth : MonoBehaviour
{
    public const int MaxLives = 3;
    public static PlayMode SelectedMode { get; set; } = PlayMode.Human;
    public PlayMode Mode => playMode;
    public int CurrentLives { get; private set; } = MaxLives;
    public bool IsRespawning => respawning && CurrentLives > 0;
    public bool IsInvincible => invulnerable;
    public float RemainingRespawnTime { get; private set; }
    public event System.Action<int> LivesChanged;
    public event System.Action Hit;

    [SerializeField] private PlayMode playMode = PlayMode.Human;

    private PlayerMovement movement;
    private SpriteRenderer spriteRenderer;
    private LifeDisplay lifeDisplay;
    private bool respawning;
    private bool invulnerable;
    private bool hitProcessing;
    private bool recordSaved;
    private string startedAtIso8601;
    private readonly float[] lifeLossTimes = { -1f, -1f, -1f };
    private BulletSpawner bulletSpawner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSelectedMode() => SelectedMode = PlayMode.Human;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        lifeDisplay = GetComponent<LifeDisplay>();
        bulletSpawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        playMode = SelectedMode;
        startedAtIso8601 = DateTimeOffset.Now.ToString("o");
    }

    public void TakeHit()
    {
        if (CurrentLives == 0 || invulnerable || respawning || hitProcessing)
        {
            if (playMode == PlayMode.Bot)
                Debug.Log($"PlayerHealth TakeHit blocked at {DateTimeOffset.Now:HH:mm:ss.fff}, frame {Time.frameCount}, lives {CurrentLives} -> {CurrentLives}, invulnerable={invulnerable}, respawning={respawning}, hitProcessing={hitProcessing}.");
            return;
        }

        hitProcessing = true;
        invulnerable = true;
        int livesBefore = CurrentLives;
        string hitTime = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        respawning = true;
        movement.CanMove = false;
        CurrentLives--;
        if (playMode == PlayMode.Bot)
            Debug.Log($"PlayerHealth TakeHit accepted at {hitTime}, frame {Time.frameCount}, lives {livesBefore} -> {CurrentLives}, invulnerable={invulnerable}.");
        lifeLossTimes[MaxLives - CurrentLives - 1] = bulletSpawner.SurvivalTime;
        LivesChanged?.Invoke(CurrentLives);
        RemainingRespawnTime = CurrentLives > 0 ? 1f : 0f;
        Hit?.Invoke();
        DeathPieces.Create(spriteRenderer);

        if (CurrentLives > 0)
            StartCoroutine(Respawn());
        else
            FinishGame(true, "GameOver");
        hitProcessing = false;
    }

    public void EndForApiError()
    {
        if (CurrentLives == 0)
            return;

        StopAllCoroutines();
        invulnerable = true;
        respawning = true;
        movement.CanMove = false;
        CurrentLives = 0;
        LivesChanged?.Invoke(CurrentLives);
        RemainingRespawnTime = 0f;
        DeathPieces.Create(spriteRenderer);
        FinishGame(false, "JevApiError");
    }

    private void FinishGame(bool completedNormally, string endReason)
    {
        bulletSpawner.StopSurvivalTimer();
        SaveRecordOnce(completedNormally, endReason);
        spriteRenderer.enabled = false;
        StartCoroutine(GameOverAfterEffect());
    }

    private void SaveRecordOnce(bool completedNormally, string endReason)
    {
        if (recordSaved)
            return;

        recordSaved = true;
        PlayRecordStore.Append(new PlayRecord
        {
            firstLifeLostSeconds = lifeLossTimes[0],
            secondLifeLostSeconds = lifeLossTimes[1],
            thirdLifeLostSeconds = lifeLossTimes[2],
            survivalSeconds = bulletSpawner.SurvivalTime,
            maxActiveBullets = bulletSpawner.MaxActiveBullets,
            startedAtIso8601 = startedAtIso8601,
            mode = playMode.ToString(),
            completedNormally = completedNormally,
            endReason = endReason
        });
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
            RemainingRespawnTime = duration - elapsed;
            float y = Mathf.Lerp(startY, endY, elapsed / duration);
            transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, y, depth));
            spriteRenderer.enabled = Mathf.FloorToInt(elapsed * 10f) % 2 == 0;
            yield return null;
        }

        RemainingRespawnTime = 0f;
        respawning = false;
        movement.CanMove = true;

        float protectionStartedAt = Time.time;
        while (Time.time - protectionStartedAt < duration)
        {
            float protectedElapsed = Time.time - protectionStartedAt;
            spriteRenderer.enabled = Mathf.FloorToInt((duration + protectedElapsed) * 10f) % 2 == 0;
            yield return null;
        }

        spriteRenderer.enabled = true;
        invulnerable = false;
    }
}
