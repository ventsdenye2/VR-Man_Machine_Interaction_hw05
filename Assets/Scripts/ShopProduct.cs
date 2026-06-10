using UnityEngine;

[DisallowMultipleComponent]
public class ShopProduct : MonoBehaviour
{
    public string productId;
    public string displayName;
    public int price;
    public Color highlightColor = new Color(1f, 0.86f, 0.15f, 1f);
    public float highlightEmission = 0.65f;

    Renderer[] renderers;
    MaterialPropertyBlock propertyBlock;
    bool highlighted;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        CacheRenderers();
    }

    void OnDisable()
    {
        SetHighlighted(false);
    }

    public void SetHighlighted(bool value)
    {
        if (highlighted == value)
            return;

        highlighted = value;
        ApplyHighlight();
    }

    public string GetCartLabel()
    {
        return string.IsNullOrWhiteSpace(displayName) ? productId : displayName;
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        CacheRenderers();

        bounds = default;
        var hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    void CacheRenderers()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    void ApplyHighlight()
    {
        CacheRenderers();

        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);

            if (highlighted)
            {
                propertyBlock.SetColor(BaseColorId, highlightColor);
                propertyBlock.SetColor(ColorId, highlightColor);
                propertyBlock.SetColor(EmissionColorId, highlightColor * highlightEmission);
            }
            else
            {
                propertyBlock.Clear();
            }

            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
