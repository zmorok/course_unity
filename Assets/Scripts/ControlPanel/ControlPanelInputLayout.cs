using System.Collections.Generic;
using UnityEngine.InputSystem;

public enum ControlPanelButton
{
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Center,
    Delete,
    Enter,
    Clear,
    Program,
    Dot,
    Div,
    Mult,
    Minus,
    Plus,
    Eq,
    DualStart,
    PowerSwitch,
    EmergencyStop
}

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

public static class ControlPanelInputLayout
{
    private static readonly ControlPanelBinding[] bindings =
    {
        new(Key.Digit0, ControlPanelButton.Digit0, false),
        new(Key.Digit1, ControlPanelButton.Digit1, false),
        new(Key.Digit2, ControlPanelButton.Digit2, false),
        new(Key.Digit3, ControlPanelButton.Digit3, false),
        new(Key.Digit4, ControlPanelButton.Digit4, false),
        new(Key.Digit5, ControlPanelButton.Digit5, false),
        new(Key.Digit6, ControlPanelButton.Digit6, false),
        new(Key.Digit7, ControlPanelButton.Digit7, false),
        new(Key.Digit8, ControlPanelButton.Digit8, false),
        new(Key.Digit9, ControlPanelButton.Digit9, false),
        new(Key.UpArrow, ControlPanelButton.ArrowUp, false),
        new(Key.DownArrow, ControlPanelButton.ArrowDown, false),
        new(Key.LeftArrow, ControlPanelButton.ArrowLeft, false),
        new(Key.RightArrow, ControlPanelButton.ArrowRight, false),
        new(Key.RightAlt, ControlPanelButton.Center, false),
        new(Key.Backspace, ControlPanelButton.Delete, false),
        new(Key.Enter, ControlPanelButton.Enter, false),
        new(Key.C, ControlPanelButton.Clear, false),
        new(Key.P, ControlPanelButton.Program, false),
        new(Key.Period, ControlPanelButton.Dot, true),
        new(Key.Slash, ControlPanelButton.Div, true),
        new(Key.Digit8, ControlPanelButton.Mult, true),
        new(Key.Minus, ControlPanelButton.Minus, true),
        new(Key.Equals, ControlPanelButton.Plus, true),
        new(Key.Enter, ControlPanelButton.Eq, true),
        new(Key.V, ControlPanelButton.DualStart, false),
        new(Key.O, ControlPanelButton.PowerSwitch, false),
        new(Key.X, ControlPanelButton.EmergencyStop, false)
    };

    public static IReadOnlyList<ControlPanelBinding> Bindings => bindings;

    public static IReadOnlyList<string> GetObjectNames(ControlPanelButton button)
    {
        return button switch
        {
            ControlPanelButton.Digit0 => new[] { "btn_0" },
            ControlPanelButton.Digit1 => new[] { "btn_1" },
            ControlPanelButton.Digit2 => new[] { "btn_2" },
            ControlPanelButton.Digit3 => new[] { "btn_3" },
            ControlPanelButton.Digit4 => new[] { "btn_4" },
            ControlPanelButton.Digit5 => new[] { "btn_5" },
            ControlPanelButton.Digit6 => new[] { "btn_6" },
            ControlPanelButton.Digit7 => new[] { "btn_7" },
            ControlPanelButton.Digit8 => new[] { "btn_8" },
            ControlPanelButton.Digit9 => new[] { "btn_9" },
            ControlPanelButton.ArrowUp => new[] { "btn_ArrowUp" },
            ControlPanelButton.ArrowDown => new[] { "btn_ArrowDown" },
            ControlPanelButton.ArrowLeft => new[] { "btn_ArrowLeft" },
            ControlPanelButton.ArrowRight => new[] { "btn_ArrowRight" },
            ControlPanelButton.Center => new[] { "btn_Center" },
            ControlPanelButton.Delete => new[] { "btn_Delete" },
            ControlPanelButton.Enter => new[] { "btn_Enter" },
            ControlPanelButton.Clear => new[] { "btn_Clear" },
            ControlPanelButton.Program => new[] { "btn_Program" },
            ControlPanelButton.Dot => new[] { "btn_Dot" },
            ControlPanelButton.Div => new[] { "btn_Div" },
            ControlPanelButton.Mult => new[] { "btn_Mult" },
            ControlPanelButton.Minus => new[] { "btn_Minus" },
            ControlPanelButton.Plus => new[] { "btn_Plus" },
            ControlPanelButton.Eq => new[] { "btn_Eq" },
            ControlPanelButton.DualStart => new[] { "CTRL_TwoHandStart_L", "CTRL_TwoHandStart_R" },
            ControlPanelButton.PowerSwitch => new[] { "_PowerSwitch" },
            ControlPanelButton.EmergencyStop => new[] { "PANEL_EStop" },
            _ => System.Array.Empty<string>()
        };
    }

    public static bool UsesOneShotClip(ControlPanelButton button)
    {
        return button is
            ControlPanelButton.DualStart or
            ControlPanelButton.PowerSwitch or
            ControlPanelButton.EmergencyStop;
    }

    public static bool TryGetDigit(ControlPanelButton button, out char digit)
    {
        switch (button)
        {
            case ControlPanelButton.Digit0: digit = '0'; return true;
            case ControlPanelButton.Digit1: digit = '1'; return true;
            case ControlPanelButton.Digit2: digit = '2'; return true;
            case ControlPanelButton.Digit3: digit = '3'; return true;
            case ControlPanelButton.Digit4: digit = '4'; return true;
            case ControlPanelButton.Digit5: digit = '5'; return true;
            case ControlPanelButton.Digit6: digit = '6'; return true;
            case ControlPanelButton.Digit7: digit = '7'; return true;
            case ControlPanelButton.Digit8: digit = '8'; return true;
            case ControlPanelButton.Digit9: digit = '9'; return true;
            default:
                digit = default;
                return false;
        }
    }

    public static bool TryGetOperatorSymbol(ControlPanelButton button, out string symbol)
    {
        switch (button)
        {
            case ControlPanelButton.Plus: symbol = "+"; return true;
            case ControlPanelButton.Minus: symbol = "-"; return true;
            case ControlPanelButton.Mult: symbol = "*"; return true;
            case ControlPanelButton.Div: symbol = "/"; return true;
            default:
                symbol = string.Empty;
                return false;
        }
    }

    public static string GetLabel(ControlPanelButton button)
    {
        if (TryGetDigit(button, out char digit))
            return digit.ToString();

        return button switch
        {
            ControlPanelButton.ArrowUp => "UP",
            ControlPanelButton.ArrowDown => "DOWN",
            ControlPanelButton.ArrowLeft => "LEFT",
            ControlPanelButton.ArrowRight => "RIGHT",
            ControlPanelButton.Center => "CENTER",
            ControlPanelButton.Delete => "DELETE",
            ControlPanelButton.Enter => "ENTER",
            ControlPanelButton.Clear => "CLEAR",
            ControlPanelButton.Program => "PROGRAM",
            ControlPanelButton.Dot => "DOT",
            ControlPanelButton.Div => "DIV",
            ControlPanelButton.Mult => "MULT",
            ControlPanelButton.Minus => "MINUS",
            ControlPanelButton.Plus => "PLUS",
            ControlPanelButton.Eq => "EQUALS",
            ControlPanelButton.DualStart => "DUAL START",
            ControlPanelButton.PowerSwitch => "POWER",
            ControlPanelButton.EmergencyStop => "E-STOP",
            _ => button.ToString().ToUpperInvariant()
        };
    }
}
