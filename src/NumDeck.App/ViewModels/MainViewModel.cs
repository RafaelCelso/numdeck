using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NumDeck.App.Services;
using NumDeck.Core;
using NumDeck.Core.Actions;
using NumDeck.Core.Browsers;
using NumDeck.Core.Config;
using NumDeck.Core.Execution;
using NumDeck.Core.Models;
using NumDeck.Input;

namespace NumDeck.App.ViewModels;

public sealed record ActionTypeOption(string Name, string ShortName, string Glyph, Type? Type, Func<ActionDefinition?> Create);
public sealed record Option<T>(T Value, string Name);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigStore _store = new();
    private readonly InputSink _sink = new();
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _deviceRefreshTimer;
    private readonly HashSet<KeyId> _pressed = new();
    private string _lastSavedJson = "";
    private DeckEngine _engine = null!;
    private ActionRunner _runner = null!;
    private bool _suppressDeviceSelection;

    /// <summary>(title, message) for tray notifications.</summary>
    public event Action<string, string>? Notify;

    public MainViewModel()
    {
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => SaveIfChanged(), _ui);
        _deviceRefreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            RefreshDevices();
        }, _ui);
        _deviceRefreshTimer.Stop();

        ActionTypes = new()
        {
            new("Nenhuma ação", "Nenhuma", Glyphs.None, null, () => null),
            new("Abrir aplicativo", "Aplicativo", Glyphs.App, typeof(LaunchAppAction), () => new LaunchAppAction()),
            new("Abrir navegador com abas", "Navegador", Glyphs.Browser, typeof(OpenBrowserAction), CreateBrowserAction),
            new("Abrir pasta (Explorador de Arquivos)", "Pasta", Glyphs.Folder, typeof(OpenFolderAction), () => new OpenFolderAction()),
            new("Tocar som", "Som", Glyphs.Sound, typeof(PlaySoundAction), () => new PlaySoundAction()),
            new("Macro / atalho de teclado", "Macro", Glyphs.Macro, typeof(MacroAction), () => new MacroAction()),
            new("Mídia / volume", "Mídia", Glyphs.Media, typeof(MediaAction), () => new MediaAction()),
            new("Trocar perfil", "Perfil", Glyphs.Profile, typeof(SwitchProfileAction), () => new SwitchProfileAction()),
        };
        FolderLocations = Enum.GetValues<FolderLocation>().OrderBy(l => l != FolderLocation.Custom)
            .Select(l => new Option<FolderLocation>(l, OpenFolderAction.DisplayName(l))).ToList();
        MediaOptions = Enum.GetValues<MediaCommand>().Select(c => new Option<MediaCommand>(c, MediaAction.DisplayName(c))).ToList();
        ProfileSwitchModes = new()
        {
            new(ProfileSwitchMode.Next, "Próximo perfil"),
            new(ProfileSwitchMode.Previous, "Perfil anterior"),
            new(ProfileSwitchMode.Specific, "Ir para um perfil específico"),
        };
    }

    // ---------------------------------------------------------------- state

    public AppConfig Config { get; private set; } = AppConfig.CreateDefault();
    public ObservableCollection<KeyboardDevice> Devices { get; } = new();
    public ObservableCollection<Profile> EditableProfiles { get; } = new();
    public ObservableCollection<KeyCellViewModel> Cells { get; } = new();
    public ObservableCollection<KeyCellViewModel> ExtraCells { get; } = new();
    public List<ActionTypeOption> ActionTypes { get; }
    public List<Option<MediaCommand>> MediaOptions { get; }
    public List<Option<FolderLocation>> FolderLocations { get; }
    public List<Option<ProfileSwitchMode>> ProfileSwitchModes { get; }
    public ObservableCollection<BrowserInfo> BrowserOptions { get; } = new();
    public List<AudioDeviceInfo> AudioOutputs { get; private set; } = new();

    [ObservableProperty] private KeyboardDevice? _selectedDevice;
    [ObservableProperty] private string _deviceStatus = "Nenhum teclado escolhido";
    [ObservableProperty] private bool _isDeckConnected;
    [ObservableProperty] private bool _isIdentifying;
    [ObservableProperty] private bool _isLearning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string? _hookStatus;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private Profile? _editingProfile;
    [ObservableProperty] private KeyCellViewModel? _selectedCell;
    [ObservableProperty] private string _activeProfileName = "";
    [ObservableProperty] private bool _startWithWindows;

    public bool IsElevated { get; } = StartupService.IsElevated();
    public string ConfigPath => _store.FilePath;
    public bool IsEditingGlobal => EditingProfile is not null && ReferenceEquals(EditingProfile, Config.GlobalKeys);
    public bool IsEditingActive => EditingProfile is not null && EditingProfile.Id == Config.ActiveProfileId;

    /// <summary>Actions replaced by changing a key's action type in this session, per profile/key/type.</summary>
    private readonly Dictionary<(Profile, KeyId, Type), ActionDefinition> _replacedActions = new();

    public ActionTypeOption? SelectedActionType
    {
        get
        {
            var t = SelectedCell?.Binding?.Action?.GetType();
            return ActionTypes.FirstOrDefault(o => o.Type == t);
        }
        set
        {
            if (value is null || SelectedCell is null || EditingProfile is null) return;
            var current = SelectedCell.Binding?.Action?.GetType();
            if (current == value.Type) return;

            // Remember the action being replaced, so picking its type again brings it back as it was
            // (a stray click on another action type must not lose the key's settings).
            if (SelectedCell.Binding?.Action is { } old)
                _replacedActions[(EditingProfile, SelectedCell.Key, old.GetType())] = old;
            var action = value.Type is { } t && _replacedActions.Remove((EditingProfile, SelectedCell.Key, t), out var kept)
                ? kept
                : value.Create();
            if (action is null)
            {
                if (SelectedCell.Binding is { } b) { b.Action = null; if (string.IsNullOrEmpty(b.Label)) RemoveBinding(b); }
            }
            else
            {
                var binding = SelectedCell.Binding ?? EditingProfile.GetOrAdd(SelectedCell.Key);
                binding.Action = action;
                SelectedCell.Binding = binding;
            }
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- startup

    public void Initialize()
    {
        Config = _store.Load();
        _lastSavedJson = ConfigStore.Serialize(Config);
        StartWithWindows = StartupService.IsEnabled();
        if (StartWithWindows) { try { StartupService.RepairPath(); } catch { } }
        Config.StartWithWindows = StartWithWindows;

        foreach (var b in BrowserCatalog.Detect()) BrowserOptions.Add(b);
        BrowserOptions.Add(new BrowserInfo(OpenBrowserAction.CustomBrowserId, "Outro navegador (escolher .exe)…", ""));
        AudioOutputs = AudioDevices.Outputs();

        _runner = new ActionRunner(() => BrowserOptions.ToList());
        _runner.Failed += (what, ex) => _ui.BeginInvoke(() => ReportError($"Falha em \"{what}\"", ex));
        _runner.Info += msg => _ui.BeginInvoke(() => { SetStatus(msg); Notify?.Invoke("NumDeck", msg); });
        _engine = new DeckEngine(Config, a => _runner.RunAsync(a));
        _engine.ProfileChanged += OnProfileChanged;

        RebuildEditableProfiles();
        EditingProfile = _engine.ActiveProfile;
        ActiveProfileName = _engine.ActiveProfile.Name;

        _sink.RawKey += e => _ui.BeginInvoke(() => OnRawKey(e));
        _sink.DevicesChanged += () => _ui.BeginInvoke(() => { _deviceRefreshTimer.Stop(); _deviceRefreshTimer.Start(); });
        _sink.BlockingEnabled = Config.BlockDeckKeys;
        _sink.Start();
        HookStatus = _sink.HookStatus;

        RefreshDevices();
        _saveTimer.Start();
        Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppConfig.BlockDeckKeys)) _sink.BlockingEnabled = Config.BlockDeckKeys;
            if (e.PropertyName == nameof(AppConfig.ActiveProfileId)) OnPropertyChanged(nameof(IsEditingActive));
        };
        SetStatus(SelectedDevice is null
            ? "Escolha o teclado do deck acima (ou clique em \"Identificar\" e aperte uma tecla nele)."
            : "Pronto.");
    }

    private OpenBrowserAction CreateBrowserAction()
    {
        var first = BrowserOptions.FirstOrDefault(b => b.Id != OpenBrowserAction.CustomBrowserId);
        return new OpenBrowserAction { BrowserId = first?.Id ?? OpenBrowserAction.CustomBrowserId, BrowserName = first?.Name ?? "" };
    }

    // ---------------------------------------------------------------- devices

    [RelayCommand]
    public void RefreshDevices()
    {
        var list = DeviceEnumerator.GetKeyboards()
            .OrderBy(d => d.Connection is ConnectionKind.Internal or ConnectionKind.Virtual)
            .ThenBy(d => d.Name).ToList();
        _suppressDeviceSelection = true;
        Devices.Clear();
        foreach (var d in list) Devices.Add(d);
        SelectedDevice = DeviceEnumerator.FindSelected(list, Config.Device);
        _suppressDeviceSelection = false;
        UpdateDeckDevice();
    }

    partial void OnSelectedDeviceChanged(KeyboardDevice? value)
    {
        if (_suppressDeviceSelection) return;
        Config.Device = value?.ToSelection();
        UpdateDeckDevice();
        if (value is not null) SetStatus($"Deck: {value.Name}. As teclas desse teclado agora disparam ações.");
    }

    private void UpdateDeckDevice()
    {
        _sink.DeckDevice = SelectedDevice?.Handle ?? 0;
        IsDeckConnected = SelectedDevice is not null;
        DeviceStatus = SelectedDevice is not null ? "Conectado"
            : Config.Device is not null ? $"Desconectado — conecte \"{Config.Device.DisplayName}\""
            : "Nenhum teclado escolhido";
    }

    [RelayCommand]
    private void Identify()
    {
        IsLearning = false;
        IsIdentifying = !IsIdentifying;
        if (IsIdentifying) SetStatus("Aperte qualquer tecla no teclado que você quer usar como deck…");
    }

    // ---------------------------------------------------------------- raw keys

    private void OnRawKey(RawKeyEvent e)
    {
        if (IsIdentifying)
        {
            if (e.IsUp || e.Device == 0) return;
            IsIdentifying = false;
            var dev = Devices.FirstOrDefault(d => d.Handle == e.Device);
            if (dev is null) { RefreshDevices(); dev = Devices.FirstOrDefault(d => d.Handle == e.Device); }
            if (dev is not null) SelectedDevice = dev;
            else SetStatus("Não consegui identificar esse teclado. Tente outra tecla.", true);
            return;
        }

        if (e.Device == 0 || e.Device != _sink.DeckDevice) return;

        if (e.IsUp) { _pressed.Remove(e.Key); return; }
        if (!_pressed.Add(e.Key)) return; // auto-repeat while held

        FlashKey(e.Key);

        if (IsLearning)
        {
            IsLearning = false;
            SelectedCell = FindOrCreateCell(e.Key);
            return;
        }
        if (IsPaused) return;

        var binding = _engine.HandleKeyDown(e.Key);
        if (binding?.Action is { } a && a is not SwitchProfileAction) SetStatus("▶ " + (binding.Label is { Length: > 0 } l ? l : a.Summary));
    }

    private async void FlashKey(KeyId key)
    {
        var cell = Cells.Concat(ExtraCells).FirstOrDefault(c => c.Key == key);
        if (cell is null) return;
        cell.IsPressed = true;
        await Task.Delay(180);
        cell.IsPressed = false;
    }

    [RelayCommand]
    private void Learn()
    {
        IsIdentifying = false;
        IsLearning = !IsLearning;
        if (IsLearning)
            SetStatus(SelectedDevice is null
                ? "Escolha primeiro o teclado do deck."
                : "Aperte no deck a tecla que você quer configurar…", SelectedDevice is null);
        if (SelectedDevice is null) IsLearning = false;
    }

    // ---------------------------------------------------------------- profiles

    private void RebuildEditableProfiles()
    {
        var keep = EditingProfile;
        EditableProfiles.Clear();
        foreach (var p in Config.Profiles) EditableProfiles.Add(p);
        EditableProfiles.Add(Config.GlobalKeys);
        if (keep is not null && EditableProfiles.Contains(keep)) EditingProfile = keep;
    }

    partial void OnEditingProfileChanging(Profile? value) => EndRename(commit: true);

    partial void OnEditingProfileChanged(Profile? value)
    {
        RenameProfileCommand.NotifyCanExecuteChanged();
        RebuildCells();
        OnPropertyChanged(nameof(IsEditingGlobal));
        OnPropertyChanged(nameof(IsEditingActive));
        RemoveProfileCommand.NotifyCanExecuteChanged();
        ActivateProfileCommand.NotifyCanExecuteChanged();
    }

    private void OnProfileChanged(Profile profile)
    {
        ActiveProfileName = profile.Name;
        // Follow the deck when the user was looking at the previous active profile.
        if (!IsEditingGlobal) EditingProfile = profile;
        OnPropertyChanged(nameof(IsEditingActive));
        if (Config.ShowProfileNotifications) Notify?.Invoke("Perfil ativo", profile.Name);
        SetStatus($"Perfil ativo: {profile.Name}");
    }

    [RelayCommand]
    private void AddProfile()
    {
        var p = new Profile { Name = $"Perfil {Config.Profiles.Count + 1}" };
        Config.Profiles.Add(p);
        RebuildEditableProfiles();
        EditingProfile = p;
    }

    private bool CanModifyProfile() => EditingProfile is not null && !IsEditingGlobal;

    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    private void RemoveProfile()
    {
        if (EditingProfile is not { } p || IsEditingGlobal || Config.Profiles.Count <= 1) return;
        if (MessageBox.Show($"Excluir o perfil \"{p.Name}\" e todas as suas teclas?", "NumDeck",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool wasActive = p.Id == Config.ActiveProfileId;
        Config.Profiles.Remove(p);
        if (wasActive) _engine.SetActive(Config.Profiles[0]);
        EditingProfile = Config.Profiles[0];
        RebuildEditableProfiles();
    }

    private bool CanRemoveProfile() => CanModifyProfile() && Config.Profiles.Count > 1;

    [RelayCommand(CanExecute = nameof(CanModifyProfile))]
    private void ActivateProfile()
    {
        if (EditingProfile is { } p && !IsEditingGlobal) _engine.SetActive(p);
    }

    /// <summary>Profile whose name is being edited in the list (null = none).</summary>
    [ObservableProperty] private Profile? _renamingProfile;
    private string _nameBeforeRename = "";

    [RelayCommand(CanExecute = nameof(CanModifyProfile))]
    private void RenameProfile()
    {
        if (EditingProfile is not { } p || IsEditingGlobal) return;
        EndRename(commit: true);
        _nameBeforeRename = p.Name;
        RenamingProfile = p;
    }

    /// <summary>Finishes the in-place rename; an empty name or Esc keeps the old one.</summary>
    public void EndRename(bool commit)
    {
        if (RenamingProfile is not { } p) return;
        RenamingProfile = null;
        var name = (p.Name ?? "").Trim();
        if (!commit || name.Length == 0) { p.Name = _nameBeforeRename; return; }
        p.Name = name;
        if (name == _nameBeforeRename) return;

        if (p.Id == Config.ActiveProfileId) ActiveProfileName = name;
        // "Trocar perfil" keys show the profile name.
        foreach (var b in Config.Profiles.Append(Config.GlobalKeys).SelectMany(x => x.Bindings))
            if (b.Action is SwitchProfileAction s && s.ProfileId == p.Id) s.ProfileName = name;
        SetStatus($"Perfil renomeado: \"{_nameBeforeRename}\" → \"{name}\"");
    }

    /// <summary>Drag and drop in the profile list: puts <paramref name="p"/> before the profile at <paramref name="insertBefore"/>
    /// (Config.Profiles index; Count = at the end). "Teclas globais" always stays last.</summary>
    public void MoveProfileTo(Profile p, int insertBefore)
    {
        int from = Config.Profiles.IndexOf(p);
        if (from < 0) return;
        int to = Math.Clamp(insertBefore > from ? insertBefore - 1 : insertBefore, 0, Config.Profiles.Count - 1);
        if (to == from) return;
        Config.Profiles.Move(from, to);
        RebuildEditableProfiles();
        EditingProfile = p;
        SetStatus($"Perfil \"{p.Name}\" movido para a posição {to + 1}.");
    }

    // ---------------------------------------------------------------- key grid

    private void RebuildCells()
    {
        var selectedKey = SelectedCell?.Key;
        Cells.Clear();
        ExtraCells.Clear();
        if (EditingProfile is null) { SelectedCell = null; return; }

        foreach (var k in NumpadLayout.Keys)
            Cells.Add(new KeyCellViewModel(k.Key, k.Label, k.Row, k.Column, k.RowSpan, k.ColumnSpan)
            {
                Binding = EditingProfile.Find(k.Key),
                GlobalBinding = IsEditingGlobal ? null : Config.GlobalKeys.Find(k.Key),
            });
        foreach (var b in EditingProfile.Bindings.Where(b => !NumpadLayout.IsStandard(b.Key)))
            ExtraCells.Add(new KeyCellViewModel(b.Key, DeviceEnumerator.KeyName(b.Key)) { Binding = b });

        SelectedCell = selectedKey is { } key ? Cells.Concat(ExtraCells).FirstOrDefault(c => c.Key == key) : null;
    }

    private KeyCellViewModel FindOrCreateCell(KeyId key)
    {
        var cell = Cells.Concat(ExtraCells).FirstOrDefault(c => c.Key == key);
        if (cell is null)
        {
            cell = new KeyCellViewModel(key, DeviceEnumerator.KeyName(key)) { Binding = EditingProfile?.Find(key) };
            ExtraCells.Add(cell);
        }
        return cell;
    }

    [RelayCommand]
    private void SelectCell(KeyCellViewModel cell) => SelectedCell = cell;

    /// <summary>Drag and drop on the deck: the two keys exchange their action and label.</summary>
    public void SwapKeys(KeyCellViewModel from, KeyCellViewModel to)
    {
        if (EditingProfile is not { } p || ReferenceEquals(from, to)) return;
        if (!p.SwapKeys(from.Key, to.Key)) return;

        (from.Binding, to.Binding) = (to.Binding, from.Binding);
        // Extra keys are listed only while they have something.
        if (from.Binding is null && !NumpadLayout.IsStandard(from.Key)) ExtraCells.Remove(from);
        SelectedCell = to;
        OnPropertyChanged(nameof(SelectedActionType));
        OnPropertyChanged(nameof(SelectedLabel));
        SetStatus(from.Binding is null
            ? $"Ação movida da tecla {from.KeyLabel} para a {to.KeyLabel}."
            : $"Teclas {from.KeyLabel} e {to.KeyLabel} trocadas.");
    }

    partial void OnSelectedCellChanged(KeyCellViewModel? oldValue, KeyCellViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(SelectedActionType));
        OnPropertyChanged(nameof(SelectedLabel));
    }

    /// <summary>Custom key caption (empty = show the action summary).</summary>
    public string SelectedLabel
    {
        get => SelectedCell?.Binding?.Label ?? "";
        set
        {
            if (SelectedCell is null || EditingProfile is null) return;
            var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SelectedCell.Binding is null && text is null) return;
            var b = SelectedCell.Binding ?? EditingProfile.GetOrAdd(SelectedCell.Key);
            b.Label = text;
            SelectedCell.Binding = b;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ClearKey()
    {
        if (SelectedCell?.Binding is not { } b) return;
        RemoveBinding(b);
        OnPropertyChanged(nameof(SelectedActionType));
        OnPropertyChanged(nameof(SelectedLabel));
    }

    private void RemoveBinding(KeyBinding b)
    {
        EditingProfile?.Bindings.Remove(b);
        if (SelectedCell is { } cell && ReferenceEquals(cell.Binding, b))
        {
            cell.Binding = null;
            if (!NumpadLayout.IsStandard(cell.Key)) { ExtraCells.Remove(cell); SelectedCell = null; }
        }
    }

    [RelayCommand]
    private void TestAction()
    {
        if (SelectedCell?.Binding?.Action is not { } a) return;
        if (a is SwitchProfileAction s) _engine.SwitchProfile(s);
        else _ = _runner.RunAsync(a);
        SetStatus("▶ Teste: " + a.Summary);
    }

    [RelayCommand]
    private void StopSounds() => _runner.StopSounds();

    // ---------------------------------------------------------------- settings

    partial void OnStartWithWindowsChanged(bool value)
    {
        try
        {
            if (StartupService.IsEnabled() != value) StartupService.Set(value);
            Config.StartWithWindows = value;
        }
        catch (Exception ex) { ReportError("Não foi possível alterar a inicialização com o Windows", ex); }
    }

    [RelayCommand]
    private void RestartElevated()
    {
        SaveIfChanged(force: true);
        if (StartupService.RestartElevated()) ((App)Application.Current).ExitApp();
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        SaveIfChanged(force: true);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ConfigPath}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- persistence & status

    public void SaveIfChanged(bool force = false)
    {
        try
        {
            var json = ConfigStore.Serialize(Config);
            if (!force && json == _lastSavedJson) return;
            _store.Save(Config);
            _lastSavedJson = json;
        }
        catch (Exception ex) { ReportError("Não foi possível salvar a configuração", ex); }
    }

    public void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    public void ReportError(string what, Exception ex)
    {
        SetStatus($"{what}: {ex.Message}", true);
        Notify?.Invoke("NumDeck — erro", $"{what}: {ex.Message}");
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        SaveIfChanged();
        _sink.Dispose();
        _runner?.Dispose();
    }
}
