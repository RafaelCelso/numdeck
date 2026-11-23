using NumDeck.Core.Actions;

namespace NumDeck.App.ViewModels;

/// <summary>Segoe Fluent Icons / MDL2 glyphs used across the UI.</summary>
public static class Glyphs
{
    public const string None = "";      // Cancel
    public const string App = "";       // AllApps
    public const string Browser = "";   // Globe
    public const string Folder = "";    // Folder
    public const string Sound = "";     // Audio
    public const string Macro = "";     // KeyboardClassic
    public const string Media = "";     // Play
    public const string Profile = "";   // Switch

    public static string For(ActionDefinition? action) => action switch
    {
        null => "",
        LaunchAppAction => App,
        OpenBrowserAction => Browser,
        OpenFolderAction => Folder,
        PlaySoundAction => Sound,
        MacroAction => Macro,
        MediaAction m => m.Command switch
        {
            MediaCommand.NextTrack => "",
            MediaCommand.PreviousTrack => "",
            MediaCommand.Stop => "",
            MediaCommand.VolumeUp or MediaCommand.VolumeDown => "",
            MediaCommand.VolumeMute => "",
            MediaCommand.MicrophoneMute => "",
            _ => Media,
        },
        SwitchProfileAction => Profile,
        _ => "",
    };
}
