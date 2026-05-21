using UnityEngine;

// управление лампами отдельно от экрана панели, чтобы дисплей не работал напрямую с материалами индикаторов
public sealed class ControlPanelIndicatorLights
{
    private readonly string greenIndicatorObjectName;
    private readonly string redIndicatorObjectName;
    private readonly Color greenIndicatorEmissionColor;
    private readonly Color redIndicatorEmissionColor;
    private readonly float greenIndicatorEmission;
    private readonly float redIndicatorEmission;

    private Renderer[] greenIndicatorRenderers;
    private Renderer[] redIndicatorRenderers;

    public ControlPanelIndicatorLights(
        string greenIndicatorObjectName,
        string redIndicatorObjectName,
        Color greenIndicatorEmissionColor,
        Color redIndicatorEmissionColor,
        float greenIndicatorEmission,
        float redIndicatorEmission)
    {
        this.greenIndicatorObjectName = greenIndicatorObjectName;
        this.redIndicatorObjectName = redIndicatorObjectName;
        this.greenIndicatorEmissionColor = greenIndicatorEmissionColor;
        this.redIndicatorEmissionColor = redIndicatorEmissionColor;
        this.greenIndicatorEmission = greenIndicatorEmission;
        this.redIndicatorEmission = redIndicatorEmission;
    }

    // вызывается при изменении питания или реза, чтобы зелёная лампа означала готовность, а красная активный рез
    public void ApplyState(bool machinePowered, bool isCutting)
    {
        EnsureIndicators();

        bool greenOn = machinePowered && !isCutting;
        bool redOn = machinePowered && isCutting;

        SetIndicatorEmission(greenIndicatorRenderers, greenIndicatorEmissionColor, greenOn ? greenIndicatorEmission : 0f);
        SetIndicatorEmission(redIndicatorRenderers, redIndicatorEmissionColor, redOn ? redIndicatorEmission : 0f);
    }

    private void EnsureIndicators()
    {
        greenIndicatorRenderers ??= FindIndicatorRenderers(greenIndicatorObjectName);
        redIndicatorRenderers ??= FindIndicatorRenderers(redIndicatorObjectName);
    }

    private Renderer[] FindIndicatorRenderers(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return System.Array.Empty<Renderer>();

        GameObject indicatorObject = GameObject.Find(objectName);
        if (indicatorObject == null)
        {
            Debug.LogWarning($"Не найден индикатор '{objectName}'.");
            return System.Array.Empty<Renderer>();
        }

        return indicatorObject.GetComponentsInChildren<Renderer>(true);
    }

    private void SetIndicatorEmission(Renderer[] renderers, Color emissionColor, float intensity)
    {
        if (renderers == null || renderers.Length == 0)
            return;

        Color targetEmission = emissionColor * Mathf.Max(0f, intensity);
        bool emissionEnabled = intensity > 0f;

        foreach (Renderer lampRenderer in renderers)
        {
            if (lampRenderer == null)
                continue;

            Material[] materials = lampRenderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || !material.HasProperty("_EmissionColor"))
                    continue;

                if (emissionEnabled)
                    material.EnableKeyword("_EMISSION");
                else
                    material.DisableKeyword("_EMISSION");

                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", targetEmission);
            }
        }
    }
}
