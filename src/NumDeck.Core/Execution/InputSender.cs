using System.Runtime.InteropServices;
using NumDeck.Core.Macros;

namespace NumDeck.Core.Execution;

/// <summary>Synthesizes keyboard input with SendInput.</summary>
public static partial class InputSender
{
    /// <summary>Tag put in dwExtraInfo so NumDeck can recognise its own injected keys.</summary>
    public static readonly nint Signature = 0x4E554D44; // "NUMD"

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    // INPUT is 40 bytes on x64: the union is sized by MOUSEINPUT (32 bytes).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    private static readonly HashSet<ushort> ExtendedVks = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home arrows
        0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0xA3, 0xA5, // PrtSc Ins Del Win Apps Num/ RCtrl RAlt
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, // media + volume
    };

    private static INPUT Key(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (ExtendedVks.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, 0), dwFlags = flags, dwExtraInfo = Signature }
        };
    }

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>()) != inputs.Count)
            throw new InvalidOperationException(
                "O Windows bloqueou o envio de teclas (o app em foco pode estar rodando como administrador).");
    }

    public static void Tap(ushort vk) => Send(new() { Key(vk, false), Key(vk, true) });

    public static void SendCombo(KeyCombo combo)
    {
        var inputs = new List<INPUT>();
        foreach (var m in combo.Modifiers) inputs.Add(Key(m, false));
        inputs.Add(Key(combo.Key, false));
        inputs.Add(Key(combo.Key, true));
        for (int i = combo.Modifiers.Count - 1; i >= 0; i--) inputs.Add(Key(combo.Modifiers[i], true));
        Send(inputs);
    }

    public static void SendText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); continue; }
            foreach (var up in new[] { false, true })
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = Signature }
                });
        }
        Send(inputs);
    }
}
