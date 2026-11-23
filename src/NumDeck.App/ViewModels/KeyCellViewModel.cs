using CommunityToolkit.Mvvm.ComponentModel;
using NumDeck.Core.Models;

namespace NumDeck.App.ViewModels;

/// <summary>One key on the on-screen deck.</summary>
public sealed partial class KeyCellViewModel : ObservableObject
{
    public KeyCellViewModel(KeyId key, string keyLabel, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    {
        Key = key; KeyLabel = keyLabel; Row = row; Column = column; RowSpan = rowSpan; ColumnSpan = columnSpan;
    }

    public KeyId Key { get; }
    public string KeyLabel { get; }
    public int Row { get; }
    public int Column { get; }
    public int RowSpan { get; }
    public int ColumnSpan { get; }

    /// <summary>Binding in the profile being edited (null = no action yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGlobal))]
    private KeyBinding? _binding;

    /// <summary>Global binding for this key (only set when editing a normal profile).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGlobal))]
    private KeyBinding? _globalBinding;

    /// <summary>True when the key has no action here, so the global one applies.</summary>
    public bool ShowsGlobal => Binding?.Action is null && GlobalBinding?.Action is not null;

    partial void OnBindingChanged(KeyBinding? oldValue, KeyBinding? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnBindingPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnBindingPropertyChanged;
    }

    private void OnBindingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyBinding.Action)) OnPropertyChanged(nameof(ShowsGlobal));
    }
    [ObservableProperty] private bool _isSelected;
    /// <summary>Briefly true when the physical key is pressed.</summary>
    [ObservableProperty] private bool _isPressed;
    /// <summary>A dragged key is over this one.</summary>
    [ObservableProperty] private bool _isDropTarget;
    /// <summary>This key is being dragged.</summary>
    [ObservableProperty] private bool _isDragSource;
}
