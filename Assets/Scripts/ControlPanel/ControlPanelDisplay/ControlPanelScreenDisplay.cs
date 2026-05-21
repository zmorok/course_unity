using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ControlPanelScreenDisplay : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private string displayObjectName = "ScreenDisplayText";
    [SerializeField] private Vector3 displayLocalPosition = new(0f, -0.008f, 0f);
    [SerializeField] private Vector3 displayLocalEulerAngles = new(90f, 0f, 0f);
    [SerializeField] private Vector3 displayLocalScale = new(-0.004f, 0.004f, 0.004f);
    [SerializeField] private Vector2 displayRectSize = new(2.15f, 14f);
    [SerializeField] private float displayFontSize = 6.25f;
    [SerializeField] private float minAutoFontSize = 1.65f;
    [SerializeField] private Color displayColor = new(0.92f, 1f, 1f, 1f);
    [SerializeField] private TMP_FontAsset fontAsset;

    [Header("Indicators")]
    [SerializeField] private string greenIndicatorObjectName = "PANEL_SmallGreenBtn";
    [SerializeField] private string redIndicatorObjectName = "PANEL_SmallRedBtn";
    [SerializeField] private Color greenIndicatorEmissionColor = new(0.25f, 1f, 0.25f, 1f);
    [SerializeField] private Color redIndicatorEmissionColor = new(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private float greenIndicatorEmission = 2.5f;
    [SerializeField] private float redIndicatorEmission = 3.5f;

    [Header("Paper Cut Command")]
    [SerializeField] private PaperPathMover paperMover;
    [SerializeField] private BottomInfoPanel infoPanel;
    [SerializeField] private string invalidPaperCutMessage = PaperPathMover.InvalidCutSizeMessage;

    private readonly ControlPanelCalculator calculator = new();
    private ControlPanelIndicatorLights indicatorLights;
    private TextMeshPro displayText;
    private bool machinePowered;
    private bool isCutting;

    private void Awake()
    {
        EnsureDisplay();
        EnsureIndicatorLights();
        isCutting = CutAnimator.IsCutting;
        ApplyPowerState(ButtonAnimator.IsMachinePowered);
    }

    private void OnEnable()
    {
        ButtonAnimator.ButtonPressed += HandleButton;
        ButtonAnimator.MachinePowerChanged += HandleMachinePowerChanged;
        CutAnimator.CuttingStateChanged += HandleCuttingStateChanged;
        isCutting = CutAnimator.IsCutting;
        ApplyPowerState(ButtonAnimator.IsMachinePowered);
        RefreshDisplay();
    }

    private void OnDisable()
    {
        ButtonAnimator.ButtonPressed -= HandleButton;
        ButtonAnimator.MachinePowerChanged -= HandleMachinePowerChanged;
        CutAnimator.CuttingStateChanged -= HandleCuttingStateChanged;
    }

    private void HandleMachinePowerChanged(bool isPowered)
    {
        ApplyPowerState(isPowered);
    }

    private void HandleCuttingStateChanged(bool cutting)
    {
        isCutting = cutting;
        ApplyIndicatorLights();
    }

    private void HandleButton(ControlPanelButton button)
    {
        if (!machinePowered)
            return;

        if (ControlPanelInputLayout.TryGetDigit(button, out char digit))
        {
            calculator.AppendDigit(digit);
            RefreshDisplay();
            return;
        }

        switch (button)
        {
            case ControlPanelButton.Dot:
                calculator.AppendDot();
                RefreshDisplay();
                break;
            case ControlPanelButton.Plus:
            case ControlPanelButton.Minus:
            case ControlPanelButton.Mult:
            case ControlPanelButton.Div:
                calculator.QueueOperation(button);
                RefreshDisplay();
                break;
            case ControlPanelButton.Eq:
                calculator.EvaluateQueuedOperation("RESULT");
                RefreshDisplay();
                break;
            case ControlPanelButton.Enter:
                HandleEnter();
                break;
            case ControlPanelButton.Delete:
                calculator.DeleteLastCharacter();
                RefreshDisplay();
                break;
            case ControlPanelButton.Clear:
                calculator.ResetState(true);
                TryClearPaperCutOffset();
                RefreshDisplay();
                break;
            case ControlPanelButton.Program:
                calculator.ToggleProgramMode();
                RefreshDisplay();
                break;
            case ControlPanelButton.ArrowUp:
            case ControlPanelButton.ArrowDown:
            case ControlPanelButton.ArrowLeft:
            case ControlPanelButton.ArrowRight:
            case ControlPanelButton.Center:
                calculator.ShowStatus(ControlPanelInputLayout.GetLabel(button));
                RefreshDisplay();
                break;
        }
    }

    private void HandleEnter()
    {
        if (calculator.HasQueuedOperationReady)
        {
            calculator.EvaluateQueuedOperation("ENTER");
            RefreshDisplay();
            return;
        }

        if (TryHandlePaperCutCommand())
            return;

        calculator.ShowStatus("ENTER");
        RefreshDisplay();
    }

    private bool TryHandlePaperCutCommand()
    {
        ResolvePaperMover();

        if (paperMover == null || !paperMover.CanAcceptCutCommand)
            return false;

        if (!calculator.TryParseCurrentInput(out double cutSize))
        {
            ShowPaperCutError(invalidPaperCutMessage);
            return true;
        }

        if (paperMover.TryApplyCutCommand((float)cutSize, out string statusLabel, out string errorMessage))
        {
            calculator.ApplyCutCommandAccepted(cutSize, statusLabel);
            PracticeTasksPopupController.NotifyCutCommandAcceptedFromPanel((float)cutSize);

            if (!PracticeTasksPopupController.IsPracticeFlowRunning())
                ShowBottomInfo($"Выбран тип бумаги {paperMover.ActivePaperVariantLabel}. Бумага смещается к линии реза.");

            RefreshDisplay();
            return true;
        }

        ShowPaperCutError(string.IsNullOrWhiteSpace(errorMessage) ? invalidPaperCutMessage : errorMessage);
        return true;
    }

    private void TryClearPaperCutOffset()
    {
        ResolvePaperMover();

        if (paperMover == null)
            return;

        if (!paperMover.ClearCutOffsetFromPanel(out string statusLabel))
            return;

        calculator.ShowStatus(statusLabel);
        ShowBottomInfo("Смещение бумаги сброшено. Введите размер реза от 100 до 900мм и нажмите Enter.");
    }

    private void ShowPaperCutError(string message)
    {
        calculator.SetError("Error");
        ShowBottomInfo(message);
        RefreshDisplay();
    }

    private void ApplyPowerState(bool isPowered)
    {
        machinePowered = isPowered;

        if (!machinePowered)
        {
            calculator.ResetForPowerOff();
            ApplyIndicatorLights();
            RefreshDisplay();
            return;
        }

        displayText.enabled = true;
        ApplyIndicatorLights();
        calculator.ResetState(false);
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (displayText == null)
            return;

        if (!machinePowered)
        {
            displayText.text = string.Empty;
            displayText.enabled = false;
            return;
        }

        displayText.enabled = true;
        ControlPanelDisplayState state = calculator.State;

        string topLine = FitDisplayLine(state.ExpressionLine, 20, 18);
        string mainLine = FitDisplayLine(state.CurrentInput, 12, 12);
        string bottomLine = FitDisplayLine(state.StatusLine, 18, 16);

        if (string.IsNullOrEmpty(topLine))
            topLine = " ";

        if (string.IsNullOrEmpty(mainLine))
            mainLine = "0";

        if (string.IsNullOrEmpty(bottomLine))
            bottomLine = " ";

        int topPercent = GetRelativeLineSize(topLine, 40, 24, 10, 4);
        int mainPercent = GetRelativeLineSize(mainLine, 78, 22, 6, 10);
        int bottomPercent = GetRelativeLineSize(bottomLine, 28, 16, 8, 4);

        displayText.text =
            $"<size={topPercent}%>{Sanitize(topLine)}</size>\n" +
            $"<size={mainPercent}%>{Sanitize(mainLine)}</size>\n" +
            $"<size={bottomPercent}%>{Sanitize(bottomLine)}</size>";
    }

    private void EnsureDisplay()
    {
        if (displayText != null)
            return;

        Transform displayTransform = transform.Find(displayObjectName);

        if (displayTransform == null)
        {
            GameObject displayObject = new GameObject(displayObjectName);
            displayTransform = displayObject.transform;
            displayTransform.SetParent(transform, false);
        }

        displayTransform.localPosition = displayLocalPosition;
        displayTransform.localRotation = Quaternion.Euler(displayLocalEulerAngles);
        displayTransform.localScale = displayLocalScale;

        if (displayTransform is RectTransform rectTransform)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = displayRectSize;
        }

        displayText = displayTransform.GetComponent<TextMeshPro>();

        if (displayText == null)
            displayText = displayTransform.gameObject.AddComponent<TextMeshPro>();

        displayText.font = fontAsset != null ? fontAsset : ResolveFontAsset();
        displayText.fontSize = displayFontSize;
        displayText.enableAutoSizing = true;
        displayText.fontSizeMax = displayFontSize;
        displayText.fontSizeMin = minAutoFontSize;
        displayText.richText = true;
        displayText.alignment = TextAlignmentOptions.Center;
        displayText.textWrappingMode = TextWrappingModes.NoWrap;
        displayText.overflowMode = TextOverflowModes.Ellipsis;
        displayText.color = displayColor;
        displayText.outlineColor = new Color32(3, 28, 40, 255);
        displayText.outlineWidth = 0.1f;
        displayText.margin = new Vector4(0.15f, 0.12f, 0.15f, 0.12f);
        displayText.characterWidthAdjustment = 8f;
        displayText.text = string.Empty;

        MeshRenderer renderer = displayText.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = 5;
        }
    }

    private void EnsureIndicatorLights()
    {
        if (indicatorLights != null)
            return;

        indicatorLights = new ControlPanelIndicatorLights(
            greenIndicatorObjectName,
            redIndicatorObjectName,
            greenIndicatorEmissionColor,
            redIndicatorEmissionColor,
            greenIndicatorEmission,
            redIndicatorEmission);
    }

    private void ApplyIndicatorLights()
    {
        EnsureIndicatorLights();
        indicatorLights.ApplyState(machinePowered, isCutting);
    }

    private void ResolvePaperMover()
    {
        if (paperMover == null)
            paperMover = Object.FindFirstObjectByType<PaperPathMover>();
    }

    private void ResolveInfoPanel()
    {
        if (infoPanel == null)
            infoPanel = Object.FindFirstObjectByType<BottomInfoPanel>();
    }

    private void ShowBottomInfo(string message)
    {
        ResolveInfoPanel();
        infoPanel?.ShowInfo(message);
    }

    private TMP_FontAsset ResolveFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private string Sanitize(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private string FitDisplayLine(string value, int maxChars, int numericChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string trimmed = value.Trim();

        if (trimmed.Length <= maxChars)
            return trimmed;

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericValue))
        {
            string scientific = numericValue.ToString("0.###E+0", CultureInfo.InvariantCulture);
            if (scientific.Length <= numericChars)
                return scientific;
        }

        if (maxChars <= 1)
            return trimmed[..1];

        return trimmed[..(maxChars - 1)] + "…";
    }

    private int GetRelativeLineSize(string value, int basePercent, int minPercent, int softLimit, int stepPercent)
    {
        if (string.IsNullOrEmpty(value))
            return basePercent;

        int overflow = Mathf.Max(0, value.Length - softLimit);
        if (overflow == 0)
            return basePercent;

        int adjustedPercent = basePercent - overflow * stepPercent;
        return Mathf.Clamp(adjustedPercent, minPercent, basePercent);
    }
}
