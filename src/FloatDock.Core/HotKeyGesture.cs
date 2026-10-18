namespace FloatDock.Core;

[Flags]
public enum HotKeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>A global shortcut such as "Ctrl+Alt+Space": modifiers (values match MOD_*) and one virtual key.</summary>
public readonly record struct HotKeyGesture(HotKeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly (string Name, uint Key)[] Named = [("Space", 0x20), ("Tab", 0x09), ("Enter", 0x0D)];

    /// <summary>Letters, digits, F1 to F24, Space, Tab and Enter, with at least one of Ctrl, Alt or Win (Shift alone types text).</summary>
    public static bool TryParse(string? text, out HotKeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = HotKeyModifiers.None;
        uint key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotKeyModifiers.Control; continue;
                case "alt": modifiers |= HotKeyModifiers.Alt; continue;
                case "shift": modifiers |= HotKeyModifiers.Shift; continue;
                case "win": modifiers |= HotKeyModifiers.Win; continue;
            }
            if (key != 0 || KeyFor(raw) is not { } parsed) return false;
            key = parsed;
        }
        if (key == 0 || (modifiers & ~HotKeyModifiers.Shift) == 0) return false;
        gesture = new HotKeyGesture(modifiers, key);
        return true;
    }

    private static uint? KeyFor(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) return name[0];
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24)
            return (uint)(0x70 + number - 1);
        foreach (var (named, key) in Named)
            if (named.Equals(name, StringComparison.OrdinalIgnoreCase)) return key;
        return null;
    }

    /// <summary>The text <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & HotKeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotKeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotKeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotKeyModifiers.Win) != 0) parts.Add("Win");
        var virtualKey = VirtualKey;
        parts.Add(virtualKey switch
        {
            >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x70 + 1),
            _ when Named.FirstOrDefault(n => n.Key == virtualKey).Name is { } named => named,
            _ => ((char)virtualKey).ToString()
        });
        return string.Join('+', parts);
    }
}
