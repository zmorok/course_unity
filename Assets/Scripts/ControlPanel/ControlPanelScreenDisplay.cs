using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ControlPanelScreenDisplay : MonoBehaviour
{
    private enum PendingOperation
    {
        None,
        Add,
        Subtract,
        Multiply,
        Divide
    }

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

    private TextMeshPro displayText;
    private Renderer[] greenIndicatorRenderers;
    private Renderer[] redIndicatorRenderers;

    private double? storedOperand;
    private PendingOperation pendingOperation = PendingOperation.None;
    private string currentInput = "0";
    private string expressionLine = string.Empty;
    private string statusLine = "READY";
    private bool startNewInput;
    private bool programMode;
    private bool hasError;
    private bool machinePowered;
    private bool isCutting;

    private void Awake()
    {
        EnsureDisplay();
        EnsureIndicators();
        isCutting = CUT_Animator.IsCutting;
        ApplyPowerState(btn_Animator.IsMachinePowered);
    }

    private void OnEnable()
    {
        btn_Animator.ButtonPressed += HandleButton;
        btn_Animator.MachinePowerChanged += HandleMachinePowerChanged;
        CUT_Animator.CuttingStateChanged += HandleCuttingStateChanged;
        isCutting = CUT_Animator.IsCutting;
        ApplyPowerState(btn_Animator.IsMachinePowered);
        RefreshDisplay();
    }

    private void OnDisable()
    {
        btn_Animator.ButtonPressed -= HandleButton;
        btn_Animator.MachinePowerChanged -= HandleMachinePowerChanged;
        CUT_Animator.CuttingStateChanged -= HandleCuttingStateChanged;
    }

    private void HandleButton(ControlPanelButton button)
    {
        if (!machinePowered)
            return;

        if (ControlPanelInputLayout.TryGetDigit(button, out char digit))
        {
            AppendDigit(digit);
            return;
        }

        switch (button)
        {
            case ControlPanelButton.Dot:
                AppendDot();
                break;
            case ControlPanelButton.Plus:
            case ControlPanelButton.Minus:
            case ControlPanelButton.Mult:
            case ControlPanelButton.Div:
                QueueOperation(button);
                break;
            case ControlPanelButton.Eq:
                EvaluatePendingOperation("RESULT");
                break;
            case ControlPanelButton.Enter:
                HandleEnter();
                break;
            case ControlPanelButton.Delete:
                DeleteLastCharacter();
                break;
            case ControlPanelButton.Clear:
                ResetState(true);
                break;
            case ControlPanelButton.Program:
                ToggleProgramMode();
                break;
            case ControlPanelButton.ArrowUp:
            case ControlPanelButton.ArrowDown:
            case ControlPanelButton.ArrowLeft:
            case ControlPanelButton.ArrowRight:
            case ControlPanelButton.Center:
                ShowStatus(ControlPanelInputLayout.GetLabel(button));
                break;
        }
    }

    private void HandleMachinePowerChanged(bool isPowered)
    {
        ApplyPowerState(isPowered);
    }

    private void HandleCuttingStateChanged(bool cutting)
    {
        isCutting = cutting;
        ApplyIndicatorState();
    }

    private void AppendDigit(char digit)
    {
        PrepareForInput();

        if (startNewInput || currentInput == "0")
            currentInput = digit.ToString();
        else
            currentInput += digit;

        startNewInput = false;
        ShowStatus($"KEY {digit}");
        RefreshDisplay();
    }

    private void AppendDot()
    {
        PrepareForInput();

        if (startNewInput)
        {
            currentInput = "0.";
            startNewInput = false;
        }
        else if (!currentInput.Contains("."))
        {
            currentInput += ".";
        }

        ShowStatus("KEY DOT");
        RefreshDisplay();
    }

    private void QueueOperation(ControlPanelButton button)
    {
        if (!TryParseCurrentInput(out double currentValue))
        {
            SetError("INPUT ERROR");
            return;
        }

        PendingOperation nextOperation = ToPendingOperation(button);

        if (pendingOperation != PendingOperation.None && !startNewInput)
        {
            if (!TryEvaluate(storedOperand ?? 0d, currentValue, pendingOperation, out double chainedResult, out string error))
            {
                SetError(error);
                return;
            }

            storedOperand = chainedResult;
            currentInput = FormatNumber(chainedResult);
        }
        else if (!storedOperand.HasValue || !startNewInput)
        {
            storedOperand = currentValue;
        }

        pendingOperation = nextOperation;
        startNewInput = true;
        hasError = false;
        expressionLine = $"{FormatNumber(storedOperand ?? currentValue)} {GetOperatorSymbol(nextOperation)}";
        ShowStatus($"OP {GetOperatorSymbol(nextOperation)}");
        RefreshDisplay();
    }

    private void EvaluatePendingOperation(string completedStatus)
    {
        if (pendingOperation == PendingOperation.None || !storedOperand.HasValue)
        {
            ShowStatus(completedStatus);
            RefreshDisplay();
            return;
        }

        if (!TryParseCurrentInput(out double secondOperand))
        {
            SetError("INPUT ERROR");
            return;
        }

        double firstOperand = storedOperand.Value;

        if (!TryEvaluate(firstOperand, secondOperand, pendingOperation, out double result, out string error))
        {
            SetError(error);
            return;
        }

        expressionLine = $"{FormatNumber(firstOperand)} {GetOperatorSymbol(pendingOperation)} {FormatNumber(secondOperand)} =";
        currentInput = FormatNumber(result);
        storedOperand = null;
        pendingOperation = PendingOperation.None;
        startNewInput = true;
        hasError = false;
        ShowStatus(completedStatus);
        RefreshDisplay();
    }

    private void HandleEnter()
    {
        if (pendingOperation != PendingOperation.None && storedOperand.HasValue && !startNewInput)
        {
            EvaluatePendingOperation("ENTER");
            return;
        }

        ShowStatus("ENTER");
        RefreshDisplay();
    }

    private void DeleteLastCharacter()
    {
        if (hasError)
        {
            ResetState(false);
            return;
        }

        if (startNewInput)
        {
            currentInput = "0";
            startNewInput = false;
        }
        else if (currentInput.Length > 1)
        {
            currentInput = currentInput[..^1];
            if (currentInput == "-" || currentInput == string.Empty)
                currentInput = "0";
        }
        else
        {
            currentInput = "0";
        }

        ShowStatus("DELETE");
        RefreshDisplay();
    }

    private void ToggleProgramMode()
    {
        programMode = !programMode;
        ShowStatus(programMode ? "PROGRAM MODE" : "CALC MODE");
        RefreshDisplay();
    }

    private void ResetState(bool showClearStatus)
    {
        storedOperand = null;
        pendingOperation = PendingOperation.None;
        currentInput = "0";
        expressionLine = string.Empty;
        startNewInput = false;
        hasError = false;
        statusLine = showClearStatus ? BuildStatus("CLEAR") : BuildStatus("READY");
        RefreshDisplay();
    }

    private void ApplyPowerState(bool isPowered)
    {
        machinePowered = isPowered;

        if (!machinePowered)
        {
            storedOperand = null;
            pendingOperation = PendingOperation.None;
            currentInput = string.Empty;
            expressionLine = string.Empty;
            statusLine = string.Empty;
            startNewInput = false;
            programMode = false;
            hasError = false;
            ApplyIndicatorState();
            RefreshDisplay();
            return;
        }

        displayText.enabled = true;
        ApplyIndicatorState();
        ResetState(false);
    }

    private void PrepareForInput()
    {
        if (!hasError)
            return;

        storedOperand = null;
        pendingOperation = PendingOperation.None;
        currentInput = "0";
        expressionLine = string.Empty;
        startNewInput = false;
        hasError = false;
    }

    private bool TryParseCurrentInput(out double value)
    {
        return double.TryParse(currentInput, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool TryEvaluate(double firstOperand, double secondOperand, PendingOperation operation, out double result, out string error)
    {
        result = 0d;
        error = string.Empty;

        switch (operation)
        {
            case PendingOperation.Add:
                result = firstOperand + secondOperand;
                break;
            case PendingOperation.Subtract:
                result = firstOperand - secondOperand;
                break;
            case PendingOperation.Multiply:
                result = firstOperand * secondOperand;
                break;
            case PendingOperation.Divide:
                if (Mathf.Approximately((float)secondOperand, 0f))
                {
                    error = "DIV BY ZERO";
                    return false;
                }

                result = firstOperand / secondOperand;
                break;
            default:
                result = secondOperand;
                break;
        }

        if (double.IsInfinity(result) || double.IsNaN(result))
        {
            error = "MATH ERROR";
            return false;
        }

        return true;
    }

    private PendingOperation ToPendingOperation(ControlPanelButton button)
    {
        return button switch
        {
            ControlPanelButton.Plus => PendingOperation.Add,
            ControlPanelButton.Minus => PendingOperation.Subtract,
            ControlPanelButton.Mult => PendingOperation.Multiply,
            ControlPanelButton.Div => PendingOperation.Divide,
            _ => PendingOperation.None
        };
    }

    private string GetOperatorSymbol(PendingOperation operation)
    {
        return operation switch
        {
            PendingOperation.Add => "+",
            PendingOperation.Subtract => "-",
            PendingOperation.Multiply => "*",
            PendingOperation.Divide => "/",
            _ => string.Empty
        };
    }

    private string FormatNumber(double value)
    {
        string text = value.ToString("0.########", CultureInfo.InvariantCulture);

        if (text.Length > 12)
            text = value.ToString("0.###E+0", CultureInfo.InvariantCulture);

        return text == "-0" ? "0" : text;
    }

    private void SetError(string error)
    {
        hasError = true;
        storedOperand = null;
        pendingOperation = PendingOperation.None;
        expressionLine = string.Empty;
        currentInput = error;
        startNewInput = true;
        statusLine = BuildStatus("CHECK INPUT");
        RefreshDisplay();
    }

    private void ShowStatus(string label)
    {
        statusLine = BuildStatus(label);
    }

    private string BuildStatus(string label)
    {
        return programMode ? $"PROGRAM | {label}" : label;
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

        string topLine = FitDisplayLine(expressionLine, 20, 18);
        string mainLine = FitDisplayLine(currentInput, 12, 12);
        string bottomLine = FitDisplayLine(statusLine, 18, 16);

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

    private void ApplyIndicatorState()
    {
        EnsureIndicators();

        bool greenOn = machinePowered && !isCutting;
        bool redOn = machinePowered && isCutting;

        SetIndicatorEmission(greenIndicatorRenderers, greenIndicatorEmissionColor, greenOn ? greenIndicatorEmission : 0f);
        SetIndicatorEmission(redIndicatorRenderers, redIndicatorEmissionColor, redOn ? redIndicatorEmission : 0f);
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
