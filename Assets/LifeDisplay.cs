using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public sealed class LifeDisplay : MonoBehaviour
{
    [SerializeField] private Sprite iconSprite;

    private readonly UnityEngine.UI.Image[] slots = new UnityEngine.UI.Image[PlayerHealth.MaxLives];
    private PlayerHealth health;
    private BulletSpawner bulletSpawner;
    private UnityEngine.UI.Text bulletCountText;
    private GameObject gameOverPanel;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        bulletSpawner = GetComponent<PlayerMovement>().PlayCamera.GetComponent<BulletSpawner>();

        GameObject canvasObject = new GameObject("Game UI", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
            UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution = new Vector2(1920, 1080);

        for (int i = 0; i < slots.Length; i++)
        {
            GameObject slot = new GameObject($"Life {i + 1}", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            slot.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = slot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f + i * 44f, -24f);
            rect.sizeDelta = new Vector2(32f, 32f);
            UnityEngine.UI.Image background = slot.GetComponent<UnityEngine.UI.Image>();
            background.sprite = iconSprite;
            background.color = new Color(0.12f, 0.18f, 0.22f, 0.8f);
            background.raycastTarget = false;

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fill.transform.SetParent(slot.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);
            slots[i] = fill.GetComponent<UnityEngine.UI.Image>();
            slots[i].sprite = iconSprite;
            slots[i].color = new Color(0.25f, 0.8f, 1f);
            slots[i].raycastTarget = false;
        }

        Refresh(health.CurrentLives);
        Font counterFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bulletCountText = CreateText("0", canvasObject.transform, counterFont, 36,
            Vector2.zero, new Vector2(180f, 50f));
        RectTransform counterRect = bulletCountText.rectTransform;
        counterRect.anchorMin = counterRect.anchorMax = Vector2.one;
        counterRect.pivot = Vector2.one;
        counterRect.anchoredPosition = new Vector2(-24f, -24f);
        bulletCountText.alignment = TextAnchor.UpperRight;
        RefreshBulletCount(bulletSpawner.ActiveCount);
        CreateGameOverPanel(canvasObject.transform);
    }

    private void OnEnable()
    {
        health.LivesChanged += Refresh;
        bulletSpawner.CountChanged += RefreshBulletCount;
    }

    private void OnDisable()
    {
        health.LivesChanged -= Refresh;
        bulletSpawner.CountChanged -= RefreshBulletCount;
    }

    private void Refresh(int lives)
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i].enabled = i < lives;
    }

    private void RefreshBulletCount(int count) => bulletCountText.text = count.ToString();

    private void CreateGameOverPanel(Transform parent)
    {
        gameOverPanel = new GameObject("Game Over Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        gameOverPanel.transform.SetParent(parent, false);
        RectTransform panelRect = gameOverPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        UnityEngine.UI.Image panelImage = gameOverPanel.GetComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0.08f, 0f, 0f, 0.85f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreateText("GAME OVER", gameOverPanel.transform, font, 80, new Vector2(0f, 55f), new Vector2(700f, 120f));

        GameObject buttonObject = new GameObject("Retry Button", typeof(RectTransform),
            typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(gameOverPanel.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(240f, 72f);
        buttonRect.anchoredPosition = new Vector2(0f, -70f);
        buttonObject.GetComponent<UnityEngine.UI.Image>().color = new Color(0.55f, 0.12f, 0.12f);
        buttonObject.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(Restart);
        CreateText("다시 하기", buttonObject.transform, font, 32, Vector2.zero, Vector2.zero);

        GameObject eventSystem = new GameObject("Event System", typeof(UnityEngine.EventSystems.EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        gameOverPanel.SetActive(false);
    }

    private static UnityEngine.UI.Text CreateText(string label, Transform parent, Font font, int size,
        Vector2 position, Vector2 textSize)
    {
        GameObject textObject = new GameObject(label, typeof(RectTransform), typeof(UnityEngine.UI.Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = textSize;
        if (textSize == Vector2.zero)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        UnityEngine.UI.Text text = textObject.GetComponent<UnityEngine.UI.Text>();
        text.text = label;
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    public void ShowGameOver() => gameOverPanel.SetActive(true);

    private static void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
