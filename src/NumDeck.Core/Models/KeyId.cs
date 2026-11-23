namespace NumDeck.Core.Models;

/// <summary>
/// Identifies a physical key by its scan code (+ E0 "extended" prefix).
/// Scan codes don't change with NumLock, unlike virtual-key codes.
/// </summary>
public readonly record struct KeyId(ushort ScanCode, bool Extended)
{
    // NumLock is reported without E0 by Raw Input but as "extended" by hooks.
    public bool Matches(ushort scanCode, bool extended) =>
        ScanCode == scanCode && (Extended == extended || ScanCode == 0x45);

    public override string ToString() => Extended ? $"E0 {ScanCode:X2}" : $"{ScanCode:X2}";
}
