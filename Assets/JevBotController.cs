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

    private readonly List<TestBullet> activeBullets = new List<TestBullet>();
    private readonly Queue<DecisionRecord> recentDecisions = new Queue<DecisionRecord>();
    private readonly List<PlannedStep> actionBuffer = new List<PlannedStep>(PlanSteps);
    private PlayerMovement movement;
    private PlayerHealth health;
    private BulletSpawner spawner;
    private string apiKey;
    private bool requestInFlight;
    private bool wasControllable;
    private bool failed;
    private string previousAction = "Stay";
    private int activeStep = -1;
    private string activeChoice;
    private float choiceAppliedAt;
    private float idleStartedAt = -1f;
    private Coroutine requestCoroutine;
    private UnityWebRequest activeRequest;

    private void Start()
    {
        health = GetComponent<PlayerHealth>();
        if (health.Mode != PlayMode.Bot)
        {
            enabled = false;
            return;
        }

        movement = GetComponent<PlayerMovement>();
        movement.UseBotInput = true;
        movement.BotDirection = Vector2.zero;
        movement.BotInputExpiresAt = 0f;
        spawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        health.Hit += LogRecentDecisionsOnHit;
        if (!JevLocalSettings.TryGetApiKey(out apiKey))
            FailBot("Local Jev API key is unavailable.");
    }

    private void OnDestroy()
    {
        if (health != null)
            health.Hit -= LogRecentDecisionsOnHit;
    }

    private void Update()
    {
        if (failed || health.CurrentLives == 0)
            return;

        float now = Time.realtimeSinceStartup;
        if (!movement.CanMove || health.IsRespawning)
        {
            if (wasControllable)
            {
                wasControllable = false;
                ClearPlan(now, "control disabled");
                CancelPendingRequest();
            }
            if (health.IsRespawning && !requestInFlight)
                StartDecisionRequest();
            return;
        }

        if (!wasControllable)
        {
            wasControllable = true;
            int expiredDuringRespawn = actionBuffer.RemoveAll(step => now >= step.endsAt);
            activeStep = -1;
            if (expiredDuringRespawn > 0)
                Debug.Log($"Jev Bot discarded {expiredDuringRespawn} plan steps before control returned.");
        }
        UpdateBufferedAction(now);

        if (!requestInFlight)
            StartDecisionRequest();
    }

    private void StartDecisionRequest()
    {
        requestInFlight = true;
        requestCoroutine = StartCoroutine(RequestDecision());
    }

    private IEnumerator RequestDecision()
    {
        string requestedAt = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        Vector3 requestPosition = movement.PlayCamera.WorldToViewportPoint(transform.position);
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
                float startsAt = requestStartedAt + i * DecisionStep;
                newPlan[i] = new PlannedStep(choices[i].choice, direction, startsAt, startsAt + DecisionStep);
            }

            float now = Time.realtimeSinceStartup;
            int expired = 0;
            ClearPlan(now, "new plan");
            foreach (PlannedStep step in newPlan)
            {
                if (now >= step.endsAt)
                    expired++;
                else
                    actionBuffer.Add(step);
            }
            if (movement.CanMove && !health.IsRespawning)
                UpdateBufferedAction(now);
            Debug.Log($"Jev Bot plan request {requestedAt}, latency {now - requestStartedAt:F3}s, expired steps {expired}, executable steps {actionBuffer.Count}, player viewport ({requestPosition.x:F3}, {requestPosition.y:F3}).");
        }

        activeRequest = null;
        requestCoroutine = null;
        requestInFlight = false;
        if (!failed && health.CurrentLives > 0)
            StartDecisionRequest();
    }

    private void PruneDecisions(float now)
    {
        while (recentDecisions.Count > 0 && recentDecisions.Peek().time < now - 1f)
            recentDecisions.Dequeue();
    }

    private void LogRecentDecisionsOnHit()
    {
        float now = Time.realtimeSinceStartup;
        LogIdleDuration(now, "hit");
        ClearPlan(now, "hit");
        previousAction = "Stay";
        wasControllable = false;
        CancelPendingRequest();
        PruneDecisions(now);
        StringBuilder summary = new StringBuilder($"Jev Bot hit: {recentDecisions.Count} successful choices in the previous 1s");
        foreach (DecisionRecord decision in recentDecisions)
            summary.Append($" | -{now - decision.time:F2}s {decision.action} ({decision.x:F2}, {decision.y:F2})");
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
    }

    private static float ThreatScore(JevBulletState bullet)
    {
        if (!bullet.approaching)
            return bullet.currentDistance;
        return Mathf.Min(bullet.currentDistance, bullet.closestApproachDistance + bullet.timeToClosestApproach * 0.05f);
    }

    private void UpdateBufferedAction(float now)
    {
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
        movement.BotInputExpiresAt = step.endsAt;
        choiceAppliedAt = now;
        activeChoice = step.choice;
        previousAction = step.choice;
        Vector3 position = movement.PlayCamera.WorldToViewportPoint(transform.position);
        recentDecisions.Enqueue(new DecisionRecord(now, step.choice, position.x, position.y));
        PruneDecisions(now);
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
        Camera camera = movement.PlayCamera;
        Vector3 playerViewport = camera.WorldToViewportPoint(transform.position);
        Bounds playerBounds = GetComponent<SpriteRenderer>().bounds;
        Vector3 corner = camera.WorldToViewportPoint(transform.position + playerBounds.extents);
        float marginX = Mathf.Abs(corner.x - playerViewport.x);
        float marginY = Mathf.Abs(corner.y - playerViewport.y);
        JevHitSize playerHitSize = GetHitSize(camera, GetComponent<CircleCollider2D>());
        float left = Mathf.Max(marginX, playerHitSize.radiusX);
        float right = 1f - left;
        float bottom = Mathf.Max(marginY, playerHitSize.radiusY);
        float top = 1f - bottom;
        float usableWidth = Mathf.Max(right - left, Mathf.Epsilon);
        float usableHeight = Mathf.Max(top - bottom, Mathf.Epsilon);

        spawner.FillActiveBullets(activeBullets);
        JevBulletState[] bullets = new JevBulletState[activeBullets.Count];
        for (int i = 0; i < activeBullets.Count; i++)
            bullets[i] = BuildBulletState(camera, playerViewport, activeBullets[i]);

        return new JevRequest
        {
            state = new JevState
            {
                objective = "Avoid enemy bullets and survive as long as possible.",
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
                canControl = movement.CanMove,
                invincible = health.IsInvincible,
                remainingRespawnSeconds = health.RemainingRespawnTime,
                previousAction = previousAction,
                bullets = bullets,
                playableArea = new JevPlayableArea
                {
                    left = left,
                    right = right,
                    bottom = bottom,
                    top = top
                }
            }
        };
    }

    private static JevBulletState BuildBulletState(Camera camera, Vector3 playerViewport, TestBullet bullet)
    {
        Vector3 position = camera.WorldToViewportPoint(bullet.transform.position);
        Vector3 velocityEnd = camera.WorldToViewportPoint(bullet.transform.position + bullet.Direction * bullet.Speed);
        Vector2 relativePosition = new Vector2(position.x - playerViewport.x, position.y - playerViewport.y);
        Vector2 relativeVelocity = new Vector2(velocityEnd.x - position.x, velocityEnd.y - position.y);
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
            speed = bullet.Speed,
            hitSize = GetHitSize(camera, bullet.GetComponent<CircleCollider2D>()),
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
        float now = Time.realtimeSinceStartup;
        LogIdleDuration(now, "API error");
        ClearPlan(now, "API error");
        Debug.LogError(message);
        health.EndForApiError();
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
    private sealed class JevState
    {
        public string objective;
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
        public JevPlayableArea playableArea;
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
            instructions = $"Avoid enemy bullets and survive as long as possible. Immediate collision avoidance is the highest priority. Do not move toward a bullet on a collision course in the near future. A timeToClosestApproach of -1 means the bullet is receding or stationary. Preserve escape space. Avoid repeatedly moving toward a screen boundary when doing so would significantly reduce future escape directions, unless necessary to avoid an immediate collision. Choose the movement for t+{step * DecisionStep:F1} to t+{(step + 1) * DecisionStep:F1} seconds, relative to this request.";
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
