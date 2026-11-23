using System.Diagnostics;
using Microsoft.Win32;
using NumDeck.Core.Actions;

namespace NumDeck.Core.Browsers;

public enum BrowserFamily { Chromium, Firefox }

public sealed record BrowserInfo(string Id, string Name, string ExePath)
{
    public BrowserFamily Family => BrowserCommand.DetectFamily(ExePath);
    public override string ToString() => Name;
}

/// <summary>Finds installed browsers via the StartMenuInternet registry keys (+ known paths).</summary>
public static class BrowserCatalog
{
    public static List<BrowserInfo> Detect()
    {
        var found = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path) in new[]
        {
            (Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet"),
            (Registry.LocalMachine, @"SOFTWARE\Clients\StartMenuInternet"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"),
        })
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(sub);
                using var cmdKey = key?.OpenSubKey(@"shell\open\command");
                var exe = ParseExe(cmdKey?.GetValue(null) as string);
                if (exe is null || !File.Exists(exe)) continue;
                // Internet Explorer is retired: iexplore.exe just redirects to Edge.
                if (Path.GetFileName(exe).Equals("iexplore.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var name = key!.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
                // Same exe may be registered under several hives: keep the first one.
                if (found.Values.Any(b => string.Equals(b.ExePath, exe, StringComparison.OrdinalIgnoreCase))) continue;
                found.TryAdd(sub, new BrowserInfo(sub, name!, exe));
            }
        }

        // Browsers that don't always register themselves.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (id, name, exe) in new[]
        {
            ("Comet", "Comet", Path.Combine(local, "Perplexity", "Comet", "Application", "comet.exe")),
            ("Chrome-User", "Google Chrome", Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")),
        })
        {
            if (File.Exists(exe) && !found.Values.Any(b => string.Equals(b.ExePath, exe, StringComparison.OrdinalIgnoreCase)))
                found.TryAdd(id, new BrowserInfo(id, name, exe));
        }

        return found.Values.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static string? ParseExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? command[..(exeEnd + 4)] : command;
    }
}

public static class BrowserCommand
{
    private static readonly string[] FirefoxExes = { "firefox", "librewolf", "waterfox", "floorp", "zen" };

    public static BrowserFamily DetectFamily(string exePath) =>
        FirefoxExes.Contains(Path.GetFileNameWithoutExtension(exePath), StringComparer.OrdinalIgnoreCase)
            ? BrowserFamily.Firefox
            : BrowserFamily.Chromium;

    /// <summary>Adds https:// when the user typed just "github.com".</summary>
    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return url;
        if (url.Contains("://") || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return url;
        return "https://" + url;
    }

    /// <summary>
    /// Chromium switch for an incognito/private window; each browser names it differently.
    /// </summary>
    public static string ChromiumIncognitoSwitch(string exePath) =>
        Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant() switch
        {
            "msedge" => "--inprivate",
            "opera" or "launcher" or "opera_gx" => "--private",
            _ => "--incognito", // Chrome, Comet, Brave, Vivaldi, Chromium…
        };

    /// <summary>Command-line arguments that open every URL as a tab.</summary>
    /// <param name="incognitoSwitch">Chromium only: the browser's incognito switch (see <see cref="ChromiumIncognitoSwitch"/>).</param>
    public static List<string> BuildArguments(BrowserFamily family, IEnumerable<string> urls, bool newWindow, string? profile,
        bool incognito = false, string incognitoSwitch = "--incognito")
    {
        var list = urls.Select(NormalizeUrl).Where(u => u.Length > 0).ToList();
        var args = new List<string>();
        if (family == BrowserFamily.Firefox)
        {
            if (!string.IsNullOrWhiteSpace(profile)) { args.Add("-P"); args.Add(profile.Trim()); }
            if (incognito)
            {
                // Firefox opens each -private-window URL as a tab of the private window.
                foreach (var url in list) { args.Add("-private-window"); args.Add(url); }
                if (list.Count == 0) args.Add("-private-window");
                return args;
            }
            for (int i = 0; i < list.Count; i++)
            {
                args.Add(i == 0 && newWindow ? "-new-window" : "-new-tab");
                args.Add(list[i]);
            }
            if (list.Count == 0 && newWindow) args.Add("-new-window");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(profile)) args.Add($"--profile-directory={profile.Trim()}");
            if (incognito) args.Add(incognitoSwitch);
            if (newWindow) args.Add("--new-window");
            args.AddRange(list);
        }
        return args;
    }

    public static List<string> BuildArguments(string exePath, OpenBrowserAction action) =>
        BuildArguments(DetectFamily(exePath), action.Urls, action.NewWindow, action.ProfileName,
            action.Incognito, ChromiumIncognitoSwitch(exePath));

    public static ProcessStartInfo BuildStartInfo(string exePath, OpenBrowserAction action)
    {
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        foreach (var a in BuildArguments(exePath, action)) psi.ArgumentList.Add(a);
        return psi;
    }
}
