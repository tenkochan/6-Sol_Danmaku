using UnityEngine;

public sealed class LifeDisplay : MonoBehaviour
{
    [SerializeField] private Sprite iconSprite;

    private readonly UnityEngine.UI.Image[] slots = new UnityEngine.UI.Image[PlayerHealth.MaxLives];
    private PlayerHealth health;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();

        GameObject canvasObject = new GameObject("Life Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler));
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
    }

    private void OnEnable() => health.LivesChanged += Refresh;
    private void OnDisable() => health.LivesChanged -= Refresh;

    private void Refresh(int lives)
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i].enabled = i < lives;
    }
}
