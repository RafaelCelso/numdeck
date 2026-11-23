using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NumDeck.Core.Actions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LaunchAppAction), "launchApp")]
[JsonDerivedType(typeof(OpenBrowserAction), "openBrowser")]
[JsonDerivedType(typeof(OpenFolderAction), "openFolder")]
[JsonDerivedType(typeof(PlaySoundAction), "playSound")]
[JsonDerivedType(typeof(MacroAction), "macro")]
[JsonDerivedType(typeof(MediaAction), "media")]
[JsonDerivedType(typeof(SwitchProfileAction), "switchProfile")]
public abstract class ActionDefinition : ObservableObject
{
    /// <summary>Short text shown on the key in the grid.</summary>
    [JsonIgnore] public string Summary => BuildSummary();

    protected abstract string BuildSummary();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(Summary)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    protected static string FileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "(não definido)" : Path.GetFileNameWithoutExtension(path);
}

/// <summary>One application opened by a <see cref="LaunchAppAction"/>.</summary>
public sealed partial class AppEntry : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayName))] private string _name = "";
    /// <summary>What is opened: a file path, or <c>shell:AppsFolder\&lt;AppUserModelId&gt;</c> for apps from the Start menu.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DisplayName))] private string _path = "";
    [ObservableProperty] private string _arguments = "";
    [ObservableProperty] private string _workingDirectory = "";
    /// <summary>Program file used to find an already running instance (optional).</summary>
    [ObservableProperty] private string _exePath = "";

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name
        : string.IsNullOrWhiteSpace(Path) ? "(não definido)" : System.IO.Path.GetFileNameWithoutExtension(Path);
}

public sealed partial class LaunchAppAction : ActionDefinition
{
    private ObservableCollection<AppEntry> _apps = new();

    public LaunchAppAction() => _apps.CollectionChanged += Apps_CollectionChanged;

    public ObservableCollection<AppEntry> Apps
    {
        get => _apps;
        set
        {
            _apps.CollectionChanged -= Apps_CollectionChanged;
            foreach (var a in _apps) a.PropertyChanged -= App_PropertyChanged;
            _apps = value ?? new();
            _apps.CollectionChanged += Apps_CollectionChanged;
            foreach (var a in _apps) a.PropertyChanged += App_PropertyChanged;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private bool _bringToFrontIfRunning = true;

    // ---- Config from 1.0/1.1 (a single app): read and converted into Apps, never written. ----

    [JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPath
    {
        get => null;
        set { if (!string.IsNullOrWhiteSpace(value)) LegacyEntry().Path = value; }
    }

    [JsonPropertyName("arguments"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyArguments
    {
        get => null;
        set { if (!string.IsNullOrWhiteSpace(value)) LegacyEntry().Arguments = value; }
    }

    [JsonPropertyName("workingDirectory"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyWorkingDirectory
    {
        get => null;
        set { if (!string.IsNullOrWhiteSpace(value)) LegacyEntry().WorkingDirectory = value; }
    }

    private AppEntry LegacyEntry()
    {
        if (Apps.Count == 0) Apps.Add(new AppEntry());
        return Apps[0];
    }

    private void Apps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (AppEntry a in e.OldItems) a.PropertyChanged -= App_PropertyChanged;
        if (e.NewItems is not null) foreach (AppEntry a in e.NewItems) a.PropertyChanged += App_PropertyChanged;
        OnPropertyChanged(nameof(Apps));
    }

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Apps));

    protected override string BuildSummary() => Apps.Count switch
    {
        0 => "Abrir (não definido)",
        1 => "Abrir " + Apps[0].DisplayName,
        _ => $"{Apps[0].DisplayName} + {Apps.Count - 1}",
    };
}

public enum FolderLocation { Home, ThisPC, Desktop, Documents, Downloads, Pictures, Music, Videos, Custom }

/// <summary>Opens File Explorer, optionally at a given folder.</summary>
public sealed partial class OpenFolderAction : ActionDefinition
{
    [ObservableProperty] private FolderLocation _location = FolderLocation.Custom;
    /// <summary>Folder for <see cref="FolderLocation.Custom"/>; environment variables (%USERPROFILE%…) are allowed.</summary>
    [ObservableProperty] private string _folderPath = "";
    /// <summary>Bring an Explorer window already showing this folder to the front instead of opening another.</summary>
    [ObservableProperty] private bool _reuseOpenWindow = true;

    public static string DisplayName(FolderLocation l) => l switch
    {
        FolderLocation.Home => "Início do Explorador",
        FolderLocation.ThisPC => "Este Computador",
        FolderLocation.Desktop => "Área de Trabalho",
        FolderLocation.Documents => "Documentos",
        FolderLocation.Downloads => "Downloads",
        FolderLocation.Pictures => "Imagens",
        FolderLocation.Music => "Músicas",
        FolderLocation.Videos => "Vídeos",
        FolderLocation.Custom => "Pasta específica",
        _ => l.ToString(),
    };

    /// <summary>What to hand to explorer.exe ("" = its start page).</summary>
    [JsonIgnore]
    public string Target => Location switch
    {
        FolderLocation.Home => "",
        FolderLocation.ThisPC => "shell:MyComputerFolder",
        FolderLocation.Desktop => "shell:Desktop",
        FolderLocation.Documents => "shell:Personal",
        FolderLocation.Downloads => "shell:Downloads",
        FolderLocation.Pictures => "shell:My Pictures",
        FolderLocation.Music => "shell:My Music",
        FolderLocation.Videos => "shell:My Video",
        _ => Environment.ExpandEnvironmentVariables(FolderPath.Trim().Trim('"')),
    };

    protected override string BuildSummary() => Location == FolderLocation.Custom
        ? "📁 " + (string.IsNullOrWhiteSpace(FolderPath) ? "(não definido)" : FolderName(FolderPath))
        : "📁 " + DisplayName(Location);

    private static string FolderName(string path)
    {
        var p = path.Trim().Trim('"').TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(p);
        return string.IsNullOrEmpty(name) ? p + "\\" : name; // "C:" → "C:\"
    }
}

public sealed partial class OpenBrowserAction : ActionDefinition
{
    public const string CustomBrowserId = "custom";

    [ObservableProperty] private string _browserId = "";
    [ObservableProperty] private string _browserName = "";
    [ObservableProperty] private string _customPath = "";
    [ObservableProperty] private bool _newWindow = true;
    [ObservableProperty] private string _profileName = "";
    /// <summary>Open in an incognito / InPrivate / private window.</summary>
    [ObservableProperty] private bool _incognito;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlsText))]
    private List<string> _urls = new();

    /// <summary>One URL per line, for the editor.</summary>
    [JsonIgnore]
    public string UrlsText
    {
        get => string.Join(Environment.NewLine, Urls);
        set => Urls = value.Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
    }

    protected override string BuildSummary() =>
        $"{(string.IsNullOrEmpty(BrowserName) ? "Navegador" : BrowserName)} ({Urls.Count} aba{(Urls.Count == 1 ? "" : "s")}{(Incognito ? ", anônima" : "")})";
}

public sealed partial class PlaySoundAction : ActionDefinition
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private double _volume = 1.0;
    /// <summary>Core Audio endpoint id; null/empty = default output.</summary>
    [ObservableProperty] private string _outputDeviceId = "";
    [ObservableProperty] private bool _stopIfPlaying = true;

    protected override string BuildSummary() => "♪ " + FileName(FilePath);
}

/// <summary>
/// One step per line:
///   Ctrl+Shift+M        (key combination)
///   texto: Olá mundo    (types text)
///   espera: 200         (waits milliseconds)
/// </summary>
public sealed partial class MacroAction : ActionDefinition
{
    [ObservableProperty] private string _script = "";

    /// <summary>Parser errors for the editor ("" when the script is valid).</summary>
    [JsonIgnore]
    public string Validation
    {
        get
        {
            Macros.MacroParser.Parse(Script, out var errors);
            return string.Join(Environment.NewLine, errors);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Script)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Validation)));
    }

    protected override string BuildSummary()
    {
        var lines = Script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length switch { 0 => "Macro vazia", 1 => lines[0], _ => $"Macro ({lines.Length} passos)" };
    }
}

public enum MediaCommand
{
    PlayPause, NextTrack, PreviousTrack, Stop, VolumeUp, VolumeDown, VolumeMute, MicrophoneMute
}

public sealed partial class MediaAction : ActionDefinition
{
    [ObservableProperty] private MediaCommand _command = MediaCommand.PlayPause;

    public static string DisplayName(MediaCommand c) => c switch
    {
        MediaCommand.PlayPause => "Play / Pause",
        MediaCommand.NextTrack => "Próxima faixa",
        MediaCommand.PreviousTrack => "Faixa anterior",
        MediaCommand.Stop => "Parar",
        MediaCommand.VolumeUp => "Volume +",
        MediaCommand.VolumeDown => "Volume −",
        MediaCommand.VolumeMute => "Mudo (alto-falante)",
        MediaCommand.MicrophoneMute => "Mudo (microfone)",
        _ => c.ToString()
    };

    protected override string BuildSummary() => DisplayName(Command);
}

public enum ProfileSwitchMode { Specific, Next, Previous }

public sealed partial class SwitchProfileAction : ActionDefinition
{
    [ObservableProperty] private ProfileSwitchMode _mode = ProfileSwitchMode.Next;
    [ObservableProperty] private string? _profileId;
    [ObservableProperty] private string _profileName = "";

    protected override string BuildSummary() => Mode switch
    {
        ProfileSwitchMode.Next => "Próximo perfil",
        ProfileSwitchMode.Previous => "Perfil anterior",
        _ => "Perfil: " + (string.IsNullOrEmpty(ProfileName) ? "?" : ProfileName)
    };
}
