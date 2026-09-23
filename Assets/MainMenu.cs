using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public sealed class MainMenu : MonoBehaviour
{
    private GameObject noticeBubble;
    private CanvasGroup noticeGroup;
    private Coroutine noticeRoutine;

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
        CreateNotice(canvasObject.transform, font);

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

    private static void CreateText(string label, Transform parent, Font font, int size,
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
