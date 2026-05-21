using TMPro;
using UnityEngine;

public class BottomInfoPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI infoText;
    [SerializeField] private string defaultMessage = "Наведите курсор на элемент панели управления.";
    [SerializeField] private float longMessageAutoSizeMin = 17f;
    [SerializeField] private int longMessageLengthThreshold = 110;

    private float defaultFontSize;
    private float defaultFontSizeMin;
    private float defaultFontSizeMax;
    private bool defaultAutoSizing;
    private TextOverflowModes defaultOverflowMode;
    private bool initialized;

    private void Awake()
    {
        CacheDefaults();
        ApplyBaseLayout();
    }

    private void Start()
    {
        ShowDefault();
    }

    public void ShowInfo(string message)
    {
        if (infoText == null)
            return;

        CacheDefaults();
        ApplyBaseLayout();
        ApplyAdaptiveSizing(message);
        infoText.text = message;
    }

    public void ShowDefault()
    {
        if (infoText == null)
            return;

        CacheDefaults();
        ApplyBaseLayout();
        RestoreDefaultSizing();
        infoText.text = defaultMessage;
    }

    private void CacheDefaults()
    {
        if (initialized || infoText == null)
            return;

        defaultFontSize = infoText.fontSize;
        defaultFontSizeMin = infoText.fontSizeMin;
        defaultFontSizeMax = infoText.fontSizeMax > 0f ? infoText.fontSizeMax : infoText.fontSize;
        defaultAutoSizing = infoText.enableAutoSizing;
        defaultOverflowMode = infoText.overflowMode;
        initialized = true;
    }

    private void ApplyBaseLayout()
    {
        if (infoText == null)
            return;

        infoText.textWrappingMode = TextWrappingModes.Normal;
        infoText.alignment = TextAlignmentOptions.MidlineLeft;
        infoText.overflowMode = TextOverflowModes.Overflow;
    }

    private void ApplyAdaptiveSizing(string message)
    {
        if (infoText == null)
            return;

        bool useCompactLayout =
            !string.IsNullOrEmpty(message) &&
            (message.Length >= longMessageLengthThreshold || message.Contains('\n'));

        if (!useCompactLayout)
        {
            RestoreDefaultSizing();
            return;
        }

        infoText.enableAutoSizing = true;
        infoText.fontSizeMax = defaultFontSize;
        infoText.fontSizeMin = Mathf.Min(defaultFontSize, longMessageAutoSizeMin);
    }

    private void RestoreDefaultSizing()
    {
        if (infoText == null)
            return;

        infoText.enableAutoSizing = defaultAutoSizing;
        infoText.fontSize = defaultFontSize;
        infoText.fontSizeMax = defaultFontSizeMax;
        infoText.fontSizeMin = defaultFontSizeMin;
        infoText.overflowMode = defaultOverflowMode;
    }
}
