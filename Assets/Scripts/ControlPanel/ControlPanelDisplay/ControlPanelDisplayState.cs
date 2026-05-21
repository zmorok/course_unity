// снимок строк дисплея для отделения расчётов панели от отрисовки
public readonly struct ControlPanelDisplayState
{
    public ControlPanelDisplayState(string expressionLine, string currentInput, string statusLine)
    {
        ExpressionLine = expressionLine;
        CurrentInput = currentInput;
        StatusLine = statusLine;
    }

    public string ExpressionLine { get; }
    public string CurrentInput { get; }
    public string StatusLine { get; }
}
