using System.Diagnostics;
using System.Runtime.InteropServices;
using NumDeck.Core.Actions;
using NumDeck.Core.Apps;
using NumDeck.Core.Browsers;
using NumDeck.Core.Macros;

namespace NumDeck.Core.Execution;

/// <summary>Executes actions off the input thread. Profile switching is handled by DeckEngine.</summary>
public sealed partial class ActionRunner : IDisposable
{
    private readonly SoundPlayer _sound = new();
    private readonly Func<IReadOnlyList<BrowserInfo>> _browsers;

    public event Action<string, Exception>? Failed;
    public event Action<string>? Info;

    public ActionRunner(Func<IReadOnlyList<BrowserInfo>> browsers) => _browsers = browsers;

    public Task RunAsync(ActionDefinition action) => Task.Run(() =>
    {
        try { Run(action); }
        catch (Exception ex) { Failed?.Invoke(action.Summary, ex); }
    });

    public void Run(ActionDefinition action)
    {
        switch (action)
        {
            case LaunchAppAction a: Launch(a); break;
            case OpenBrowserAction a: OpenBrowser(a); break;
            case OpenFolderAction a: FolderOpener.Open(a); break;
            case PlaySoundAction a: _sound.Play(a); break;
            case MacroAction a: RunMacro(a); break;
            case MediaAction a: Media(a); break;
            case SwitchProfileAction: break; // DeckEngine
        }
    }

    private static void Launch(LaunchAppAction a)
    {
        var apps = a.Apps.Where(x => !string.IsNullOrWhiteSpace(x.Path)).ToList();
        if (apps.Count == 0) throw new InvalidOperationException("Nenhum aplicativo escolhido.");

        var errors = new List<string>();
        foreach (var app in apps)
        {
            try { LaunchOne(app, a.BringToFrontIfRunning); }
            catch (Exception ex) { errors.Add($"{app.DisplayName}: {ex.Message}"); }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" · ", errors));
    }

    private static void LaunchOne(AppEntry app, bool bringToFront)
    {
        var exe = string.IsNullOrWhiteSpace(app.ExePath) ? app.Path : app.ExePath;
        if (bringToFront && TryActivateRunning(exe)) return;

        if (InstalledApps.IsAppsFolderPath(app.Path))
        {
            // Arguments can't go through the Start menu entry: start the program itself then.
            if (!string.IsNullOrWhiteSpace(app.Arguments) && File.Exists(app.ExePath))
                StartFile(app.ExePath, app.Arguments, app.WorkingDirectory);
            else
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + app.Path + "\"") { UseShellExecute = false })?.Dispose();
            return;
        }
        StartFile(app.Path, app.Arguments, app.WorkingDirectory);
    }

    private static void StartFile(string path, string? arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = true, // supports .lnk, documents, URLs
            Arguments = arguments ?? "",
        };
        var dir = string.IsNullOrWhiteSpace(workingDirectory) ? SafeDirectory(path) : workingDirectory;
        if (dir is not null) psi.WorkingDirectory = dir;
        Process.Start(psi)?.Dispose();
    }

    private static string? SafeDirectory(string path)
    {
        try { return File.Exists(path) ? Path.GetDirectoryName(path) : null; } catch { return null; }
    }

    private static bool TryActivateRunning(string path)
    {
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                if (p.MainWindowHandle == 0) continue;
                WindowActivator.Activate(p.MainWindowHandle);
                return true;
            }
        }
        return false;
    }

    private void OpenBrowser(OpenBrowserAction a)
    {
        string? exe = a.BrowserId == OpenBrowserAction.CustomBrowserId
            ? a.CustomPath
            : _browsers().FirstOrDefault(b => b.Id == a.BrowserId)?.ExePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException($"Navegador \"{a.BrowserName}\" não encontrado.");
        Process.Start(BrowserCommand.BuildStartInfo(exe, a));
    }

    private static void RunMacro(MacroAction a)
    {
        var steps = MacroParser.Parse(a.Script, out var errors);
        if (errors.Count > 0) throw new FormatException(string.Join(Environment.NewLine, errors));
        // Let the deck key's own key-up settle so it doesn't mix with the combo.
        Thread.Sleep(30);
        foreach (var step in steps)
        {
            switch (step)
            {
                case ComboStep c: InputSender.SendCombo(c.Combo); break;
                case TextStep t: InputSender.SendText(t.Text); break;
                case DelayStep d: Thread.Sleep(d.Milliseconds); break;
            }
            Thread.Sleep(15);
        }
    }

    private void Media(MediaAction a)
    {
        ushort? vk = a.Command switch
        {
            MediaCommand.PlayPause => 0xB3,
            MediaCommand.NextTrack => 0xB0,
            MediaCommand.PreviousTrack => 0xB1,
            MediaCommand.Stop => 0xB2,
            MediaCommand.VolumeUp => 0xAF,
            MediaCommand.VolumeDown => 0xAE,
            MediaCommand.VolumeMute => 0xAD,
            _ => null,
        };
        if (vk is { } v) { InputSender.Tap(v); return; }
        if (a.Command == MediaCommand.MicrophoneMute)
            Info?.Invoke(AudioDevices.ToggleMicrophoneMute() ? "Microfone mudo" : "Microfone ativado");
    }

    public void StopSounds() => _sound.StopAll();

    public void Dispose() => _sound.Dispose();
}

internal static partial class WindowActivator
{
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint h);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint h, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint h);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint h, out uint pid);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AttachThreadInput(uint a, uint b, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool BringWindowToTop(nint h);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();

    public static void Activate(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        if (SetForegroundWindow(hwnd)) return;
        // Foreground lock: temporarily attach to the foreground thread's input queue.
        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var me = GetCurrentThreadId();
        if (fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true))
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            AttachThreadInput(me, fgThread, false);
        }
    }
}
