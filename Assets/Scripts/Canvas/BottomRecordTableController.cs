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
        public TableEntry(string rawValue, bool hasNumericValue, float mainValue, float secValue, float efficiencyPercent)
        {
            RawValue = rawValue;
            HasNumericValue = hasNumericValue;
            MainValue = mainValue;
            SecValue = secValue;
            EfficiencyPercent = efficiencyPercent;
        }

        public string RawValue { get; }
        public bool HasNumericValue { get; }
        public float MainValue { get; }
        public float SecValue { get; }
        public float EfficiencyPercent { get; }
    }

    [Header("UI")]
    [SerializeField] private TMP_InputField valueInputField;
    [SerializeField] private GameObject tablePanel;
    [SerializeField] private TextMeshProUGUI tableContentText;

    [Header("Calculation")]
    [SerializeField] private float sourceSheetLength = 1000f;
    [SerializeField] private int maxVisibleRows = 12;

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
            return;

        TableEntry entry = BuildEntry(rawValue);
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

    private TableEntry BuildEntry(string rawValue)
    {
        if (!TryParseNumericValue(rawValue, out float numericValue))
            return new TableEntry(rawValue, false, 0f, 0f, 0f);

        float clampedMain = Mathf.Max(0f, numericValue);
        float clampedSourceLength = Mathf.Max(1f, sourceSheetLength);
        float secValue = Mathf.Max(0f, clampedSourceLength - clampedMain);
        float efficiencyPercent = Mathf.Clamp01(clampedMain / clampedSourceLength) * 100f;

        return new TableEntry(rawValue, true, clampedMain, secValue, efficiencyPercent);
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

    private void RefreshTableView()
    {
        if (tableContentText == null)
            return;

        tableContentText.text = BuildTableContent();
    }

    private string BuildTableContent()
    {
        StringBuilder builder = new();
        builder.AppendLine("<size=115%><b>Таблица записей</b></size>");
        builder.AppendLine("Формулы: sec = ввод, main = max(1000 - sec, 0), исп. = main / 1000 * 100%");
        builder.AppendLine();
        builder.AppendLine("<mspace=12px><b>№  | Ввод       |      sec |     main | Исп., %</b></mspace>");
        builder.AppendLine("<mspace=12px>---+------------+----------+----------+--------</mspace>");

        if (entries.Count == 0)
        {
            builder.Append("<mspace=12px>   Нет записей</mspace>");
            return builder.ToString();
        }

        int startIndex = Mathf.Max(0, entries.Count - Mathf.Max(1, maxVisibleRows));
        for (int i = startIndex; i < entries.Count; i++)
        {
            TableEntry entry = entries[i];
            string inputColumn = FitToColumn(entry.RawValue, 10);
            string mainColumn = entry.HasNumericValue ? FitToColumn(FormatNumber(entry.MainValue), 8) : "   -    ";
            string secColumn = entry.HasNumericValue ? FitToColumn(FormatNumber(entry.SecValue), 8) : "   -    ";
            string efficiencyColumn = entry.HasNumericValue ? FitToColumn(FormatNumber(entry.EfficiencyPercent), 6) : "  -   ";

            builder.Append("<mspace=12px>");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0,2} | {1,-10} | {2,8} | {3,8} | {4,6}",
                i + 1,
                inputColumn,
                mainColumn,
                secColumn,
                efficiencyColumn);
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
        tableContentText.fontSizeMax = 20f;
        tableContentText.fontSizeMin = 18f;
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
