namespace NumDeck.Core.Models;

/// <summary>A key in the on-screen numpad grid.</summary>
public sealed record NumpadKey(KeyId Key, string Label, int Row, int Column, int RowSpan = 1, int ColumnSpan = 1);

public static class NumpadLayout
{
    /// <summary>Standard 17-key numpad, positioned on a 5x4 grid.</summary>
    public static readonly IReadOnlyList<NumpadKey> Keys = new[]
    {
        new NumpadKey(new(0x45, false), "Num", 0, 0),
        new NumpadKey(new(0x35, true), "/", 0, 1),
        new NumpadKey(new(0x37, false), "*", 0, 2),
        new NumpadKey(new(0x4A, false), "−", 0, 3),
        new NumpadKey(new(0x47, false), "7", 1, 0),
        new NumpadKey(new(0x48, false), "8", 1, 1),
        new NumpadKey(new(0x49, false), "9", 1, 2),
        new NumpadKey(new(0x4E, false), "+", 1, 3, RowSpan: 2),
        new NumpadKey(new(0x4B, false), "4", 2, 0),
        new NumpadKey(new(0x4C, false), "5", 2, 1),
        new NumpadKey(new(0x4D, false), "6", 2, 2),
        new NumpadKey(new(0x4F, false), "1", 3, 0),
        new NumpadKey(new(0x50, false), "2", 3, 1),
        new NumpadKey(new(0x51, false), "3", 3, 2),
        new NumpadKey(new(0x1C, true), "Enter", 3, 3, RowSpan: 2),
        new NumpadKey(new(0x52, false), "0", 4, 0, ColumnSpan: 2),
        new NumpadKey(new(0x53, false), ",", 4, 2),
    };

    public static bool IsStandard(KeyId key) => Keys.Any(k => k.Key == key);
}
