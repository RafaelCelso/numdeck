using System.Diagnostics;
using System.Runtime.InteropServices;
using NumDeck.Core.Actions;

namespace NumDeck.Core.Execution;

/// <summary>Opens File Explorer, reusing a window that already shows the folder when asked to.</summary>
public static partial class FolderOpener
{
    public static void Open(OpenFolderAction a)
    {
        var target = a.Target;
        if (a.Location == FolderLocation.Custom)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Nenhuma pasta escolhida.");
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Pasta não encontrada: " + target);
            target = Path.GetFullPath(target);
        }

        if (a.ReuseOpenWindow && a.Location != FolderLocation.Home && ResolvePath(a.Location, target) is { } path
            && TryActivateWindowShowing(path))
            return;

        Process.Start(new ProcessStartInfo("explorer.exe", ExplorerArgument(target)) { UseShellExecute = false })?.Dispose();
    }

    /// <summary>Quotes the folder; a trailing backslash would escape the closing quote, so it is dropped (except for drive roots).</summary>
    public static string ExplorerArgument(string target)
    {
        if (target.Length == 0) return "";
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return "\"" + target + "\"";
        if (Path.GetPathRoot(target) is { } root && string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
            return target.EndsWith('\\') ? target : target + "\\"; // "C:\" (no spaces, unquoted)
        return "\"" + target.TrimEnd('\\', '/') + "\"";
    }

    /// <summary>The path an Explorer window reports for this location (Folder.Self.Path).</summary>
    private static string? ResolvePath(FolderLocation location, string customPath) => location switch
    {
        FolderLocation.ThisPC => "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
        FolderLocation.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        FolderLocation.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        FolderLocation.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        FolderLocation.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        FolderLocation.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        FolderLocation.Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")),
        FolderLocation.Custom => customPath,
        _ => null,
    };

    private static bool TryActivateWindowShowing(string path)
    {
        nint hwnd = 0;
        var thread = new Thread(() =>
        {
            try { hwnd = FindExplorerWindow(path); } catch { }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(3)) || hwnd == 0) return false;
        WindowActivator.Activate(hwnd);
        return true;
    }

    private static nint FindExplorerWindow(string path)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return 0;
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    string full = w.FullName ?? "";
                    if (!full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string shown = w.Document.Folder.Self.Path ?? "";
                    if (SamePath(shown, path)) return (nint)Convert.ToInt64(w.HWND);
                }
                catch { /* window closing, non-folder view… */ }
                finally { if (Marshal.IsComObject(w)) Marshal.ReleaseComObject(w); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return 0;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}
