using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NumDeck.Core.Models;
using NumDeck.Input.Native;

namespace NumDeck.Input;

public enum ConnectionKind { Usb, Bluetooth, Internal, Virtual, Unknown }

public sealed record KeyboardDevice(
    nint Handle, string DevicePath, string Name, string? VendorId, string? ProductId, string? Interface,
    ConnectionKind Connection)
{
    public string ConnectionText => Connection switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.Internal => "Interno",
        ConnectionKind.Virtual => "Virtual",
        _ => "?",
    };

    public string DisplayName =>
        VendorId is null ? $"{Name} ({ConnectionText})" : $"{Name} ({ConnectionText} · {VendorId}:{ProductId}{(Interface is null ? "" : " · " + Interface)})";

    public DeviceSelection ToSelection() => new()
    {
        DevicePath = DevicePath, VendorId = VendorId, ProductId = ProductId, Interface = Interface, DisplayName = DisplayName,
    };

    public override string ToString() => DisplayName;
}

public static partial class DeviceEnumerator
{
    public static List<KeyboardDevice> GetKeyboards()
    {
        uint count = 0, size = (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICELIST>();
        Win32.GetRawInputDeviceList(null, ref count, size);
        if (count == 0) return new();
        var list = new Win32.RAWINPUTDEVICELIST[count];
        if (Win32.GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return new();

        var result = new List<KeyboardDevice>();
        foreach (var d in list.Take((int)count))
        {
            if (d.dwType != Win32.RIM_TYPEKEYBOARD) continue;
            var path = GetDevicePath(d.hDevice);
            if (path is null) continue;
            result.Add(Describe(d.hDevice, path));
        }
        return result;
    }

    public static string? GetDevicePath(nint device)
    {
        uint chars = 0;
        Win32.GetRawInputDeviceInfoW(device, Win32.RIDI_DEVICENAME, null, ref chars);
        if (chars == 0) return null;
        var buf = new char[chars];
        if (Win32.GetRawInputDeviceInfoW(device, Win32.RIDI_DEVICENAME, buf, ref chars) == uint.MaxValue) return null;
        return new string(buf).TrimEnd('\0');
    }

    public static KeyboardDevice Describe(nint handle, string path)
    {
        var (vid, pid, iface) = ParseIds(path);
        var name = ReadHidProduct(path) ?? ReadRegistryDescription(path) ?? "Teclado";
        return new KeyboardDevice(handle, path, name, vid, pid, iface, Classify(path));
    }

    /// <summary>Picks the configured deck among connected keyboards (exact path, then VID/PID/interface).</summary>
    public static KeyboardDevice? FindSelected(IEnumerable<KeyboardDevice> devices, DeviceSelection? sel)
    {
        if (sel is null) return null;
        var all = devices.ToList();
        return all.FirstOrDefault(d => string.Equals(d.DevicePath, sel.DevicePath, StringComparison.OrdinalIgnoreCase))
            ?? (sel.VendorId is null ? null
                : all.FirstOrDefault(d => d.VendorId == sel.VendorId && d.ProductId == sel.ProductId && d.Interface == sel.Interface)
                  ?? all.FirstOrDefault(d => d.VendorId == sel.VendorId && d.ProductId == sel.ProductId));
    }

    [GeneratedRegex(@"VID[_&]([0-9A-F]{4,8})", RegexOptions.IgnoreCase)] private static partial Regex VidRegex();
    [GeneratedRegex(@"PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase)] private static partial Regex PidRegex();
    [GeneratedRegex(@"(MI_[0-9A-F]{2})|(Col[0-9A-F]{2})", RegexOptions.IgnoreCase)] private static partial Regex IfaceRegex();

    public static (string? Vid, string? Pid, string? Iface) ParseIds(string path)
    {
        var vid = VidRegex().Match(path);
        var pid = PidRegex().Match(path);
        // Bluetooth paths carry an 8-digit "VID&0002046d": the last 4 digits are the vendor.
        string? v = vid.Success ? vid.Groups[1].Value[^4..].ToUpperInvariant() : null;
        string? p = pid.Success ? pid.Groups[1].Value.ToUpperInvariant() : null;
        var parts = IfaceRegex().Matches(path).Select(m => m.Value.ToUpperInvariant()).ToList();
        return (v, p, parts.Count == 0 ? null : string.Join(" ", parts));
    }

    public static ConnectionKind Classify(string path)
    {
        var p = path.ToUpperInvariant();
        if (p.Contains("{00001124-0000-1000-8000-00805F9B34FB}") || p.Contains("{00001812-0000-1000-8000-00805F9B34FB}")
            || p.Contains("BTHENUM") || p.Contains("BTHLE"))
            return ConnectionKind.Bluetooth;
        if (p.Contains("VID_") || p.Contains("USB")) return ConnectionKind.Usb;
        if (p.Contains(@"\\?\ACPI") || p.Contains("#ACPI#")) return ConnectionKind.Internal;
        if (p.Contains(@"\\?\ROOT") || p.Contains("RDP_KBD") || p.Contains("TERMINPUT")) return ConnectionKind.Virtual;
        return ConnectionKind.Unknown;
    }

    private static string? ReadHidProduct(string path)
    {
        // Zero access rights are enough to query HID strings, even on keyboards Windows holds exclusively.
        var h = Win32.CreateFileW(path, 0, 3 /* share read|write */, 0, 3 /* OPEN_EXISTING */, 0, 0);
        if (h == -1 || h == 0) return null;
        try
        {
            var buf = new byte[256];
            string? product = Win32.HidD_GetProductString(h, buf, (uint)buf.Length) ? Decode(buf) : null;
            Array.Clear(buf);
            string? maker = Win32.HidD_GetManufacturerString(h, buf, (uint)buf.Length) ? Decode(buf) : null;
            if (string.IsNullOrWhiteSpace(product)) return null;
            if (!string.IsNullOrWhiteSpace(maker) && !product.Contains(maker, StringComparison.OrdinalIgnoreCase))
                return $"{maker} {product}";
            return product;
        }
        finally { Win32.CloseHandle(h); }
    }

    private static string? Decode(byte[] buf)
    {
        var s = Encoding.Unicode.GetString(buf);
        var end = s.IndexOf('\0');
        return (end >= 0 ? s[..end] : s).Trim();
    }

    private static string? ReadRegistryDescription(string path)
    {
        try
        {
            // \\?\HID#VID_xxxx&PID_yyyy#7&abc&0&0000#{guid}  ->  HID\VID_xxxx&PID_yyyy\7&abc&0&0000
            var s = path.StartsWith(@"\\?\") ? path[4..] : path;
            var guid = s.LastIndexOf("#{", StringComparison.Ordinal);
            if (guid > 0) s = s[..guid];
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + s.Replace('#', '\\'));
            var desc = key?.GetValue("FriendlyName") as string ?? key?.GetValue("DeviceDesc") as string;
            if (desc is null) return null;
            var semi = desc.LastIndexOf(';'); // "@keyboard.inf,%hid_device%;Dispositivo de teclado HID"
            return semi >= 0 ? desc[(semi + 1)..] : desc;
        }
        catch { return null; }
    }

    /// <summary>Localized key name ("7 (teclado numérico)") for keys outside the numpad layout.</summary>
    public static string KeyName(KeyId key)
    {
        var buf = new char[64];
        int lParam = (key.ScanCode << 16) | (key.Extended ? 1 << 24 : 0);
        int n = Win32.GetKeyNameTextW(lParam, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : $"Tecla {key}";
    }
}
