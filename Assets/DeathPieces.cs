using UnityEngine;

public sealed class DeathPieces : MonoBehaviour
{
    public const float Duration = 0.45f;

    private readonly Transform[] pieces = new Transform[4];
    private readonly Vector3[] directions = new Vector3[4];
    private readonly Vector3[] initialScales = new Vector3[4];
    private float elapsed;

    public static void Create(SpriteRenderer source)
    {
        GameObject effect = new GameObject("Player Death Pieces");
        DeathPieces animation = effect.AddComponent<DeathPieces>();
        Bounds spriteBounds = source.sprite.bounds;
        int index = 0;

        for (int y = -1; y <= 1; y += 2)
        for (int x = -1; x <= 1; x += 2)
        {
            GameObject piece = new GameObject("Piece", typeof(SpriteRenderer));
            piece.transform.position = source.transform.TransformPoint(
                new Vector3(x * spriteBounds.extents.x * 0.5f, y * spriteBounds.extents.y * 0.5f));
            piece.transform.rotation = source.transform.rotation;
            piece.transform.localScale = source.transform.lossyScale * 0.5f;

            SpriteRenderer renderer = piece.GetComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.color = source.color;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder + 1;

            animation.pieces[index] = piece.transform;
            animation.directions[index] = new Vector3(x, y).normalized;
            animation.initialScales[index] = piece.transform.localScale;
            index++;
        }
    }

    private void Update()
    {
        elapsed = Mathf.Min(elapsed + Time.deltaTime, Duration);
        float progress = elapsed / Duration;

        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i].position += directions[i] * (2f * Time.deltaTime);
            pieces[i].localScale = initialScales[i] * (1f - progress);
        }

        if (elapsed >= Duration)
        {
            for (int i = 0; i < pieces.Length; i++)
                Destroy(pieces[i].gameObject);
            Destroy(gameObject);
        }
    }
}
