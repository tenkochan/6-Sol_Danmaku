using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class JevBotController : MonoBehaviour
{
    private const string Endpoint = "https://jev-ai.pro/api/v1/systemone";
    private const string Model = "jev-latest";
    private const float DecisionStep = 0.2f;
    private const int PlanSteps = 6;
    private const float PlanHorizon = DecisionStep * PlanSteps;
    private const float LeadSeconds = 5f;

    private readonly List<TestBullet> activeBullets = new List<TestBullet>();
    private readonly Queue<DecisionRecord> recentDecisions = new Queue<DecisionRecord>();
    private readonly List<PlannedStep> actionBuffer = new List<PlannedStep>(PlanSteps);
    private readonly List<BulletSpawner.ScheduledBullet> scheduledBullets = new List<BulletSpawner.ScheduledBullet>();
    private readonly List<BulletSpawner.ScheduledBullet> futureScheduledBullets = new List<BulletSpawner.ScheduledBullet>();
    private PlayerMovement movement;
    private PlayerHealth health;
    private BulletSpawner spawner;
    private string apiKey;
    private bool requestInFlight;
    private bool failed;
    private string previousAction = "Stay";
    private int activeStep = -1;
    private string activeChoice;
    private float choiceAppliedAt;
    private float idleStartedAt = -1f;
    private Coroutine requestCoroutine;
    private UnityWebRequest activeRequest;
    private float plannedUntil;
    private Vector2 virtualPosition;
    private float virtualRespawnRemaining;
    private float virtualInvulnerabilityRemaining;
    private float countdownRemaining = 5f;
    private bool countdownDone;
    private bool buffering = true;
    private bool pendingRebase;
    private UnityEngine.UI.Text statusText;

    private void Start()
    {
        health = GetComponent<PlayerHealth>();
        if (health.Mode != PlayMode.Bot || health.IsAlmighty)
        {
            enabled = false;
            return;
        }

        movement = GetComponent<PlayerMovement>();
        movement.UseBotInput = true;
        movement.BotDirection = Vector2.zero;
        movement.BotInputExpiresAt = 0f;
        spawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        spawner.EnableBotSchedule();
        Vector3 viewport = movement.PlayCamera.WorldToViewportPoint(transform.position);
        virtualPosition = new Vector2(viewport.x, viewport.y);
        CreateStatusOverlay();
        Time.timeScale = 0f;
        health.Hit += LogRecentDecisionsOnHit;
        if (!JevLocalSettings.TryGetApiKey(out apiKey))
            FailBot("Local Jev API key is unavailable.");
    }

    private void OnDestroy()
    {
        if (health != null)
            health.Hit -= LogRecentDecisionsOnHit;
        if (health != null && health.Mode == PlayMode.Bot)
            Time.timeScale = 1f;
    }

    private void Update()
    {
        if (failed || health.CurrentLives == 0)
            return;

        if (pendingRebase)
        {
            Vector3 viewport = movement.PlayCamera.WorldToViewportPoint(transform.position);
            virtualPosition = new Vector2(viewport.x, viewport.y);
            virtualRespawnRemaining = health.RemainingRespawnTime;
            virtualInvulnerabilityRemaining = virtualRespawnRemaining + 1f;
            pendingRebase = false;
        }

        if (!countdownDone)
        {
            countdownRemaining -= Time.unscaledDeltaTime;
            countdownDone = countdownRemaining <= 0f;
            statusText.text = countdownDone ? "BUFFERING..." : Mathf.CeilToInt(countdownRemaining).ToString();
        }

        if (!requestInFlight)
            StartDecisionRequest();

        float playbackTime = spawner.SurvivalTime;
        float bufferSeconds = Mathf.Max(0f, plannedUntil - playbackTime);
        if (!countdownDone)
            return;

        if (!buffering && bufferSeconds < 0.5f)
            SetBuffering(true, playbackTime, bufferSeconds);
        else if (buffering && bufferSeconds >= LeadSeconds)
            SetBuffering(false, playbackTime, bufferSeconds);

        if (!buffering)
            UpdateBufferedAction(playbackTime);
    }

    private void SetBuffering(bool value, float playbackTime, float bufferSeconds)
    {
        buffering = value;
        Time.timeScale = value ? 0f : 1f;
        statusText.text = value ? "BUFFERING..." : "";
        Debug.Log($"Jev Bot buffering {(value ? "start" : "end")}: playback={playbackTime:F2}s, plannedUntil={plannedUntil:F2}s, bufferSeconds={bufferSeconds:F2}s.");
    }

    private void CreateStatusOverlay()
    {
        GameObject canvasObject = new GameObject("Bot Buffer UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        GameObject label = new GameObject("Bot Status", typeof(RectTransform), typeof(UnityEngine.UI.Text));
        label.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, 160f);
        statusText = label.GetComponent<UnityEngine.UI.Text>();
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 90;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = Color.white;
        statusText.raycastTarget = false;
        statusText.text = "5";
    }

    private void StartDecisionRequest()
    {
        requestInFlight = true;
        requestCoroutine = StartCoroutine(RequestDecision());
    }

    private IEnumerator RequestDecision()
    {
        float planStart = plannedUntil;
        string requestedAt = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        Vector2 requestPosition = virtualPosition;
        string requestJson = null;
        try
        {
            requestJson = JsonUtility.ToJson(BuildRequest());
        }
        catch (Exception)
        {
            FailBot("Could not collect the Jev Bot game state.");
        }
        if (failed)
            yield break;

        using (UnityWebRequest request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            activeRequest = request;
            float requestStartedAt = 0f;
            UnityWebRequestAsyncOperation operation = null;
            try
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                request.timeout = 5;
                requestStartedAt = Time.realtimeSinceStartup;
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                FailBot("Could not start the Jev API request.");
            }
            if (failed)
                yield break;

            yield return operation;

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401 || request.responseCode == 403)
                    FailBot($"Jev API authentication failed (HTTP {request.responseCode}). Check the local API key and Jev AI account access.");
                else
                    FailBot($"Jev API request failed (HTTP {request.responseCode}).");
                yield break;
            }

            JevResponse response = null;
            try
            {
                response = JsonUtility.FromJson<JevResponse>(request.downloadHandler.text);
            }
            catch (Exception)
            {
                FailBot("Jev API response could not be parsed.");
            }
            if (failed)
                yield break;

            JevChoiceAnswer[] choices = response?.answers?.ToArray();
            if (choices == null || choices.Length != PlanSteps)
            {
                FailBot("Jev API returned an invalid movement plan.");
                yield break;
            }

            PlannedStep[] newPlan = new PlannedStep[PlanSteps];
            for (int i = 0; i < PlanSteps; i++)
            {
                if (choices[i] == null || choices[i].type != "choice"
                    || !TryReadDirection(choices[i].choice, out Vector2 direction))
                {
                    FailBot("Jev API returned an invalid movement plan step.");
                    yield break;
                }
                float startsAt = planStart + i * DecisionStep;
                newPlan[i] = new PlannedStep(choices[i].choice, direction, startsAt, startsAt + DecisionStep);
            }

            float now = Time.realtimeSinceStartup;
            foreach (PlannedStep step in newPlan)
            {
                actionBuffer.Add(step);
                AdvanceVirtualPlayer(step);
            }
            plannedUntil = planStart + PlanHorizon;
            previousAction = newPlan[PlanSteps - 1].choice;
            float bufferSeconds = plannedUntil - spawner.SurvivalTime;
            Debug.Log($"Jev Bot plan request {requestedAt}: playback={spawner.SurvivalTime:F2}s, plannedUntil={plannedUntil:F2}s, bufferSeconds={bufferSeconds:F2}s, latency={now - requestStartedAt:F3}s, new actions={PlanSteps}, virtual player=({requestPosition.x:F3},{requestPosition.y:F3}).");
        }

        activeRequest = null;
        requestCoroutine = null;
        requestInFlight = false;
        if (!failed && health.CurrentLives > 0)
            StartDecisionRequest();
    }

    private void AdvanceVirtualPlayer(PlannedStep step)
    {
        float movableTime = DecisionStep;
        if (virtualRespawnRemaining > 0f)
        {
            float respawnTime = Mathf.Min(movableTime, virtualRespawnRemaining);
            virtualRespawnRemaining -= respawnTime;
            movableTime -= respawnTime;
            float bottom = GetPlayerViewportMargin().y;
            virtualPosition.y = Mathf.Lerp(bottom, Mathf.Max(bottom, 0.25f), 1f - virtualRespawnRemaining);
            virtualPosition.x = 0.5f;
        }
        virtualInvulnerabilityRemaining = Mathf.Max(0f, virtualInvulnerabilityRemaining - DecisionStep);
        if (movableTime <= 0f)
            return;
        Camera camera = movement.PlayCamera;
        Vector3 worldOrigin = camera.ViewportToWorldPoint(new Vector3(virtualPosition.x, virtualPosition.y,
            camera.WorldToViewportPoint(transform.position).z));
        Vector3 worldEnd = worldOrigin + (Vector3)(step.direction.normalized * movement.NormalSpeed * movableTime);
        Vector3 viewportEnd = camera.WorldToViewportPoint(worldEnd);
        Vector2 margin = GetPlayerViewportMargin();
        virtualPosition.x = Mathf.Clamp(viewportEnd.x, margin.x, 1f - margin.x);
        virtualPosition.y = Mathf.Clamp(viewportEnd.y, margin.y, 1f - margin.y);
    }

    private Vector2 GetPlayerViewportMargin()
    {
        Camera camera = movement.PlayCamera;
        Vector3 center = camera.WorldToViewportPoint(transform.position);
        Vector3 corner = camera.WorldToViewportPoint(transform.position + GetComponent<SpriteRenderer>().bounds.extents);
        return new Vector2(Mathf.Abs(corner.x - center.x), Mathf.Abs(corner.y - center.y));
    }

    private void PruneDecisions(float now)
    {
        while (recentDecisions.Count > 0 && recentDecisions.Peek().time < now - 1f)
            recentDecisions.Dequeue();
    }

    private void LogRecentDecisionsOnHit()
    {
        float realNow = Time.realtimeSinceStartup;
        float playbackTime = spawner.SurvivalTime;
        LogIdleDuration(playbackTime, "hit");
        ClearPlan(playbackTime, "hit");
        previousAction = "Stay";
        CancelPendingRequest();
        PruneDecisions(realNow);
        StringBuilder summary = new StringBuilder($"Jev Bot hit: {recentDecisions.Count} successful choices in the previous 1s");
        foreach (DecisionRecord decision in recentDecisions)
            summary.Append($" | -{realNow - decision.time:F2}s {decision.action} ({decision.x:F2}, {decision.y:F2})");
        Camera camera = movement.PlayCamera;
        Vector3 playerPosition = camera.WorldToViewportPoint(transform.position);
        spawner.FillActiveBullets(activeBullets);
        List<JevBulletState> threats = new List<JevBulletState>(activeBullets.Count);
        foreach (TestBullet bullet in activeBullets)
            threats.Add(BuildBulletState(camera, playerPosition, bullet));
        threats.Sort((a, b) => ThreatScore(a).CompareTo(ThreatScore(b)));
        for (int i = 0; i < Mathf.Min(3, threats.Count); i++)
        {
            JevBulletState threat = threats[i];
            summary.Append($" | threat {i + 1}: relativePosition=({threat.relativePosition.x:F3},{threat.relativePosition.y:F3}), relativeVelocity=({threat.relativeVelocity.x:F3},{threat.relativeVelocity.y:F3}), timeToClosestApproach={threat.timeToClosestApproach:F3}s, closestApproachDistance={threat.closestApproachDistance:F3}");
        }
        Debug.Log(summary.ToString());
        if (health.CurrentLives > 0)
        {
            plannedUntil = playbackTime;
            pendingRebase = true;
            if (!buffering)
                SetBuffering(true, playbackTime, 0f);
        }
    }

    private static float ThreatScore(JevBulletState bullet)
    {
        if (!bullet.approaching)
            return bullet.currentDistance;
        return Mathf.Min(bullet.currentDistance, bullet.closestApproachDistance + bullet.timeToClosestApproach * 0.05f);
    }

    private void UpdateBufferedAction(float now)
    {
        while (actionBuffer.Count > 0 && actionBuffer[0].endsAt <= now - DecisionStep)
        {
            actionBuffer.RemoveAt(0);
            if (activeStep >= 0)
                activeStep--;
        }
        int current = -1;
        for (int i = 0; i < actionBuffer.Count; i++)
        {
            if (now >= actionBuffer[i].startsAt && now < actionBuffer[i].endsAt)
            {
                current = i;
                break;
            }
        }
        if (current == activeStep)
        {
            if (current < 0 && idleStartedAt < 0f)
                idleStartedAt = now;
            return;
        }

        float previousEnd = activeStep >= 0 ? actionBuffer[activeStep].endsAt : now;
        ReleaseChoice(now, "step ended");
        activeStep = current;
        if (current < 0)
        {
            if (idleStartedAt < 0f)
                idleStartedAt = previousEnd;
            return;
        }

        PlannedStep step = actionBuffer[current];
        LogIdleDuration(now, "next step");
        movement.BotDirection = step.direction;
        movement.BotInputExpiresAt = Time.time + Mathf.Max(0f, step.endsAt - now);
        choiceAppliedAt = now;
        activeChoice = step.choice;
        Vector3 position = movement.PlayCamera.WorldToViewportPoint(transform.position);
        float realNow = Time.realtimeSinceStartup;
        recentDecisions.Enqueue(new DecisionRecord(realNow, step.choice, position.x, position.y));
        PruneDecisions(realNow);
    }

    private void ClearPlan(float now, string reason)
    {
        ReleaseChoice(now, reason);
        actionBuffer.Clear();
        activeStep = -1;
    }

    private void LogIdleDuration(float now, string reason)
    {
        if (idleStartedAt < 0f)
            return;
        Debug.Log($"Jev Bot stayed idle for {now - idleStartedAt:F3}s after Choice expiry ({reason}).");
        idleStartedAt = -1f;
    }

    private void ReleaseChoice(float now, string reason)
    {
        if (activeChoice != null)
        {
            float appliedSeconds = Mathf.Clamp(now - choiceAppliedAt, 0f, DecisionStep);
            Debug.Log($"Jev Bot choice {activeChoice} applied for {appliedSeconds:F3}s ({reason}).");
            activeChoice = null;
        }
        movement.BotDirection = Vector2.zero;
        movement.BotInputExpiresAt = 0f;
    }

    private void CancelPendingRequest()
    {
        if (requestCoroutine != null)
        {
            StopCoroutine(requestCoroutine);
            requestCoroutine = null;
        }
        if (activeRequest != null)
        {
            activeRequest.Abort();
            activeRequest.Dispose();
            activeRequest = null;
        }
        requestInFlight = false;
    }

    private JevRequest BuildRequest()
    {
        return new JevRequest
        {
            state = BuildState(plannedUntil, spawner.SurvivalTime, virtualPosition,
                virtualRespawnRemaining, virtualInvulnerabilityRemaining, previousAction, false)
        };
    }

    public string BuildSingleStepRequest(float planningTime, Vector2 playerPosition, string lastAction,
        out int existingBulletCount, out int futureBulletCount)
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (spawner == null)
            spawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        JevState state = BuildState(planningTime, planningTime, playerPosition, 0f, 0f, lastAction, true);
        existingBulletCount = state.bullets.Length;
        futureBulletCount = state.futureBullets.Length;
        return JsonUtility.ToJson(new JevSingleStepRequest
        {
            state = state
        });
    }

    private JevState BuildState(float planningTime, float playbackTime, Vector2 playerPosition,
        float respawnRemaining, float invulnerabilityRemaining, string lastAction, bool virtualSchedule)
    {
        Camera camera = movement.PlayCamera;
        Vector3 playerViewport = new Vector3(playerPosition.x, playerPosition.y,
            camera.WorldToViewportPoint(transform.position).z);
        Bounds playerBounds = GetComponent<SpriteRenderer>().bounds;
        Vector3 actualCenter = camera.WorldToViewportPoint(transform.position);
        Vector3 corner = camera.WorldToViewportPoint(transform.position + playerBounds.extents);
        float marginX = Mathf.Abs(corner.x - actualCenter.x);
        float marginY = Mathf.Abs(corner.y - actualCenter.y);
        JevHitSize playerHitSize = GetHitSize(camera, GetComponent<CircleCollider2D>());
        float left = Mathf.Max(marginX, playerHitSize.radiusX);
        float right = 1f - left;
        float bottom = Mathf.Max(marginY, playerHitSize.radiusY);
        float top = 1f - bottom;
        float usableWidth = Mathf.Max(right - left, Mathf.Epsilon);
        float usableHeight = Mathf.Max(top - bottom, Mathf.Epsilon);

        JevBulletState[] bullets;
        if (!virtualSchedule && Mathf.Abs(planningTime - spawner.SurvivalTime) < 0.001f)
        {
            spawner.FillActiveBullets(activeBullets);
            bullets = new JevBulletState[activeBullets.Count];
            for (int i = 0; i < activeBullets.Count; i++)
                bullets[i] = BuildBulletState(camera, playerViewport, activeBullets[i]);
            spawner.FillBotSchedule(planningTime, planningTime + LeadSeconds, futureScheduledBullets);
        }
        else
        {
            spawner.FillBotScheduleForSimulation(planningTime, LeadSeconds,
                scheduledBullets, futureScheduledBullets);
            int count = scheduledBullets.Count;
            bullets = new JevBulletState[count];
            for (int i = 0; i < count; i++)
                bullets[i] = BuildScheduledBulletState(playerViewport, scheduledBullets[i], planningTime);
        }

        JevFutureBullet[] futureBullets = new JevFutureBullet[futureScheduledBullets.Count];
        for (int i = 0; i < futureBullets.Length; i++)
        {
            BulletSpawner.ScheduledBullet bullet = futureScheduledBullets[i];
            futureBullets[i] = new JevFutureBullet
            {
                spawnTime = bullet.spawnTime,
                spawnPosition = new JevPosition { x = bullet.position.x, y = bullet.position.y },
                velocity = new JevPosition { x = bullet.velocity.x, y = bullet.velocity.y },
                hitSize = new JevHitSize { radiusX = bullet.radiusX, radiusY = bullet.radiusY }
            };
        }

        return new JevState
        {
                objective = "Avoid enemy bullets and survive as long as possible.",
                planningGameTime = planningTime,
                playbackGameTime = playbackTime,
                player = new JevPosition { x = playerViewport.x, y = playerViewport.y },
                distanceToLeft = Mathf.Clamp01((playerViewport.x - left) / usableWidth),
                distanceToRight = Mathf.Clamp01((right - playerViewport.x) / usableWidth),
                distanceToBottom = Mathf.Clamp01((playerViewport.y - bottom) / usableHeight),
                distanceToTop = Mathf.Clamp01((top - playerViewport.y) / usableHeight),
                playerNormalSpeed = movement.NormalSpeed,
                decisionIntervalSeconds = DecisionStep,
                inputDurationSeconds = DecisionStep,
                decisionStep = DecisionStep,
                planHorizon = PlanHorizon,
                maxMoveDistancePerDecision = movement.NormalSpeed * DecisionStep,
                playerHitSize = playerHitSize,
                canControl = respawnRemaining <= 0f,
                invincible = invulnerabilityRemaining > 0f,
                remainingRespawnSeconds = respawnRemaining,
                previousAction = lastAction,
                bullets = bullets,
                futureBullets = futureBullets,
                playableArea = new JevPlayableArea
                {
                    left = left,
                    right = right,
                    bottom = bottom,
                    top = top
                }
        };
    }

    private static JevBulletState BuildBulletState(Camera camera, Vector3 playerViewport, TestBullet bullet)
    {
        Vector3 position = camera.WorldToViewportPoint(bullet.transform.position);
        Vector3 velocityEnd = camera.WorldToViewportPoint(bullet.transform.position + bullet.Direction * bullet.Speed);
        return BuildBulletState(new Vector2(playerViewport.x, playerViewport.y),
            new Vector2(position.x, position.y), new Vector2(velocityEnd.x - position.x, velocityEnd.y - position.y),
            bullet.Speed, GetHitSize(camera, bullet.GetComponent<CircleCollider2D>()));
    }

    private static JevBulletState BuildScheduledBulletState(Vector3 playerViewport,
        BulletSpawner.ScheduledBullet bullet, float gameTime)
    {
        Vector2 position = bullet.position + bullet.velocity * (gameTime - bullet.spawnTime);
        return BuildBulletState(new Vector2(playerViewport.x, playerViewport.y), position, bullet.velocity,
            bullet.speed, new JevHitSize { radiusX = bullet.radiusX, radiusY = bullet.radiusY });
    }

    private static JevBulletState BuildBulletState(Vector2 playerPosition, Vector2 position,
        Vector2 velocity, float speed, JevHitSize hitSize)
    {
        Vector2 relativePosition = position - playerPosition;
        Vector2 relativeVelocity = velocity;
        float velocitySquared = relativeVelocity.sqrMagnitude;
        bool approaching = velocitySquared > 0.00000001f && Vector2.Dot(relativePosition, relativeVelocity) < 0f;
        float timeToClosestApproach = approaching
            ? -Vector2.Dot(relativePosition, relativeVelocity) / velocitySquared
            : -1f;
        float closestApproachDistance = approaching
            ? (relativePosition + relativeVelocity * timeToClosestApproach).magnitude
            : relativePosition.magnitude;

        return new JevBulletState
        {
            x = position.x,
            y = position.y,
            velocityX = relativeVelocity.x,
            velocityY = relativeVelocity.y,
            speed = speed,
            hitSize = hitSize,
            relativePosition = new JevPosition { x = relativePosition.x, y = relativePosition.y },
            relativeVelocity = new JevPosition { x = relativeVelocity.x, y = relativeVelocity.y },
            currentDistance = relativePosition.magnitude,
            approaching = approaching,
            timeToClosestApproach = timeToClosestApproach,
            closestApproachDistance = closestApproachDistance
        };
    }

    private static JevHitSize GetHitSize(Camera camera, CircleCollider2D collider)
    {
        Bounds bounds = collider.bounds;
        Vector3 center = camera.WorldToViewportPoint(bounds.center);
        Vector3 edge = camera.WorldToViewportPoint(bounds.center + bounds.extents);
        return new JevHitSize
        {
            radiusX = Mathf.Abs(edge.x - center.x),
            radiusY = Mathf.Abs(edge.y - center.y)
        };
    }

    private void FailBot(string message)
    {
        if (failed)
            return;

        failed = true;
        requestInFlight = false;
        float now = spawner.SurvivalTime;
        LogIdleDuration(now, "API error");
        ClearPlan(now, "API error");
        Debug.LogError(message);
        Time.timeScale = 1f;
        if (statusText != null)
            statusText.text = "";
        health.EndForApiError(message);
    }

    private static bool TryReadDirection(string choice, out Vector2 direction)
    {
        direction = Vector2.zero;
        switch (choice)
        {
            case "Stay": return true;
            case "Up": direction = Vector2.up; return true;
            case "Down": direction = Vector2.down; return true;
            case "Left": direction = Vector2.left; return true;
            case "Right": direction = Vector2.right; return true;
            case "UpLeft": direction = new Vector2(-1f, 1f); return true;
            case "UpRight": direction = new Vector2(1f, 1f); return true;
            case "DownLeft": direction = new Vector2(-1f, -1f); return true;
            case "DownRight": direction = new Vector2(1f, -1f); return true;
            default: return false;
        }
    }

    private readonly struct DecisionRecord
    {
        public readonly float time;
        public readonly string action;
        public readonly float x;
        public readonly float y;

        public DecisionRecord(float time, string action, float x, float y)
        {
            this.time = time;
            this.action = action;
            this.x = x;
            this.y = y;
        }
    }

    private readonly struct PlannedStep
    {
        public readonly string choice;
        public readonly Vector2 direction;
        public readonly float startsAt;
        public readonly float endsAt;

        public PlannedStep(string choice, Vector2 direction, float startsAt, float endsAt)
        {
            this.choice = choice;
            this.direction = direction;
            this.startsAt = startsAt;
            this.endsAt = endsAt;
        }
    }

    [Serializable]
    private sealed class JevRequest
    {
        public string model = Model;
        public JevState state;
        public JevQuestions questions = new JevQuestions();
    }

    [Serializable]
    private sealed class JevSingleStepRequest
    {
        public string model = Model;
        public JevState state;
        public JevSingleStepQuestions questions = new JevSingleStepQuestions();
    }

    [Serializable]
    private sealed class JevSingleStepQuestions
    {
        public JevChoiceQuestion move0 = new JevChoiceQuestion(0);
    }

    [Serializable]
    private sealed class JevState
    {
        public string objective;
        public float planningGameTime;
        public float playbackGameTime;
        public JevPosition player;
        public float distanceToLeft;
        public float distanceToRight;
        public float distanceToBottom;
        public float distanceToTop;
        public float playerNormalSpeed;
        public float decisionIntervalSeconds;
        public float inputDurationSeconds;
        public float decisionStep;
        public float planHorizon;
        public float maxMoveDistancePerDecision;
        public JevHitSize playerHitSize;
        public bool canControl;
        public bool invincible;
        public float remainingRespawnSeconds;
        public string previousAction;
        public JevBulletState[] bullets;
        public JevFutureBullet[] futureBullets;
        public JevPlayableArea playableArea;
    }

    [Serializable]
    private sealed class JevFutureBullet
    {
        public float spawnTime;
        public JevPosition spawnPosition;
        public JevPosition velocity;
        public JevHitSize hitSize;
    }

    [Serializable]
    private sealed class JevPosition
    {
        public float x;
        public float y;
    }

    [Serializable]
    private sealed class JevBulletState
    {
        public float x;
        public float y;
        public float velocityX;
        public float velocityY;
        public float speed;
        public JevHitSize hitSize;
        public JevPosition relativePosition;
        public JevPosition relativeVelocity;
        public float currentDistance;
        public bool approaching;
        public float timeToClosestApproach;
        public float closestApproachDistance;
    }

    [Serializable]
    private sealed class JevHitSize
    {
        public float radiusX;
        public float radiusY;
    }

    [Serializable]
    private sealed class JevPlayableArea
    {
        public float left;
        public float right;
        public float bottom;
        public float top;
    }

    [Serializable]
    private sealed class JevQuestions
    {
        public JevChoiceQuestion move0 = new JevChoiceQuestion(0);
        public JevChoiceQuestion move1 = new JevChoiceQuestion(1);
        public JevChoiceQuestion move2 = new JevChoiceQuestion(2);
        public JevChoiceQuestion move3 = new JevChoiceQuestion(3);
        public JevChoiceQuestion move4 = new JevChoiceQuestion(4);
        public JevChoiceQuestion move5 = new JevChoiceQuestion(5);
    }

    [Serializable]
    private sealed class JevChoiceQuestion
    {
        public string type = "choice";
        public string instructions;
        public JevCriteria criteria = new JevCriteria();

        public JevChoiceQuestion(int step)
        {
            instructions = $"Avoid enemy bullets and survive as long as possible. You know the next 5 seconds of bullet spawns in futureBullets; predict positions from spawnTime, spawnPosition and velocity. Prioritize immediate survival and preserve future escape routes. Do not move toward a bullet on a near-future collision course. A timeToClosestApproach of -1 means receding or stationary. Choose the movement for game time planningGameTime+{step * DecisionStep:F1} to +{(step + 1) * DecisionStep:F1} seconds.";
        }
    }

    [Serializable]
    private sealed class JevCriteria
    {
        public string Stay = "Remain still.";
        public string Up = "Move up.";
        public string Down = "Move down.";
        public string Left = "Move left.";
        public string Right = "Move right.";
        public string UpLeft = "Move diagonally up and left.";
        public string UpRight = "Move diagonally up and right.";
        public string DownLeft = "Move diagonally down and left.";
        public string DownRight = "Move diagonally down and right.";
    }

    [Serializable]
    private sealed class JevResponse
    {
        public JevAnswers answers;
    }

    [Serializable]
    private sealed class JevAnswers
    {
        public JevChoiceAnswer move0;
        public JevChoiceAnswer move1;
        public JevChoiceAnswer move2;
        public JevChoiceAnswer move3;
        public JevChoiceAnswer move4;
        public JevChoiceAnswer move5;

        public JevChoiceAnswer[] ToArray() => new[] { move0, move1, move2, move3, move4, move5 };
    }

    [Serializable]
    private sealed class JevChoiceAnswer
    {
        public string type;
        public string choice;
    }
}
