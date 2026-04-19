using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class BottomRecordTableController : MonoBehaviour
{
    private readonly struct TableEntry
    {
        public TableEntry(
            float cutSize,
            float usefulPartSize,
            float wasteSize,
            float usagePercent)
        {
            CutSize = cutSize;
            UsefulPartSize = usefulPartSize;
            WasteSize = wasteSize;
            UsagePercent = usagePercent;
        }

        public float CutSize { get; }
        public float UsefulPartSize { get; }
        public float WasteSize { get; }
        public float UsagePercent { get; }
    }

    [Header("UI")]
    [SerializeField] private TMP_InputField valueInputField;
    [SerializeField] private GameObject tablePanel;
    [SerializeField] private TextMeshProUGUI tableContentText;
    [SerializeField] private BottomInfoPanel infoPanel;

    [Header("Calculation")]
    [SerializeField] private float sourceSheetLength = 1000f;
    [SerializeField] private int maxVisibleRows = 8;
    [SerializeField] private string emptyTableMessage = "Записей пока нет";
    [SerializeField] private string invalidValueMessage = "Введите размер реза числом от 1 до 1000 мм.";

    private readonly List<TableEntry> entries = new();

    private void Awake()
    {
        ApplyTableTextStyle();
        RefreshTableView();
    }

    private void OnEnable()
    {
        ApplyTableTextStyle();
        RefreshTableView();
    }

    public void AddRecord()
    {
        if (valueInputField == null)
            return;

        string rawValue = valueInputField.text?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            ShowValidationError("Введите размер реза в миллиметрах.");
            valueInputField.ActivateInputField();
            valueInputField.text = string.Empty;
            return;
        }

        if (!TryBuildEntry(rawValue, out TableEntry entry, out string errorMessage))
        {
            ShowValidationError(errorMessage);
            valueInputField.ActivateInputField();
            valueInputField.text = string.Empty;
            return;
        }

        entries.Add(entry);

        valueInputField.text = string.Empty;
        valueInputField.ActivateInputField();
        RefreshTableView();
    }

    public void ToggleTablePanel()
    {
        if (tablePanel == null)
            return;

        tablePanel.SetActive(!tablePanel.activeSelf);
        RefreshTableView();
    }

    public void ClearTable()
    {
        entries.Clear();

        if (valueInputField != null)
            valueInputField.text = string.Empty;

        RefreshTableView();
    }

    private bool TryBuildEntry(string rawValue, out TableEntry entry, out string errorMessage)
    {
        entry = default;
        errorMessage = string.Empty;

        if (!TryParseNumericValue(rawValue, out float numericValue))
        {
            errorMessage = "Значение должно быть числом в миллиметрах.";
            return false;
        }

        if (float.IsNaN(numericValue) || float.IsInfinity(numericValue))
        {
            errorMessage = "Значение должно быть обычным числом в миллиметрах.";
            return false;
        }

        if (numericValue <= 0f)
        {
            errorMessage = "Размер реза должен быть больше 0 мм.";
            return false;
        }

        if (numericValue > sourceSheetLength)
        {
            errorMessage = $"Размер реза не может быть больше длины листа {FormatNumber(sourceSheetLength)} мм.";
            return false;
        }

        float cutSize = numericValue;
        float clampedSourceLength = Mathf.Max(1f, sourceSheetLength);
        float usefulPartSize = Mathf.Max(0f, clampedSourceLength - cutSize);
        float wasteSize = Mathf.Min(cutSize, clampedSourceLength);
        float usagePercent = Mathf.Clamp01(usefulPartSize / clampedSourceLength) * 100f;

        entry = new TableEntry(cutSize, usefulPartSize, wasteSize, usagePercent);
        return true;
    }

    private bool TryParseNumericValue(string rawValue, out float numericValue)
    {
        string normalizedValue = rawValue
            .Trim()
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return float.TryParse(
            normalizedValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out numericValue);
    }

    private void ShowValidationError(string message)
    {
        ResolveInfoPanel();
        infoPanel?.ShowInfo(string.IsNullOrWhiteSpace(message) ? invalidValueMessage : message);
    }

    private void ResolveInfoPanel()
    {
        if (infoPanel == null)
            infoPanel = GetComponent<BottomInfoPanel>();

        if (infoPanel == null)
            infoPanel = Object.FindFirstObjectByType<BottomInfoPanel>();
    }

    private void RefreshTableView()
    {
        if (tableContentText == null)
            return;

        tableContentText.text = BuildTableContent();
    }

    private string BuildTableContent()
    {
        StringBuilder builder = new();
        builder.AppendLine("<size=115%><b>Журнал резки бумаги</b></size>");
        builder.AppendLine($"Исходный лист: {FormatNumber(sourceSheetLength)} мм. Вводимое значение — размер отрезаемого отхода.");
        builder.AppendLine();
        builder.AppendLine("<mspace=10px><b> № | Рез, мм | Готовая часть, мм | Отход, мм | Использование, %</b></mspace>");
        builder.AppendLine("<mspace=10px>---+---------+-------------------+-----------+------------------</mspace>");

        if (entries.Count == 0)
        {
            builder.Append("<mspace=10px>  ");
            builder.Append(emptyTableMessage);
            builder.Append("</mspace>");
            return builder.ToString();
        }

        int startIndex = Mathf.Max(0, entries.Count - Mathf.Max(1, maxVisibleRows));
        for (int i = startIndex; i < entries.Count; i++)
        {
            TableEntry entry = entries[i];
            builder.Append("<mspace=10px>");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0,2} | {1,7} | {2,17} | {3,9} | {4,16}",
                i + 1,
                FitToColumn(FormatNumber(entry.CutSize), 7),
                FitToColumn(FormatNumber(entry.UsefulPartSize), 16),
                FitToColumn(FormatNumber(entry.WasteSize), 9),
                FitToColumn($"{FormatNumber(entry.UsagePercent)} %", 11));
            builder.AppendLine("</mspace>");
        }

        return builder.ToString().TrimEnd();
    }

    private void ApplyTableTextStyle()
    {
        if (tableContentText == null)
            return;

        tableContentText.alignment = TextAlignmentOptions.TopLeft;
        tableContentText.textWrappingMode = TextWrappingModes.NoWrap;
        tableContentText.enableAutoSizing = true;
        tableContentText.fontSizeMax = 19f;
        tableContentText.fontSizeMin = 14f;
        tableContentText.overflowMode = TextOverflowModes.Overflow;
    }

    private static string FitToColumn(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;

        if (maxLength <= 1)
            return trimmed[..1];

        return trimmed[..(maxLength - 1)] + "…";
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
