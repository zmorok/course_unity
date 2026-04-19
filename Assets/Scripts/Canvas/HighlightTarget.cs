using UnityEngine;

public class HighlightTarget : MonoBehaviour
{
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color highlightColor = Color.yellow;

    private Color[][] originalColors;
    private bool isHighlighted;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();

        SaveOriginalColors();
    }

    private void SaveOriginalColors()
    {
        originalColors = new Color[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;
            originalColors[i] = new Color[materials.Length];

            for (int j = 0; j < materials.Length; j++)
            {
                if (materials[j].HasProperty("_Color"))
                    originalColors[i][j] = materials[j].color;
            }
        }
    }

    public void Highlight()
    {
        if (isHighlighted) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                if (materials[j].HasProperty("_Color"))
                    materials[j].color = highlightColor;
            }
        }

        isHighlighted = true;
    }

    public void ResetHighlight()
    {
        if (!isHighlighted) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                if (materials[j].HasProperty("_Color"))
                    materials[j].color = originalColors[i][j];
            }
        }

        isHighlighted = false;
    }
}