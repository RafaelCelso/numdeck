using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using NumDeck.Core.Actions;

namespace NumDeck.Core.Models;

public sealed partial class KeyBinding : ObservableObject
{
    [ObservableProperty] private ushort _scanCode;
    [ObservableProperty] private bool _extended;
    [ObservableProperty] private string? _label;
    [ObservableProperty] private ActionDefinition? _action;

    [JsonIgnore] public KeyId Key => new(ScanCode, Extended);

    public static KeyBinding For(KeyId key) => new() { ScanCode = key.ScanCode, Extended = key.Extended };
}

public sealed partial class Profile : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "Perfil";
    public ObservableCollection<KeyBinding> Bindings { get; set; } = new();

    public KeyBinding? Find(KeyId key) => Bindings.FirstOrDefault(b => b.Key == key);

    public KeyBinding GetOrAdd(KeyId key)
    {
        var b = Find(key);
        if (b is null) { b = KeyBinding.For(key); Bindings.Add(b); }
        return b;
    }

    /// <summary>Exchanges what two keys do (action and label). False when neither key has anything.</summary>
    public bool SwapKeys(KeyId a, KeyId b)
    {
        if (a == b) return false;
        var ba = Find(a);
        var bb = Find(b);
        if (ba is null && bb is null) return false;
        if (ba is not null) { ba.ScanCode = b.ScanCode; ba.Extended = b.Extended; }
        if (bb is not null) { bb.ScanCode = a.ScanCode; bb.Extended = a.Extended; }
        return true;
    }
}

/// <summary>Which keyboard is the deck. Path is exact; VID/PID is the fallback after reconnects.</summary>
public sealed class DeviceSelection
{
    public string DevicePath { get; set; } = "";
    public string? VendorId { get; set; }
    public string? ProductId { get; set; }
    public string? Interface { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed partial class AppConfig : ObservableObject
{
    public DeviceSelection? Device { get; set; }
    public ObservableCollection<Profile> Profiles { get; set; } = new();
    /// <summary>Keys that work the same in every profile (e.g. profile switching).</summary>
    public Profile GlobalKeys { get; set; } = new() { Id = "global", Name = "Teclas globais" };
    [ObservableProperty] private string? _activeProfileId;
    [ObservableProperty] private bool _blockDeckKeys = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _showProfileNotifications = true;

    public static AppConfig CreateDefault()
    {
        var p = new Profile { Name = "Principal" };
        var cfg = new AppConfig();
        cfg.Profiles.Add(p);
        cfg.ActiveProfileId = p.Id;
        return cfg;
    }
}
