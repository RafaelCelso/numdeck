using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NumDeck.App.ViewModels;
using NumDeck.Core.Actions;
using NumDeck.Core.Apps;
using NumDeck.Core.Browsers;
using NumDeck.Core.Macros;
using NumDeck.Core.Models;

namespace NumDeck.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        var version = typeof(MainWindow).Assembly.GetName().Version;
        if (version is not null)
        {
            Title = $"NumDeck {version.Major}.{version.Minor}.{version.Build}";
            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build} · Controlador de teclado numérico";
        }
    }

    private static T? ActionOf<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    private static string? PickFile(string filter, string? current)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true, DereferenceLinks = false };
        try
        {
            if (!string.IsNullOrWhiteSpace(current) && System.IO.Path.GetDirectoryName(current) is { } dir && System.IO.Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }
        catch { }
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        if (ActionOf<LaunchAppAction>(sender) is not { } a) return;
        var last = a.Apps.LastOrDefault(x => !InstalledApps.IsAppsFolderPath(x.Path))?.Path;
        if (PickFile("Aplicativos e atalhos|*.exe;*.lnk;*.bat;*.cmd;*.ps1;*.url|Todos os arquivos|*.*", last) is { } f)
            a.Apps.Add(new AppEntry { Path = f });
    }

    /// <summary>Start menu list: checked apps are added, unchecked ones removed (files added by hand stay).</summary>
    private void PickApps_Click(object sender, RoutedEventArgs e)
    {
        if (ActionOf<LaunchAppAction>(sender) is not { } a) return;
        var picker = new AppPickerWindow(a.Apps.Select(x => x.Path)) { Owner = this };
        if (picker.ShowDialog() != true) return;

        var chosen = picker.Checked.ToDictionary(x => x.LaunchPath, StringComparer.OrdinalIgnoreCase);
        foreach (var old in a.Apps.Where(x => InstalledApps.IsAppsFolderPath(x.Path) && !chosen.ContainsKey(x.Path)).ToList())
            a.Apps.Remove(old);
        foreach (var app in picker.Checked)
            if (!a.Apps.Any(x => string.Equals(x.Path, app.LaunchPath, StringComparison.OrdinalIgnoreCase)))
                a.Apps.Add(new AppEntry { Name = app.Name, Path = app.LaunchPath, ExePath = app.ExePath });
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppEntry entry) return;
        // The button sits inside the list, whose DataContext chain leads back to the action.
        var list = FindParent<ListBox>(sender as DependencyObject);
        (list?.DataContext as LaunchAppAction)?.Apps.Remove(entry);
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T)
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    // ---------------------------------------------------------------- profile rename

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<TextBox>(e.OriginalSource as DependencyObject) is not null) return; // double-click inside the name box
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not null && Vm.RenameProfileCommand.CanExecute(null))
            Vm.RenameProfileCommand.Execute(null);
    }

    private void ProfileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Vm.RenameProfileCommand.CanExecute(null)) { Vm.RenameProfileCommand.Execute(null); e.Handled = true; }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
            Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            Vm.EndRename(commit: e.Key == Key.Enter);
            e.Handled = true;
            ProfileList.Focus();
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Vm.EndRename(commit: true);

    /// <summary>The menu is for the profile under the mouse; none on empty space.</summary>
    private void ProfileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = ProfileList.ContextMenu;
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not Profile p
            || FindParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        Vm.EditingProfile = p;
        bool normal = p.Id != "global";
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsEnabled = (item.Tag as string) switch
            {
                "rename" => normal,
                "activate" => normal && p.Id != Vm.Config.ActiveProfileId,
                "delete" => Vm.RemoveProfileCommand.CanExecute(null),
                _ => true,
            };
    }

    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as MenuItem)?.Tag as string)
        {
            case "rename": Vm.RenameProfileCommand.Execute(null); break;
            case "activate": Vm.ActivateProfileCommand.Execute(null); break;
            case "delete": Vm.RemoveProfileCommand.Execute(null); break;
        }
    }

    // ---------------------------------------------------------------- profile reordering

    private Point _profileDragStart;
    private Profile? _profileDragCandidate;
    private InsertionAdorner? _insertionAdorner;

    private void ProfileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        _profileDragCandidate = FindParent<TextBox>(source) is null
            ? FindParent<ListBoxItem>(source)?.DataContext as Profile
            : null;
        _profileDragStart = e.GetPosition(ProfileList);
    }

    private void ProfileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_profileDragCandidate is not { } p || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(ProfileList) - _profileDragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _profileDragCandidate = null;
        if (!Vm.Config.Profiles.Contains(p)) return; // "Teclas globais" stays put

        Mouse.Capture(null);
        try { DragDrop.DoDragDrop(ProfileList, new DataObject(typeof(Profile), p), DragDropEffects.Move); }
        finally { ShowInsertion(null, false); }
    }

    /// <summary>Where the dragged profile would go: Config.Profiles index to insert before, plus the item and edge to mark.</summary>
    private (int insertBefore, ListBoxItem? item, bool after)? DropPosition(DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Profile)) is not Profile) return null;
        var profiles = Vm.Config.Profiles;
        var item = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not Profile target || !profiles.Contains(target))
        {
            // Empty space or "Teclas globais": at the end of the normal profiles.
            var last = profiles.Count > 0 ? ProfileList.ItemContainerGenerator.ContainerFromItem(profiles[^1]) as ListBoxItem : null;
            return (profiles.Count, last, true);
        }
        bool after = e.GetPosition(item).Y > item.ActualHeight / 2;
        return (profiles.IndexOf(target) + (after ? 1 : 0), item, after);
    }

    private void ProfileList_DragOver(object sender, DragEventArgs e)
    {
        var pos = DropPosition(e);
        e.Effects = pos is null ? DragDropEffects.None : DragDropEffects.Move;
        ShowInsertion(pos?.item, pos?.after ?? false);
        e.Handled = true;
    }

    private void ProfileList_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between items; only clear when the mouse really left the list.
        var p = e.GetPosition(ProfileList);
        if (p.X < 0 || p.Y < 0 || p.X > ProfileList.ActualWidth || p.Y > ProfileList.ActualHeight) ShowInsertion(null, false);
    }

    private void ProfileList_Drop(object sender, DragEventArgs e)
    {
        var pos = DropPosition(e);
        ShowInsertion(null, false);
        if (pos is { } where && e.Data.GetData(typeof(Profile)) is Profile p) Vm.MoveProfileTo(p, where.insertBefore);
        e.Handled = true;
    }

    private void ShowInsertion(ListBoxItem? item, bool after)
    {
        if (_insertionAdorner is { } old)
        {
            if (item is not null && ReferenceEquals(old.AdornedElement, item)) { old.After = after; return; }
            System.Windows.Documents.AdornerLayer.GetAdornerLayer(old.AdornedElement)?.Remove(old);
            _insertionAdorner = null;
        }
        if (item is null || System.Windows.Documents.AdornerLayer.GetAdornerLayer(item) is not { } layer) return;
        _insertionAdorner = new InsertionAdorner(item, (System.Windows.Media.Brush)FindResource("AccentFillColorDefaultBrush")) { After = after, IsHitTestVisible = false };
        layer.Add(_insertionAdorner);
    }

    /// <summary>A line on the top or bottom edge of a list item: "drops here".</summary>
    private sealed class InsertionAdorner(UIElement element, System.Windows.Media.Brush brush) : System.Windows.Documents.Adorner(element)
    {
        private bool _after;
        public bool After { get => _after; set { _after = value; InvalidateVisual(); } }

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            double y = After ? size.Height - 1 : 1;
            dc.DrawRoundedRectangle(brush, null, new Rect(2, y - 1.5, Math.Max(0, size.Width - 4), 3), 1.5, 1.5);
        }
    }

    // ---------------------------------------------------------------- deck drag and drop

    private Point _dragStart;
    private KeyCellViewModel? _dragCandidate;
    private KeyCellViewModel? _dropTarget;

    private static KeyCellViewModel? CellAt(object? source) =>
        FindParent<Button>(source as DependencyObject)?.DataContext as KeyCellViewModel;

    private void Deck_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = CellAt(e.OriginalSource);
        _dragStart = e.GetPosition(this);
    }

    private void Deck_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { } cell || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragCandidate = null;
        if (cell.Binding is null) return; // nothing to move

        if (FindParent<Button>(e.OriginalSource as DependencyObject) is { } button) button.ReleaseMouseCapture();
        cell.IsDragSource = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(KeyCellViewModel), cell), DragDropEffects.Move); }
        finally
        {
            cell.IsDragSource = false;
            SetDropTarget(null);
        }
    }

    private void SetDropTarget(KeyCellViewModel? cell)
    {
        if (ReferenceEquals(_dropTarget, cell)) return;
        if (_dropTarget is not null) _dropTarget.IsDropTarget = false;
        _dropTarget = cell;
        if (cell is not null) cell.IsDropTarget = true;
    }

    private void Deck_DragOver(object sender, DragEventArgs e)
    {
        var source = e.Data.GetData(typeof(KeyCellViewModel)) as KeyCellViewModel;
        var target = CellAt(e.OriginalSource);
        bool ok = source is not null && target is not null && !ReferenceEquals(source, target);
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(ok ? target : null);
        e.Handled = true;
    }

    private void Deck_DragLeave(object sender, DragEventArgs e) => SetDropTarget(null);

    private void Deck_Drop(object sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (e.Data.GetData(typeof(KeyCellViewModel)) is KeyCellViewModel source && CellAt(e.OriginalSource) is { } target)
            Vm.SwapKeys(source, target);
        e.Handled = true;
    }

    private void BrowseBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (ActionOf<OpenBrowserAction>(sender) is not { } a) return;
        if (PickFile("Navegador|*.exe", a.CustomPath) is { } f)
        {
            a.CustomPath = f;
            a.BrowserName = System.IO.Path.GetFileNameWithoutExtension(f);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ActionOf<OpenFolderAction>(sender) is not { } a) return;
        var dlg = new OpenFolderDialog { Title = "Escolha a pasta que a tecla vai abrir" };
        try
        {
            var current = Environment.ExpandEnvironmentVariables(a.FolderPath.Trim().Trim('"'));
            if (current.Length > 0 && System.IO.Directory.Exists(current)) dlg.InitialDirectory = current;
        }
        catch { }
        if (dlg.ShowDialog(this) == true) a.FolderPath = dlg.FolderName;
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        if (ActionOf<PlaySoundAction>(sender) is not { } a) return;
        if (PickFile("Sons|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.flac|Todos os arquivos|*.*", a.FilePath) is { } f) a.FilePath = f;
    }

    private void Browser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionOf<OpenBrowserAction>(sender) is not { } a || (sender as ComboBox)?.SelectedItem is not BrowserInfo b) return;
        a.BrowserName = b.Id == OpenBrowserAction.CustomBrowserId
            ? (string.IsNullOrEmpty(a.CustomPath) ? "Navegador" : System.IO.Path.GetFileNameWithoutExtension(a.CustomPath))
            : b.Name;
    }

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionOf<SwitchProfileAction>(sender) is { } a && (sender as ComboBox)?.SelectedItem is Profile p)
            a.ProfileName = p.Name;
    }

    /// <summary>Records a key combination into the macro script.</summary>
    private void RecordShortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ActionOf<MacroAction>(sender) is not { } a) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return;

        e.Handled = true;
        var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var mods = Keyboard.Modifiers;
        var combo = KeyCombo.Format(
            mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Alt),
            Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin), vk);

        if (sender is TextBox box) box.Text = combo;
        var script = a.Script.TrimEnd();
        a.Script = script.Length == 0 ? combo : script + Environment.NewLine + combo;
    }
}
