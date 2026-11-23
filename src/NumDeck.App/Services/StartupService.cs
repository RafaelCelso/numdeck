using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace NumDeck.App.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NumDeck";

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>If autostart is on but points at another copy of NumDeck (moved/updated), point it here.</summary>
    public static void RepairPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string s && !s.Contains(ExePath, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, $"\"{ExePath}\" --minimized");
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\" --minimized");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Starts an elevated copy that waits for this one to exit. Returns false if the user cancelled UAC.</summary>
    public static bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, $"--wait-for {Environment.ProcessId}")
            {
                UseShellExecute = true, Verb = "runas",
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
