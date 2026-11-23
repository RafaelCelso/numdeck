using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NumDeck.App.Services;
using NumDeck.Core.Apps;

namespace NumDeck.App;

/// <summary>Start menu apps with check boxes: pick several at once.</summary>
public partial class AppPickerWindow : Window
{
    private List<PickerItem> _items = [];
    private ICollectionView? _view;
    private readonly HashSet<string> _initiallyChecked;
    private volatile bool _closed;

    /// <summary>The apps checked when the user pressed "Adicionar".</summary>
    public IReadOnlyList<InstalledApp> Checked { get; private set; } = [];

    public AppPickerWindow(IEnumerable<string> checkedLaunchPaths)
    {
        InitializeComponent();
        _initiallyChecked = new HashSet<string>(checkedLaunchPaths, StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _closed = true;
        UpdateCount();
    }

    private async Task LoadAsync()
    {
        try
        {
            var apps = await Task.Run(InstalledApps.Enumerate);
            _items = apps.Select(a => new PickerItem(a) { IsChecked = _initiallyChecked.Contains(a.LaunchPath) }).ToList();
            foreach (var i in _items) i.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PickerItem.IsChecked)) UpdateCount(); };
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = Matches;
            AppList.ItemsSource = _view;
            LoadingText.Visibility = Visibility.Collapsed;
            UpdateCount();
            LoadIconsInBackground(_items);
        }
        catch (Exception ex)
        {
            LoadingText.Text = ex.Message;
        }
    }

    /// <summary>Icons come from the shell on a separate STA thread so the list shows at once.</summary>
    private void LoadIconsInBackground(List<PickerItem> items)
    {
        // Already cached icons first (no waiting on reopen).
        foreach (var i in items)
            if (ShellIcons.TryGetCached(i.App.LaunchPath, 32, out var icon)) i.Icon = icon;

        var thread = new Thread(() =>
        {
            foreach (var item in items)
            {
                if (_closed) return;
                if (item.Icon is not null) continue;
                var icon = ShellIcons.Get(item.App.LaunchPath, 32);
                if (icon is not null) Dispatcher.BeginInvoke(() => item.Icon = icon);
            }
        }) { IsBackground = true, Name = "NumDeck icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private bool Matches(object o)
    {
        var q = SearchBox.Text.Trim();
        return q.Length == 0 || ((PickerItem)o).App.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || RemoveAccents(((PickerItem)o).App.Name).Contains(RemoveAccents(q), StringComparison.CurrentCultureIgnoreCase);
    }

    private static string RemoveAccents(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
            != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        if (AppList.Items.Count > 0 && AppList.SelectedIndex < 0) AppList.SelectedIndex = 0;
    }

    private void UpdateCount()
    {
        var n = _items.Count(i => i.IsChecked);
        CountText.Text = n switch { 0 => "Nenhum marcado", 1 => "1 marcado", _ => $"{n} marcados" };
        OkButton.Content = n == 0 && _initiallyChecked.Count == 0 ? "Adicionar" : "Confirmar";
    }

    private void Toggle(object? item)
    {
        if (item is PickerItem p) p.IsChecked = !p.IsChecked;
    }

    /// <summary>A click anywhere on the row checks/unchecks it (the check box handles its own clicks).</summary>
    private void AppList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d) return;
        for (var p = d; p is not null && p != AppList; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is CheckBox or System.Windows.Controls.Primitives.ScrollBar) return;
        if (ItemsControl.ContainerFromElement(AppList, d) is ListBoxItem li) Toggle(li.DataContext);
    }

    private void AppList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { Toggle(AppList.SelectedItem); e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Arrow keys in the search box move through the list.
        if (e.OriginalSource == SearchBox && e.Key is Key.Down or Key.Up && AppList.Items.Count > 0)
        {
            var i = Math.Clamp(AppList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, AppList.Items.Count - 1);
            AppList.SelectedIndex = i;
            AppList.ScrollIntoView(AppList.SelectedItem);
            e.Handled = true;
        }
        else if (e.OriginalSource == SearchBox && e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Toggle(AppList.SelectedItem);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Checked = _items.Where(i => i.IsChecked).Select(i => i.App).ToList();
        DialogResult = true;
    }

    public sealed partial class PickerItem(InstalledApp app) : ObservableObject
    {
        public InstalledApp App { get; } = app;
        public string Name => App.Name;
        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private ImageSource? _icon;
    }
}
