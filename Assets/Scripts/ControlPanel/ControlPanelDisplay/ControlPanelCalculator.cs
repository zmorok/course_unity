using System.Globalization;
using UnityEngine;

// состояние ввода панели отдельно от отображения готовых строк
public sealed class ControlPanelCalculator
{
    private enum QueuedOperation
    {
        None,
        Add,
        Subtract,
        Multiply,
        Divide
    }

    private double? storedOperand;
    private QueuedOperation queuedOperation = QueuedOperation.None;
    private string currentInput = "0";
    private string expressionLine = string.Empty;
    private string statusLine = "READY";
    private bool startNewInput;
    private bool programMode;
    private bool hasError;

    public ControlPanelDisplayState State => new(expressionLine, currentInput, statusLine);

    public bool HasQueuedOperationReady => queuedOperation != QueuedOperation.None && storedOperand.HasValue && !startNewInput;

    public void AppendDigit(char digit)
    {
        PrepareForInput();

        if (startNewInput || currentInput == "0")
            currentInput = digit.ToString();
        else
            currentInput += digit;

        startNewInput = false;
        ShowStatus($"KEY {digit}");
    }

    public void AppendDot()
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
    }

    public void DeleteLastCharacter()
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
    }

    // метод после команды Enter:
    // для обработки введённого числа как значения реза
    public bool TryParseCurrentInput(out double value)
    {
        return double.TryParse(currentInput, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // нужен для цепочек вроде 2 + 3 + 4:
    // предыдущая операция считается до постановки новой
    public void QueueOperation(ControlPanelButton button)
    {
        if (!TryParseCurrentInput(out double currentValue))
        {
            SetError("INPUT ERROR");
            return;
        }

        QueuedOperation nextOperation = ToQueuedOperation(button);

        if (queuedOperation != QueuedOperation.None && !startNewInput)
        {
            if (!TryEvaluate(storedOperand ?? 0d, currentValue, queuedOperation, out double chainedResult, out string error))
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

        queuedOperation = nextOperation;
        startNewInput = true;
        hasError = false;
        expressionLine = $"{FormatNumber(storedOperand ?? currentValue)} {GetOperatorSymbol(nextOperation)}";
        ShowStatus($"OP {GetOperatorSymbol(nextOperation)}");
    }

    public void EvaluateQueuedOperation(string completedStatus)
    {
        if (queuedOperation == QueuedOperation.None || !storedOperand.HasValue)
        {
            ShowStatus(completedStatus);
            return;
        }

        if (!TryParseCurrentInput(out double secondOperand))
        {
            SetError("INPUT ERROR");
            return;
        }

        double firstOperand = storedOperand.Value;

        if (!TryEvaluate(firstOperand, secondOperand, queuedOperation, out double result, out string error))
        {
            SetError(error);
            return;
        }

        expressionLine = $"{FormatNumber(firstOperand)} {GetOperatorSymbol(queuedOperation)} {FormatNumber(secondOperand)} =";
        currentInput = FormatNumber(result);
        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        startNewInput = true;
        hasError = false;
        ShowStatus(completedStatus);
    }

    public void ToggleProgramMode()
    {
        programMode = !programMode;
        ShowStatus(programMode ? "PROGRAM MODE" : "CALC MODE");
    }

    public void ResetState(bool showClearStatus)
    {
        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        currentInput = "0";
        expressionLine = string.Empty;
        startNewInput = false;
        hasError = false;
        statusLine = showClearStatus ? BuildStatus("CLEAR") : BuildStatus("READY");
    }

    public void ResetForPowerOff()
    {
        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        currentInput = string.Empty;
        expressionLine = string.Empty;
        statusLine = string.Empty;
        startNewInput = false;
        programMode = false;
        hasError = false;
    }

    // метод после принятой команды реза:
    // чтобы дисплей показывал CUT-команду, а не обычный результат калькулятора
    public void ApplyCutCommandAccepted(double cutSize, string statusLabel)
    {
        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        expressionLine = $"CUT {FormatNumber(cutSize)} MM";
        startNewInput = true;
        hasError = false;
        ShowStatus(statusLabel);
    }

    public void SetError(string error)
    {
        hasError = true;
        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        expressionLine = string.Empty;
        currentInput = error;
        startNewInput = true;
        statusLine = BuildStatus("CHECK INPUT");
    }

    public void ShowStatus(string label)
    {
        statusLine = BuildStatus(label);
    }

    // метод после ошибки:
    // первый новый ввод должен очистить текст ошибки
    private void PrepareForInput()
    {
        if (!hasError)
            return;

        storedOperand = null;
        queuedOperation = QueuedOperation.None;
        currentInput = "0";
        expressionLine = string.Empty;
        startNewInput = false;
        hasError = false;
    }

    private bool TryEvaluate(double firstOperand, double secondOperand, QueuedOperation operation, out double result, out string error)
    {
        result = 0d;
        error = string.Empty;

        switch (operation)
        {
            case QueuedOperation.Add:
                result = firstOperand + secondOperand;
                break;
            case QueuedOperation.Subtract:
                result = firstOperand - secondOperand;
                break;
            case QueuedOperation.Multiply:
                result = firstOperand * secondOperand;
                break;
            case QueuedOperation.Divide:
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

    private QueuedOperation ToQueuedOperation(ControlPanelButton button)
    {
        return button switch
        {
            ControlPanelButton.Plus => QueuedOperation.Add,
            ControlPanelButton.Minus => QueuedOperation.Subtract,
            ControlPanelButton.Mult => QueuedOperation.Multiply,
            ControlPanelButton.Div => QueuedOperation.Divide,
            _ => QueuedOperation.None
        };
    }

    private string GetOperatorSymbol(QueuedOperation operation)
    {
        return operation switch
        {
            QueuedOperation.Add => "+",
            QueuedOperation.Subtract => "-",
            QueuedOperation.Multiply => "*",
            QueuedOperation.Divide => "/",
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

    private string BuildStatus(string label)
    {
        return programMode ? $"PROGRAM | {label}" : label;
    }
}
