using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NumDeck.Core.Apps;

/// <summary>An application from the Start menu.</summary>
/// <param name="Name">Name shown in the Start menu.</param>
/// <param name="LaunchPath"><c>shell:AppsFolder\&lt;id&gt;</c>: opens it exactly like the Start menu does.</param>
/// <param name="ExePath">Program file, when known, used to find a running instance ("" otherwise).</param>
public sealed record InstalledApp(string Name, string LaunchPath, string ExePath);

/// <summary>Lists the applications of the Start menu ("shell:AppsFolder"): desktop programs and Store apps.</summary>
public static partial class InstalledApps
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static bool IsAppsFolderPath(string? path) =>
        path is not null && path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<InstalledApp> Enumerate()
    {
        // Shell objects want an STA thread.
        IReadOnlyList<InstalledApp> result = [];
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = EnumerateCore(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "NumDeck apps" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Não foi possível listar os aplicativos: " + error.Message, error);
        return result;
    }

    private static List<InstalledApp> EnumerateCore()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application indisponível.");
        dynamic shell = Activator.CreateInstance(type)!;
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        try
        {
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                try
                {
                    string name = item.Name ?? "";
                    string id = item.Path ?? "";
                    string target = "";
                    try { target = item.ExtendedProperty("System.Link.TargetParsingPath") as string ?? ""; } catch { }
                    if (Classify(name, id, target) is { } app) apps.TryAdd(app.LaunchPath, app);
                }
                catch { /* skip odd entries */ }
                finally { if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }

        return apps.Values
            .OrderBy(a => a.Name, StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, true))
            .ToList();
    }

    [GeneratedRegex(@"\b(uninstall|desinstal|remover|uninst)", RegexOptions.IgnoreCase)]
    private static partial Regex UninstallName();

    [GeneratedRegex(@"^unins\d*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex UninstallerFile();

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".htm", ".html", ".chm", ".url", ".txt", ".rtf", ".md", ".doc", ".docx", ".xml", ".ini", ".log",
    };

    /// <summary>Turns a Start menu entry into an app, or null for uninstallers, manuals, web links and folders.</summary>
    public static InstalledApp? Classify(string name, string id, string target)
    {
        name = name.Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(id)) return null;
        if (UninstallName().IsMatch(name)) return null;

        var file = string.IsNullOrWhiteSpace(target) ? id : target;
        if (file.Contains("://")) return null;
        string ext = "", fileName = "";
        try { ext = Path.GetExtension(file); fileName = Path.GetFileName(file); } catch { }
        if (DocumentExtensions.Contains(ext) || UninstallerFile().IsMatch(fileName)) return null;
        try { if (Path.IsPathFullyQualified(file) && Directory.Exists(file)) return null; } catch { }

        // Shared Windows programs (cmd.exe, powershell.exe, explorer.exe…) host many shortcuts:
        // they can't tell which window belongs to this app.
        var exe = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsWindowsProgram(target) ? target : "";
        return new InstalledApp(name, AppsFolderPrefix + id, exe);
    }

    private static bool IsWindowsProgram(string path)
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return win.Length > 0 && path.StartsWith(win.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
