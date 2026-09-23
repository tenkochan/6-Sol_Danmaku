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
    private const float DecisionInterval = 0.2f;

    private readonly List<TestBullet> activeBullets = new List<TestBullet>();
    private PlayerMovement movement;
    private PlayerHealth health;
    private BulletSpawner spawner;
    private string apiKey;
    private float decisionElapsed;
    private bool requestInFlight;
    private bool failed;
    private bool loggedFirstSuccess;

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
        spawner = movement.PlayCamera.GetComponent<BulletSpawner>();
        if (!JevLocalSettings.TryGetApiKey(out apiKey))
            FailBot("Local Jev API key is unavailable.");
    }

    private void Update()
    {
        if (failed || health.CurrentLives == 0 || !movement.CanMove)
            return;

        decisionElapsed += Time.deltaTime;
        if (requestInFlight || decisionElapsed < DecisionInterval)
            return;

        decisionElapsed = 0f;
        requestInFlight = true;
        StartCoroutine(RequestDecision());
    }

    private IEnumerator RequestDecision()
    {
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
            float requestStartedAt = Time.realtimeSinceStartup;
            UnityWebRequestAsyncOperation operation = null;
            try
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                request.timeout = 5;
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

            if (response?.answers?.move == null || response.answers.move.type != "choice"
                || !TryReadDirection(response.answers.move.choice, out Vector2 direction))
            {
                FailBot("Jev API returned an invalid movement choice.");
                yield break;
            }

            if (health.CurrentLives > 0)
            {
                movement.BotDirection = direction;
                if (!loggedFirstSuccess)
                {
                    loggedFirstSuccess = true;
                    Debug.Log($"Jev Bot received choice {response.answers.move.choice} in {(Time.realtimeSinceStartup - requestStartedAt):F2}s.");
                }
            }
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

        spawner.FillActiveBullets(activeBullets);
        JevBulletState[] bullets = new JevBulletState[activeBullets.Count];
        for (int i = 0; i < activeBullets.Count; i++)
        {
            TestBullet bullet = activeBullets[i];
            Vector3 position = camera.WorldToViewportPoint(bullet.transform.position);
            Vector3 directionEnd = camera.WorldToViewportPoint(bullet.transform.position + bullet.Direction);
            Vector2 direction = new Vector2(directionEnd.x - position.x, directionEnd.y - position.y).normalized;
            bullets[i] = new JevBulletState
            {
                x = position.x,
                y = position.y,
                directionX = direction.x,
                directionY = direction.y,
                speed = bullet.Speed
            };
        }

        return new JevRequest
        {
            state = new JevState
            {
                objective = "Avoid enemy bullets and survive as long as possible.",
                player = new JevPosition { x = playerViewport.x, y = playerViewport.y },
                bullets = bullets,
                playableArea = new JevPlayableArea
                {
                    left = marginX,
                    right = 1f - marginX,
                    bottom = marginY,
                    top = 1f - marginY
                }
            }
        };
    }

    private void FailBot(string message)
    {
        if (failed)
            return;

        failed = true;
        requestInFlight = false;
        movement.BotDirection = Vector2.zero;
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
        public float directionX;
        public float directionY;
        public float speed;
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
        public JevChoiceQuestion move = new JevChoiceQuestion();
    }

    [Serializable]
    private sealed class JevChoiceQuestion
    {
        public string type = "choice";
        public string instructions = "Avoid enemy bullets and survive as long as possible. Choose one movement action.";
        public JevCriteria criteria = new JevCriteria();
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
        public JevChoiceAnswer move;
    }

    [Serializable]
    private sealed class JevChoiceAnswer
    {
        public string type;
        public string choice;
    }
}
