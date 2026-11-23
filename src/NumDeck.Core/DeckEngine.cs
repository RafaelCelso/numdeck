using NumDeck.Core.Actions;
using NumDeck.Core.Models;

namespace NumDeck.Core;

/// <summary>Maps a deck key press to an action, honouring profiles and global keys.</summary>
public sealed class DeckEngine
{
    private readonly AppConfig _config;
    private readonly Func<ActionDefinition, Task> _run;

    public event Action<Profile>? ProfileChanged;

    public DeckEngine(AppConfig config, Func<ActionDefinition, Task> run)
    {
        _config = config;
        _run = run;
    }

    public Profile ActiveProfile =>
        _config.Profiles.FirstOrDefault(p => p.Id == _config.ActiveProfileId) ?? _config.Profiles[0];

    /// <summary>Profile keys win over global keys; keys without an action fall through.</summary>
    public KeyBinding? Resolve(KeyId key)
    {
        var b = ActiveProfile.Find(key);
        if (b?.Action is not null) return b;
        var g = _config.GlobalKeys.Find(key);
        return g?.Action is not null ? g : null;
    }

    /// <returns>The binding that ran, or null if the key is unassigned.</returns>
    public KeyBinding? HandleKeyDown(KeyId key)
    {
        var binding = Resolve(key);
        if (binding?.Action is null) return null;
        if (binding.Action is SwitchProfileAction s) SwitchProfile(s);
        else _ = _run(binding.Action);
        return binding;
    }

    public void SwitchProfile(SwitchProfileAction s)
    {
        var profiles = _config.Profiles;
        int index = profiles.IndexOf(ActiveProfile);
        Profile? target = s.Mode switch
        {
            ProfileSwitchMode.Next => profiles[(index + 1) % profiles.Count],
            ProfileSwitchMode.Previous => profiles[(index - 1 + profiles.Count) % profiles.Count],
            _ => profiles.FirstOrDefault(p => p.Id == s.ProfileId),
        };
        if (target is not null) SetActive(target);
    }

    public void SetActive(Profile profile)
    {
        if (_config.ActiveProfileId == profile.Id) return;
        _config.ActiveProfileId = profile.Id;
        ProfileChanged?.Invoke(profile);
    }
}
