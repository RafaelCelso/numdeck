using System.Globalization;

namespace NumDeck.Core.Macros;

public sealed record KeyCombo(IReadOnlyList<ushort> Modifiers, ushort Key)
{
    public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    private static readonly Dictionary<string, ushort> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VK_CONTROL, ["control"] = VK_CONTROL,
        ["shift"] = VK_SHIFT,
        ["alt"] = VK_MENU,
        ["win"] = VK_LWIN, ["windows"] = VK_LWIN,
    };

    // Static initializers run in textual order: DisplayNames must exist first.
    private static readonly Dictionary<ushort, string> DisplayNames = BuildDisplayNames();
    private static readonly Dictionary<string, ushort> KeyNames = BuildKeyNames();

    private static Dictionary<string, ushort> BuildKeyNames()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B,
            ["space"] = 0x20, ["espaco"] = 0x20, ["espaço"] = 0x20, ["backspace"] = 0x08,
            ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["ins"] = 0x2D,
            ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14, ["apps"] = 0x5D, ["menu"] = 0x5D,
            ["plus"] = 0xBB, ["minus"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE,
            ["nummultiply"] = 0x6A, ["numadd"] = 0x6B, ["numsubtract"] = 0x6D, ["numdecimal"] = 0x6E, ["numdivide"] = 0x6F,
        };
        foreach (var (vk, name) in DisplayNames) d.TryAdd(name, vk);
        return d;
    }

    /// <summary>Canonical names (used by Format, also accepted by TryParse).</summary>
    private static Dictionary<ushort, string> BuildDisplayNames()
    {
        var d = new Dictionary<ushort, string>
        {
            [0x0D] = "Enter", [0x09] = "Tab", [0x1B] = "Esc", [0x20] = "Space", [0x08] = "Backspace",
            [0x2E] = "Delete", [0x2D] = "Insert", [0x24] = "Home", [0x23] = "End", [0x21] = "PageUp", [0x22] = "PageDown",
            [0x26] = "Up", [0x28] = "Down", [0x25] = "Left", [0x27] = "Right",
            [0x2C] = "PrintScreen", [0x13] = "Pause", [0x14] = "CapsLock", [0x5D] = "Apps",
            [0xBB] = "Plus", [0xBD] = "Minus", [0xBC] = "Comma", [0xBE] = "Period",
            [0x6A] = "NumMultiply", [0x6B] = "NumAdd", [0x6D] = "NumSubtract", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
        };
        for (int i = 1; i <= 24; i++) d[(ushort)(0x70 + i - 1)] = "F" + i;
        for (int i = 0; i <= 9; i++) d[(ushort)(0x60 + i)] = "Num" + i;
        return d;
    }

    /// <summary>Parses "Ctrl+Shift+M", "Alt+F4", "Win+D", "0x41".</summary>
    public static bool TryParse(string text, out KeyCombo? combo)
    {
        combo = null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        // "Ctrl++" means Ctrl + Plus.
        if (text.EndsWith("++")) parts = parts[..^2].Append("plus").ToArray();
        if (parts.Length == 0 || parts.Any(p => p.Length == 0)) return false;

        var mods = new List<ushort>();
        foreach (var p in parts[..^1])
        {
            if (!ModifierNames.TryGetValue(p, out var vk)) return false;
            if (!mods.Contains(vk)) mods.Add(vk);
        }
        var last = parts[^1];
        if (ModifierNames.TryGetValue(last, out var modOnly)) { combo = new KeyCombo(mods, modOnly); return true; }
        if (!TryParseKey(last, out var key)) return false;
        combo = new KeyCombo(mods, key);
        return true;
    }

    private static bool TryParseKey(string s, out ushort vk)
    {
        vk = 0;
        if (KeyNames.TryGetValue(s, out vk)) return true;
        if (s.Length == 1)
        {
            char c = char.ToUpperInvariant(s[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
            return false;
        }
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 0xFF)
            return true;
        return false;
    }

    /// <summary>Inverse of TryParse, used when recording a shortcut from the keyboard.</summary>
    public static string Format(bool ctrl, bool shift, bool alt, bool win, ushort vk)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        parts.Add(vk is >= 'A' and <= 'Z' or >= '0' and <= '9' ? ((char)vk).ToString()
            : DisplayNames.TryGetValue(vk, out var name) ? name
            : $"0x{vk:X2}");
        return string.Join("+", parts);
    }
}

public abstract record MacroStep;
public sealed record ComboStep(KeyCombo Combo) : MacroStep;
public sealed record TextStep(string Text) : MacroStep;
public sealed record DelayStep(int Milliseconds) : MacroStep;

public static class MacroParser
{
    private static readonly string[] TextPrefixes = { "texto:", "text:" };
    private static readonly string[] DelayPrefixes = { "espera:", "esperar:", "delay:", "wait:" };

    /// <summary>Parses the macro script. Errors are reported per line (1-based).</summary>
    public static List<MacroStep> Parse(string script, out List<string> errors)
    {
        var steps = new List<MacroStep>();
        errors = new List<string>();
        var lines = script.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (StripPrefix(raw.TrimStart(), TextPrefixes) is { } text)
            {
                // Keep inner spaces; drop just one space after the colon.
                steps.Add(new TextStep(text.StartsWith(' ') ? text[1..] : text));
            }
            else if (StripPrefix(line, DelayPrefixes) is { } delay)
            {
                if (int.TryParse(delay.Trim(), out var ms) && ms >= 0 && ms <= 60000) steps.Add(new DelayStep(ms));
                else errors.Add($"Linha {i + 1}: espera inválida \"{delay.Trim()}\" (use milissegundos, até 60000)");
            }
            else if (KeyCombo.TryParse(line, out var combo))
            {
                steps.Add(new ComboStep(combo!));
            }
            else
            {
                errors.Add($"Linha {i + 1}: atalho não reconhecido \"{line}\"");
            }
        }
        return steps;
    }

    private static string? StripPrefix(string line, string[] prefixes)
    {
        foreach (var p in prefixes)
            if (line.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return line[p.Length..];
        return null;
    }
}
