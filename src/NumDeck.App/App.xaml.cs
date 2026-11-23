using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using NumDeck.App.ViewModels;

namespace NumDeck.App;

public partial class App : Application
{
    private const string MutexName = "NumDeck.SingleInstance";
    private const string ShowEventName = "NumDeck.ShowWindow";
    private const string ExitEventName = "NumDeck.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private TaskbarIcon? _tray;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private bool _exiting;

    /// <summary>Hand cursor on everything clickable (buttons, check boxes, lists, menus, sliders, scroll bars).
    /// Done through property metadata so the Fluent theme styles stay untouched; must run before any control exists.</summary>
    static App()
    {
        foreach (var type in new[]
                 {
                     typeof(System.Windows.Controls.Primitives.ButtonBase), // Button, ToggleButton, CheckBox, RadioButton, RepeatButton
                     typeof(ComboBox), typeof(ComboBoxItem), typeof(ListBoxItem), typeof(MenuItem),
                     typeof(Slider), typeof(System.Windows.Controls.Primitives.Thumb),
                 })
        {
            try { FrameworkElement.CursorProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(System.Windows.Input.Cursors.Hand)); }
            catch (ArgumentException) { /* already overridden for this type */ }
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        // "--wait-for <pid>": restarted (e.g. elevated) copy waits for the old one to exit.
        int waitIndex = Array.IndexOf(args, "--wait-for");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out var pid))
        {
            try { using var old = Process.GetProcessById(pid); old.WaitForExit(10000); } catch { }
        }

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show its window.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        // The installer/uninstaller signals this event to close NumDeck cleanly.
        _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
        new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _exitEvent };
            while (true)
            {
                int which = WaitHandle.WaitAny(handles);
                if (_exiting) return;
                if (which == 1) { Dispatcher.BeginInvoke(ExitApp); return; }
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            _vm?.ReportError("Erro inesperado", ex.Exception);
        };

        _vm = new MainViewModel();
        _vm.Notify += (title, message) => _tray?.ShowNotification(title, message, NotificationIcon.Info);
        _vm.Initialize();

        CreateTray();

        _window = new MainWindow(_vm);
        _window.Closing += (_, ce) =>
        {
            if (_exiting) return;
            ce.Cancel = true; // closing the window just hides it; the deck keeps working
            _window.Hide();
        };

        if (!args.Contains("--minimized")) ShowMainWindow();
    }

    private void CreateTray()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Abrir NumDeck" };
        open.Click += (_, _) => ShowMainWindow();
        var pause = new MenuItem { Header = "Pausar deck", IsCheckable = true };
        pause.SetBinding(MenuItem.IsCheckedProperty, new System.Windows.Data.Binding(nameof(MainViewModel.IsPaused)) { Source = _vm, Mode = System.Windows.Data.BindingMode.TwoWay });
        var stop = new MenuItem { Header = "Parar todos os sons" };
        stop.Click += (_, _) => _vm!.StopSoundsCommand.Execute(null);
        var exit = new MenuItem { Header = "Sair" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(open);
        menu.Items.Add(pause);
        menu.Items.Add(stop);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _tray = new TaskbarIcon
        {
            ToolTipText = "NumDeck",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/numdeck.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        // Efficiency mode would throttle the process and delay key handling.
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _showEvent?.Set();
        _vm?.Dispose();
        _tray?.Dispose();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting) _vm?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
