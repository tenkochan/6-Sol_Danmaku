using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public sealed class MainMenu : MonoBehaviour
{
    private GameObject noticeBubble;
    private CanvasGroup noticeGroup;
    private Coroutine noticeRoutine;
    private GameObject rankingPanel;
    private GameObject noRecordsText;
    private readonly GameObject[] rankingRows = new GameObject[10];
    private readonly UnityEngine.UI.Text[][] rankingCells = new UnityEngine.UI.Text[10][];

    private void Awake()
    {
        GameObject canvasObject = new GameObject("Menu Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Font font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 40);
        CreateText("6 SOL DANMAKU", canvasObject.transform, font, 72,
            new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(900f, 110f));
        CreateButton("사람 모드", canvasObject.transform, font, new Vector2(0.5f, 0.52f), StartHumanMode);
        CreateButton("봇 모드", canvasObject.transform, font, new Vector2(0.5f, 0.41f), ShowBotNotice);
        CreateButton("랭킹", canvasObject.transform, font, new Vector2(0.5f, 0.30f), ShowRanking);
        CreateNotice(canvasObject.transform, font);
        CreateRankingScreen(canvasObject.transform, font);

        GameObject eventSystem = new GameObject("Event System", typeof(UnityEngine.EventSystems.EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor,
        Vector2 position, Vector2 size)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static UnityEngine.UI.Text CreateText(string label, Transform parent, Font font, int size,
        Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = CreateRect(label, parent, anchor, position, dimensions);
        UnityEngine.UI.Text text = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
        text.text = label;
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void CreateButton(string label, Transform parent, Font font, Vector2 anchor,
        UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = CreateRect(label, parent, anchor, Vector2.zero, new Vector2(340f, 82f));
        UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.16f, 0.28f, 0.43f);
        UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(action);
        CreateText(label, rect, font, 36, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(340f, 82f));
    }

    private void CreateNotice(Transform parent, Font font)
    {
        RectTransform rect = CreateRect("Bot Notice", parent, new Vector2(0.5f, 0f),
            new Vector2(0f, 100f), new Vector2(320f, 72f));
        noticeBubble = rect.gameObject;
        rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.18f, 0.26f);
        noticeGroup = rect.gameObject.AddComponent<CanvasGroup>();
        CreateText("개발 중입니다.", rect, font, 28, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(300f, 68f));

        RectTransform tail = CreateRect("Bubble Tail", rect, new Vector2(0.5f, 0f),
            new Vector2(0f, -9f), new Vector2(20f, 20f));
        tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
        tail.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.18f, 0.26f);
        noticeBubble.SetActive(false);
    }

    private void CreateRankingScreen(Transform parent, Font font)
    {
        RectTransform panel = CreateRect("Ranking Panel", parent, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        rankingPanel = panel.gameObject;
        panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.04f, 0.08f, 0.14f, 0.98f);

        CreateText("랭킹", panel, font, 60, new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(500f, 90f));
        CreateRankingRow(panel, font, 260f, 22, new[]
        {
            "순위", "첫 라이프", "두 번째 라이프", "세 번째 라이프", "기록 시점", "최대 탄막", "모드"
        });

        for (int i = 0; i < rankingRows.Length; i++)
        {
            UnityEngine.UI.Text[] cells = CreateRankingRow(panel, font, 195f - i * 48f, 22,
                new[] { "", "", "", "", "", "", "" });
            rankingCells[i] = cells;
            rankingRows[i] = cells[0].transform.parent.gameObject;
            rankingRows[i].SetActive(false);
        }

        noRecordsText = CreateText("기록이 없습니다.", panel, font, 34,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 70f)).gameObject;
        CreateButton("뒤로", panel, font, new Vector2(0.5f, 0.09f), HideRanking);
        rankingPanel.SetActive(false);
    }

    private static UnityEngine.UI.Text[] CreateRankingRow(Transform parent, Font font, float y,
        int fontSize, string[] values)
    {
        RectTransform row = CreateRect("Ranking Row", parent, new Vector2(0.5f, 0.5f),
            new Vector2(0f, y), new Vector2(1220f, 46f));
        float[] xPositions = { -530f, -410f, -250f, -90f, 140f, 400f, 535f };
        float[] widths = { 80f, 150f, 150f, 150f, 300f, 130f, 110f };
        UnityEngine.UI.Text[] cells = new UnityEngine.UI.Text[values.Length];
        for (int i = 0; i < values.Length; i++)
            cells[i] = CreateText(values[i], row, font, fontSize, new Vector2(0.5f, 0.5f),
                new Vector2(xPositions[i], 0f), new Vector2(widths[i], 46f));
        return cells;
    }

    private void ShowRanking()
    {
        List<PlayRecord> records = PlayRecordStore.LoadRecords();
        records.RemoveAll(record => record == null);
        records.Sort((left, right) => right.thirdLifeLostSeconds.CompareTo(left.thirdLifeLostSeconds));
        int shown = Mathf.Min(records.Count, rankingRows.Length);
        noRecordsText.SetActive(shown == 0);

        for (int i = 0; i < rankingRows.Length; i++)
        {
            rankingRows[i].SetActive(i < shown);
            if (i >= shown)
                continue;

            PlayRecord record = records[i];
            string date = DateTimeOffset.TryParse(record.startedAtIso8601, out DateTimeOffset started)
                ? started.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : record.startedAtIso8601 ?? "";
            string[] values =
            {
                (i + 1).ToString(),
                FormatTime(record.firstLifeLostSeconds),
                FormatTime(record.secondLifeLostSeconds),
                FormatTime(record.thirdLifeLostSeconds),
                date,
                record.maxActiveBullets.ToString(),
                record.mode ?? ""
            };
            for (int column = 0; column < values.Length; column++)
                rankingCells[i][column].text = values[column];
        }

        rankingPanel.SetActive(true);
    }

    private static string FormatTime(float seconds)
    {
        int tenths = Mathf.FloorToInt(Mathf.Max(0f, seconds) * 10f);
        return $"{tenths / 600:00}:{tenths / 10 % 60:00}.{tenths % 10}";
    }

    private void HideRanking() => rankingPanel.SetActive(false);

    private static void StartHumanMode() => SceneManager.LoadScene("Main");

    private void ShowBotNotice()
    {
        if (noticeRoutine != null)
            StopCoroutine(noticeRoutine);
        noticeBubble.SetActive(true);
        noticeGroup.alpha = 1f;
        noticeRoutine = StartCoroutine(HideNotice());
    }

    private IEnumerator HideNotice()
    {
        yield return new WaitForSecondsRealtime(2f);
        const float fadeDuration = 0.5f;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            noticeGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        noticeBubble.SetActive(false);
        noticeRoutine = null;
    }
}
