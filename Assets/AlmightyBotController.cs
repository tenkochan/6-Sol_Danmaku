using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerMovement), typeof(PlayerHealth))]
public sealed class AlmightyBotController : MonoBehaviour
{
    private const string Endpoint = "https://jev-ai.pro/api/v1/systemone";
    public const float ScheduleSeconds = 60f;
    public const float ActionSeconds = 0.2f;
    public const int ActionCount = 200;
    private readonly string[] actions = new string[ActionCount];
    private readonly List<BulletSpawner.ScheduledBullet> schedule = new List<BulletSpawner.ScheduledBullet>();
    private PlayerMovement movement;
    private PlayerHealth health;
    private BulletSpawner spawner;
    private Text statusText;
    private GameObject planningGroup;
    private RectTransform progressFill;
    private Text progressText;
    private Text planningErrorText;
    private bool planReady;
    private bool playing;
    private bool failed;
    private float countdown = 5f;
    private int loggedStep = -1;
    private Vector3 loggedStepStartPosition;
    private float loggedStepStartTime;
    private Vector2 loggedDirection;
    private string loggedRawAction;

    private void Start()
    {
        health = GetComponent<PlayerHealth>();
        movement = GetComponent<PlayerMovement>();
        spawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        movement.UseBotInput = true;
        movement.BotDirection = Vector2.zero;
        movement.BotInputExpiresAt = 0f;
        Time.timeScale = 0f;
        spawner.EnableAlmightySchedule();
        spawner.FillBotScheduleThrough(ScheduleSeconds, schedule);
        if (AlmightyChallengeExchange.PendingExport)
        {
            AlmightyChallengeExchange.PendingExport = false;
            AlmightyChallengeExchange.Export(movement, schedule, out string exportMessage);
            Debug.Log(exportMessage);
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            return;
        }
        CreateStatusOverlay();
        health.Hit += LogHit;
        if (AlmightyChallengeExchange.HasSelectedAnswer)
        {
            if (!AlmightyChallengeExchange.MatchesScene(AlmightyChallengeExchange.SelectedChallenge,
                    movement, out string mismatch))
            {
                Fail(mismatch);
                return;
            }
            spawner.LoadChallengeSchedule(AlmightyChallengeExchange.DecodeSchedule(
                AlmightyChallengeExchange.SelectedChallenge, movement.PlayCamera));
            Array.Copy(AlmightyChallengeExchange.SelectedAnswer.actions, actions, ActionCount);
            planReady = true;
            planningGroup.SetActive(false);
            countdown = 0f;
            Debug.Log($"External Almighty plan loaded: {AlmightyChallengeExchange.SelectedAnswer.name}, "
                + $"challengeId={AlmightyChallengeExchange.SelectedChallenge.challengeId}, actions={ActionCount}, "
                + $"bullets={AlmightyChallengeExchange.SelectedChallenge.bulletCount}.");
            return;
        }
        Debug.Log($"Almighty Bot generated {schedule.Count} bullets for game time 0-60s.");
        if (!JevLocalSettings.TryGetApiKey(out string key))
        {
            Fail("Almighty Bot could not load the local Jev API key.");
            return;
        }
        StartCoroutine(PreparePlan(key));
    }

    private void OnDestroy()
    {
        if (health != null)
            health.Hit -= LogHit;
        if (health != null && health.IsAlmighty)
            Time.timeScale = 1f;
    }

    private void Update()
    {
        if (failed || health.CurrentLives == 0 || !planReady)
            return;

        if (!playing)
        {
            countdown -= Time.unscaledDeltaTime;
            if (countdown > 0f)
            {
                statusText.text = Mathf.CeilToInt(countdown).ToString();
                return;
            }
            playing = true;
            statusText.text = "";
            health.MarkAlmightyGameplayStarted();
            Time.timeScale = 1f;
            Debug.Log($"Almighty Bot playback started at game time {spawner.SurvivalTime:F3}s.");
        }

        int step = Mathf.FloorToInt(spawner.SurvivalTime / ActionSeconds);
        if (step != loggedStep)
        {
            LogCompletedPlaybackStep();
            loggedStep = step;
            if (step >= 0 && step < 10)
            {
                loggedStepStartTime = spawner.SurvivalTime;
                loggedStepStartPosition = transform.position;
                loggedRawAction = actions[step];
                TryDirection(loggedRawAction, out loggedDirection);
            }
        }
        if (step < 0 || step >= ActionCount)
        {
            movement.BotDirection = Vector2.zero;
            movement.BotInputExpiresAt = 0f;
            return;
        }
        TryDirection(actions[step], out Vector2 direction);
        movement.BotDirection = direction;
        float remaining = (step + 1) * ActionSeconds - spawner.SurvivalTime;
        movement.BotInputExpiresAt = Time.time + Mathf.Max(0f, remaining);
    }

    private void LogCompletedPlaybackStep()
    {
        if (loggedStep < 0 || loggedStep >= 10)
            return;
        Debug.Log($"Almighty playback action: gameTime={loggedStepStartTime:F3}s, actionIndex={loggedStep}, "
            + $"rawAction={loggedRawAction}, decodedMovement=({loggedDirection.x:F3},{loggedDirection.y:F3}), "
            + $"playerPosition=({loggedStepStartPosition.x:F3},{loggedStepStartPosition.y:F3}) -> "
            + $"({transform.position.x:F3},{transform.position.y:F3}), "
            + $"UseBotInput={movement.UseBotInput}, CanMove={movement.CanMove}.");
    }

    private IEnumerator PreparePlan(string key)
    {
        JevBotController realtimeBot = GetComponent<JevBotController>();
        if (realtimeBot == null)
        {
            Fail("Almighty Bot could not find the shared Jev request builder.");
            yield break;
        }
        string startedAt = DateTimeOffset.Now.ToString("o");
        Debug.Log($"Almighty Bot plan started at {startedAt}; payload bullet count={schedule.Count}.");
        float requestStart = Time.realtimeSinceStartup;
        Vector3 plannedPosition = transform.position;
        string previousAction = "Stay";
        for (int step = 0; step < ActionCount; step++)
        {
            Vector3 viewport = movement.PlayCamera.WorldToViewportPoint(plannedPosition);
            string requestJson = realtimeBot.BuildSingleStepRequest(step * ActionSeconds,
                new Vector2(viewport.x, viewport.y), previousAction,
                out int existingBulletCount, out int futureBulletCount);
            if (step == 0)
                LogFirstRequestQuestions(requestJson);
            int byteCount = Encoding.UTF8.GetByteCount(requestJson);
            int stateByteCount = GetStateByteCount(requestJson);
            if (step == 0 || step == 50 || step == 100 || step == 150 || step == ActionCount - 1)
                Debug.Log($"Almighty planning request step={step}: request UTF-8 bytes={byteCount}, "
                    + $"state UTF-8 bytes={stateByteCount}, existing bullets={existingBulletCount}, "
                    + $"future bullets={futureBulletCount}, total bullets={existingBulletCount + futureBulletCount}.");
            if (byteCount > 256 * 1024)
            {
                Fail($"Almighty Bot request exceeds the Jev API 256 KB body limit ({byteCount} bytes).");
                yield break;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
            using (UnityWebRequest request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                UnityWebRequestAsyncOperation operation = null;
                try
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", "Bearer " + key);
                    request.timeout = 120;
                    operation = request.SendWebRequest();
                }
                catch (Exception)
                {
                    Fail($"Almighty Bot could not start Jev request for step {step}.");
                }
                if (failed)
                    yield break;
                yield return operation;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 400 && attempt == 0)
                    {
                        Debug.LogWarning($"Almighty Bot Jev HTTP 400 at step {step}; retrying the same request once.");
                        yield return new WaitForSecondsRealtime(1f);
                        continue;
                    }
                    if (request.responseCode < 200 || request.responseCode >= 300)
                        LogHttpFailure(request, key, requestJson, byteCount, step);
                    Fail($"Almighty Bot Jev request for step {step} failed (HTTP {request.responseCode}).");
                    yield break;
                }

                BatchResponse response;
                try
                {
                    response = JsonUtility.FromJson<BatchResponse>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                    Fail($"Almighty Bot Jev response for step {step} could not be parsed.");
                    yield break;
                }
                ChoiceAnswer answer = response?.answers?.move0;
                if (step == 0)
                    LogFirstResponseAnswer(request.downloadHandler.text);
                if (answer == null || answer.type != "choice" || !TryDirection(answer.choice, out Vector2 direction))
                {
                    Fail($"Almighty Bot Jev response has an invalid action at step {step}.");
                    yield break;
                }
                actions[step] = answer.choice;
                previousAction = answer.choice;
                Vector3 nextPosition = SimulateMovement(plannedPosition, direction);
                Debug.Log($"Almighty planning step={step}, time={step * ActionSeconds:F1}s, "
                    + $"playerPosition=({plannedPosition.x:F4},{plannedPosition.y:F4}), "
                    + $"selectedAction={answer.choice}, nextPlayerPosition=({nextPosition.x:F4},{nextPosition.y:F4}).");
                plannedPosition = nextPosition;
                UpdatePlanningProgress(step + 1);
                break;
            }
            }
        }

        planReady = true;
        planningGroup.SetActive(false);
        statusText.text = "5";
        int movingActions = 0;
        for (int i = 0; i < ActionCount; i++)
        {
            if (actions[i] != "Stay")
                movingActions++;
        }
        if (movingActions == 0)
            Debug.LogWarning($"Almighty Bot received {ActionCount} Stay actions; playback will keep the player stationary.");
        string[] actionNames = { "Stay", "Up", "Down", "Left", "Right", "UpLeft", "UpRight", "DownLeft", "DownRight" };
        StringBuilder actionCounts = new StringBuilder($"Almighty Bot {ActionCount}-action distribution: ");
        for (int nameIndex = 0; nameIndex < actionNames.Length; nameIndex++)
        {
            if (nameIndex > 0)
                actionCounts.Append(", ");
            int count = 0;
            for (int step = 0; step < ActionCount; step++)
            {
                if (actions[step] == actionNames[nameIndex])
                    count++;
            }
            actionCounts.Append(actionNames[nameIndex]).Append('=').Append(count);
        }
        Debug.Log(actionCounts.ToString());
        Debug.Log($"Almighty Bot plan ended at {DateTimeOffset.Now:o}; latency={Time.realtimeSinceStartup - requestStart:F3}s, returned action steps={ActionCount}.");
    }

    private Vector3 SimulateMovement(Vector3 position, Vector2 direction)
    {
        Camera camera = movement.PlayCamera;
        Vector3 candidate = position + (Vector3)(direction.normalized * movement.NormalSpeed * ActionSeconds);
        Vector3 center = camera.WorldToViewportPoint(candidate);
        Vector3 extents = GetComponent<SpriteRenderer>().bounds.extents;
        Vector3 corner = camera.WorldToViewportPoint(candidate + extents);
        Vector3 oppositeCorner = camera.WorldToViewportPoint(candidate - extents);
        float extentX = Mathf.Max(Mathf.Abs(corner.x - center.x), Mathf.Abs(oppositeCorner.x - center.x));
        float extentY = Mathf.Max(Mathf.Abs(corner.y - center.y), Mathf.Abs(oppositeCorner.y - center.y));
        center.x = Mathf.Clamp(center.x, extentX, 1f - extentX);
        center.y = Mathf.Clamp(center.y, extentY, 1f - extentY);
        return camera.ViewportToWorldPoint(center);
    }

    private static void LogFirstRequestQuestions(string requestJson)
    {
        if (TryExtractJsonObject(requestJson, "move0", out string question))
            Debug.Log($"Almighty first request question move0 (including all 9 choices): {question}");
    }

    private static void LogFirstResponseAnswer(string responseJson)
    {
        if (TryExtractJsonObject(responseJson, "move0", out string rawAnswer))
            Debug.Log($"Almighty first response raw answer move0: {rawAnswer}");
    }

    private static bool TryExtractJsonObject(string json, string property, out string value)
    {
        value = null;
        int at = json.IndexOf("\"" + property + "\":", StringComparison.Ordinal);
        if (at < 0)
            return false;
        int start = json.IndexOf('{', at + property.Length + 3);
        if (start < 0)
            return false;
        int depth = 0;
        bool quoted = false;
        bool escaped = false;
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '{') depth++;
            if (c != '}') continue;
            depth--;
            if (depth != 0) continue;
            value = json.Substring(start, i - start + 1);
            return true;
        }
        return false;
    }

    private void LogHttpFailure(UnityWebRequest request, string key, string requestJson,
        int requestBytes, int step)
    {
        string responseBody = request.downloadHandler != null ? request.downloadHandler.text : "";
        if (string.IsNullOrEmpty(responseBody))
            responseBody = "(empty)";
        else if (responseBody.Contains("\"bullets\"") && responseBody.Contains("\"questions\""))
            responseBody = "[response body omitted because it contains the request payload]";
        else
        {
            if (!string.IsNullOrEmpty(key))
                responseBody = responseBody.Replace(key, "[REDACTED]");
            int authorizationAt = responseBody.IndexOf("Authorization", StringComparison.OrdinalIgnoreCase);
            if (authorizationAt >= 0)
                responseBody = responseBody.Substring(0, authorizationAt) + "[Authorization header omitted]";
        }

        Debug.LogError($"Almighty Bot Jev HTTP {request.responseCode} at step {step}. Response body: {responseBody}");

        const string stateMarker = "\"state\":";
        const string questionsMarker = ",\"questions\":{";
        int stateStart = requestJson.IndexOf(stateMarker, StringComparison.Ordinal);
        int questionsStart = requestJson.IndexOf(questionsMarker, StringComparison.Ordinal);
        if (stateStart < 0 || questionsStart <= stateStart)
        {
            Debug.LogError($"Almighty Bot request diagnostics: request UTF-8 bytes={requestBytes}; state/questions boundaries could not be read.");
            return;
        }

        stateStart += stateMarker.Length;
        int stateBytes = Encoding.UTF8.GetByteCount(requestJson.Substring(stateStart, questionsStart - stateStart));
        int questionCount = 0;
        int longestQuestionBytes = 0;
        int largestOptionCount = 0;
        StringBuilder optionCounts = new StringBuilder(12);
        int searchFrom = questionsStart + questionsMarker.Length;
        for (int i = 0; i < 1; i++)
        {
            string questionKey = "\"move" + i + "\":{";
            int questionAt = requestJson.IndexOf(questionKey, searchFrom, StringComparison.Ordinal);
            if (questionAt < 0)
                break;
            int criteriaAt = requestJson.IndexOf("\"criteria\":{", questionAt, StringComparison.Ordinal);
            if (criteriaAt < 0)
                break;
            int optionStart = criteriaAt + "\"criteria\":{".Length;
            int optionEnd = requestJson.IndexOf('}', optionStart);
            if (optionEnd < 0 || optionEnd + 1 >= requestJson.Length)
                break;
            int optionCount = 0;
            for (int cursor = optionStart; cursor < optionEnd; cursor++)
            {
                if (requestJson[cursor] == ':')
                    optionCount++;
            }
            if (i > 0)
                optionCounts.Append(", ");
            optionCounts.Append("move").Append(i).Append('=').Append(optionCount);
            largestOptionCount = Mathf.Max(largestOptionCount, optionCount);
            questionCount++;
            longestQuestionBytes = Mathf.Max(longestQuestionBytes,
                Encoding.UTF8.GetByteCount(requestJson.Substring(questionAt, optionEnd + 2 - questionAt)));
            searchFrom = optionEnd + 2;
        }

        int combinedBytes = stateBytes + longestQuestionBytes;
        int lowTokenEstimate = Mathf.CeilToInt(combinedBytes / 4f);
        int highTokenEstimate = Mathf.CeilToInt(combinedBytes / 2f);
        Debug.LogError($"Almighty Bot request diagnostics: request UTF-8 bytes={requestBytes} (<=262144: {requestBytes <= 256 * 1024}); "
            + $"state UTF-8 bytes={stateBytes}; questions={questionCount} (<=64: {questionCount <= 64}); "
            + $"state + longest question UTF-8 bytes={combinedBytes}, rough token range={lowTokenEstimate}-{highTokenEstimate} "
            + $"(32k token limit; actual Jev tokenizer count unknown). "
            + $"Choice option counts: {optionCounts} (all <=255: {largestOptionCount <= 255}).");
    }

    private static int GetStateByteCount(string requestJson)
    {
        const string stateMarker = "\"state\":";
        const string questionsMarker = ",\"questions\":{";
        int start = requestJson.IndexOf(stateMarker, StringComparison.Ordinal);
        if (start < 0)
            return -1;
        start += stateMarker.Length;
        int end = requestJson.IndexOf(questionsMarker, start, StringComparison.Ordinal);
        return end >= start ? Encoding.UTF8.GetByteCount(requestJson.Substring(start, end - start)) : -1;
    }

    public static bool IsValidAction(string action) => TryDirection(action, out _);

    private static bool TryDirection(string choice, out Vector2 direction)
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

    private void LogHit()
    {
        Debug.Log($"Almighty Bot hit at game time {spawner.SurvivalTime:F3}s; lives remaining={health.CurrentLives}.");
    }

    private void Fail(string reason)
    {
        if (failed)
            return;
        failed = true;
        Debug.LogError(reason);
        movement.BotDirection = Vector2.zero;
        movement.BotInputExpiresAt = 0f;
        if (statusText != null)
            statusText.text = "";
        if (planningErrorText != null && !planReady)
            planningErrorText.text = "PLANNING ERROR";
        Time.timeScale = 1f;
        health.EndForApiError();
    }

    private void CreateStatusOverlay()
    {
        GameObject canvasObject = new GameObject("Almighty Status Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        GameObject label = new GameObject("Almighty Status", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1100f, 160f);
        statusText = label.GetComponent<Text>();
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 80;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = Color.white;
        statusText.raycastTarget = false;
        statusText.text = "";

        planningGroup = new GameObject("Almighty Planning", typeof(RectTransform));
        planningGroup.transform.SetParent(canvasObject.transform, false);
        RectTransform groupRect = planningGroup.GetComponent<RectTransform>();
        groupRect.anchorMin = groupRect.anchorMax = new Vector2(0.5f, 0.5f);
        groupRect.sizeDelta = new Vector2(900f, 360f);
        groupRect.anchoredPosition = new Vector2(0f, 90f);
        CreatePlanningText("The Almighty", planningGroup.transform, 80,
            new Vector2(0f, 90f), new Vector2(850f, 110f));

        GameObject bar = new GameObject("Planning Bar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        bar.transform.SetParent(planningGroup.transform, false);
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(650f, 36f);
        barRect.anchoredPosition = new Vector2(0f, -10f);
        bar.GetComponent<UnityEngine.UI.Image>().color = new Color(0.14f, 0.17f, 0.22f, 0.95f);

        GameObject fill = new GameObject("Planning Bar Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        fill.transform.SetParent(bar.transform, false);
        progressFill = fill.GetComponent<RectTransform>();
        progressFill.anchorMin = new Vector2(0f, 0f);
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressFill.pivot = new Vector2(0f, 0.5f);
        progressFill.anchoredPosition = Vector2.zero;
        fill.GetComponent<UnityEngine.UI.Image>().color = new Color(0.25f, 0.8f, 1f, 1f);

        progressText = CreatePlanningText("0%", planningGroup.transform, 48,
            new Vector2(0f, -80f), new Vector2(400f, 70f));
        planningErrorText = CreatePlanningText("", planningGroup.transform, 32,
            new Vector2(0f, -135f), new Vector2(650f, 55f));
        planningErrorText.color = new Color(1f, 0.45f, 0.45f);
        UpdatePlanningProgress(0);
    }

    private Text CreatePlanningText(string value, Transform parent, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject label = new GameObject(value, typeof(RectTransform), typeof(Text));
        label.transform.SetParent(parent, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Text labelText = label.GetComponent<Text>();
        labelText.font = statusText.font;
        labelText.fontSize = fontSize;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;
        labelText.raycastTarget = false;
        labelText.text = value;
        return labelText;
    }

    private void UpdatePlanningProgress(int completedSteps)
    {
        float progress = Mathf.Clamp01(completedSteps / (float)ActionCount);
        progressFill.sizeDelta = new Vector2(650f * progress, 0f);
        progressText.text = $"{Mathf.RoundToInt(progress * 100f)}%";
    }

    [Serializable]
    private sealed class BatchResponse
    {
        public BatchAnswers answers;
    }

    [Serializable]
    private sealed class BatchAnswers
    {
        public ChoiceAnswer move0;
    }

    [Serializable]
    private sealed class ChoiceAnswer
    {
        public string type;
        public string choice;
    }
}
