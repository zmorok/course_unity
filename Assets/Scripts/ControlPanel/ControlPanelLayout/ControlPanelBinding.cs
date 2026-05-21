using UnityEngine.InputSystem;

// описание одной физической клавиши для одной логической кнопки на панели управления симулятора
// разделение для кнопок с Shift : Z+key и Z+Shift+key
public readonly struct ControlPanelBinding
{
    public ControlPanelBinding(Key key, ControlPanelButton button, bool requiresShift)
    {
        Key = key;
        Button = button;
        RequiresShift = requiresShift;
    }

    public Key Key { get; }
    public ControlPanelButton Button { get; }
    public bool RequiresShift { get; }
}
