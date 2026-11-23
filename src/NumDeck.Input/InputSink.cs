using System.Diagnostics;
using System.Runtime.InteropServices;
using NumDeck.Core.Models;
using NumDeck.Input.Native;

namespace NumDeck.Input;

public readonly record struct RawKeyEvent(nint Device, KeyId Key, bool IsUp, ushort VirtualKey);

/// <summary>
/// Owns a dedicated thread with a hidden message-only window that:
///  1. receives Raw Input from every keyboard (tells us WHICH keyboard sent a key);
///  2. answers the hook DLL's "should I swallow this key?" queries.
///
/// Raw Input for a keystroke is posted to us before the focused app reads its
/// WM_KEYDOWN, so when the hook (running inside that app) asks, we match the
/// query against the oldest pending raw record for the same key (FIFO) and
/// block it if that record came from the deck.
/// </summary>
public sealed class InputSink : IDisposable
{
    public const string WindowClass = "NumDeckInputSink";
    private const string QueryMessageName = "NumDeck.HookQuery";
    private const int NoRemoveFlag = 0x10000;
    private const long RecordLifetimeMs = 1000;

    private sealed class Record
    {
        public nint Device; public ushort Scan; public bool Extended, Up; public long Tick; public bool Consumed;
    }

    private readonly List<Record> _pending = new(); // sink thread only
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private nint _hwnd;
    private int _sinkThreadId;
    private uint _queryMsg;
    private Win32.WndProc? _wndProc; // keep the delegate alive
    private HookManager? _hooks;
    private volatile bool _blockingEnabled;
    private nint _deckDevice;

    /// <summary>Raised on the sink thread for every key from every keyboard.</summary>
    public event Action<RawKeyEvent>? RawKey;
    /// <summary>Raised on the sink thread when a keyboard is connected/disconnected.</summary>
    public event Action? DevicesChanged;

    public nint DeckDevice { get => Interlocked.CompareExchange(ref _deckDevice, 0, 0); set => Interlocked.Exchange(ref _deckDevice, value); }
    public bool BlockingEnabled { get => _blockingEnabled; set => _blockingEnabled = value; }
    public string? HookStatus { get; private set; }

    /// <summary>Diagnostics only: treat injected (SendInput) keys with this scan code as deck keys.</summary>
    public volatile int DiagnosticBlockInjectedScan = -1;

    /// <summary>Diagnostics: number of queries received from the hook DLL.</summary>
    public int HookQueryCount;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "NumDeck input sink" };
        _thread.Start();
        _ready.Wait();
    }

    private void ThreadMain()
    {
        _sinkThreadId = Environment.CurrentManagedThreadId;
        try
        {
            _queryMsg = Win32.RegisterWindowMessageW(QueryMessageName);
            _wndProc = WndProc;
            var instance = Win32.GetModuleHandleW(null);
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = instance,
                lpszClassName = WindowClass,
            };
            Win32.RegisterClassExW(ref wc);
            _hwnd = Win32.CreateWindowExW(0, WindowClass, "NumDeck", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, instance, 0);
            if (_hwnd == 0) throw new InvalidOperationException("Não foi possível criar a janela de entrada.");

            // Lower-integrity processes (sandboxed renderers, etc.) must be allowed to query us.
            Win32.ChangeWindowMessageFilterEx(_hwnd, _queryMsg, 1 /* MSGFLT_ALLOW */, 0);

            var rid = new[]
            {
                new Win32.RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, usUsage = 0x06, // generic desktop / keyboard
                    dwFlags = Win32.RIDEV_INPUTSINK | Win32.RIDEV_DEVNOTIFY, hwndTarget = _hwnd,
                }
            };
            if (!Win32.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>()))
                throw new InvalidOperationException("RegisterRawInputDevices falhou: " + Marshal.GetLastWin32Error());

            // Installed from this thread: cross-bitness hook callbacks get delivered here.
            _hooks = new HookManager();
            HookStatus = _hooks.Install(this);
        }
        catch (Exception ex)
        {
            HookStatus = ex.Message;
        }
        finally
        {
            _ready.Set();
        }

        while (Win32.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }

        _hooks?.Dispose();
        if (_hwnd != 0) Win32.DestroyWindow(_hwnd);
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_INPUT)
        {
            HandleRawInput(lParam);
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        if (msg == Win32.WM_INPUT_DEVICE_CHANGE)
        {
            SafeInvoke(() => DevicesChanged?.Invoke());
            return 0;
        }
        if (msg == _queryMsg && _queryMsg != 0)
        {
            Interlocked.Increment(ref HookQueryCount);
            return ShouldBlock((int)wParam, lParam) ? 1 : 0;
        }
        if (msg == Win32.WM_CLOSE)
        {
            Win32.PostQuitMessage(0);
            return 0;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleRawInput(nint hRawInput)
    {
        uint size = (uint)Marshal.SizeOf<Win32.RAWINPUTKEYBOARD>();
        if (Win32.GetRawInputData(hRawInput, Win32.RID_INPUT, out var data, ref size,
                (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>()) == uint.MaxValue)
            return;
        if (data.header.dwType != Win32.RIM_TYPEKEYBOARD) return;
        var kb = data.keyboard;
        // 0xFF = fake key (e.g. part of a Pause sequence); E1 = Pause/Break prefix.
        if (kb.MakeCode == 0 || kb.VKey == 0xFF || (kb.Flags & Win32.RI_KEY_E1) != 0) return;

        var rec = new Record
        {
            Device = data.header.hDevice,
            Scan = kb.MakeCode,
            Extended = (kb.Flags & Win32.RI_KEY_E0) != 0,
            Up = (kb.Flags & Win32.RI_KEY_BREAK) != 0,
            Tick = Environment.TickCount64,
        };
        Prune(rec.Tick);
        _pending.Add(rec);

        var ev = new RawKeyEvent(rec.Device, new KeyId(rec.Scan, rec.Extended), rec.Up, kb.VKey);
        SafeInvoke(() => RawKey?.Invoke(ev));
    }

    private void Prune(long now)
    {
        _pending.RemoveAll(r => r.Consumed || now - r.Tick > RecordLifetimeMs);
        if (_pending.Count > 256) _pending.RemoveRange(0, _pending.Count - 256);
    }

    private bool ShouldBlock(int packedWParam, nint keyFlags)
    {
        bool peekOnly = (packedWParam & NoRemoveFlag) != 0;
        long flags = keyFlags;
        ushort scan = (ushort)((flags >> 16) & 0xFF);
        bool ext = ((flags >> 24) & 1) != 0;
        bool up = ((flags >> 31) & 1) != 0;

        var rec = FindRecord(scan, ext, up);
        if (rec is null)
        {
            // The raw record may still be sitting in our queue: process it now.
            PumpRawInput();
            rec = FindRecord(scan, ext, up);
        }
        if (rec is null) return false;
        if (!peekOnly) rec.Consumed = true;
        if (!_blockingEnabled) return false;
        if (rec.Device == 0) return DiagnosticBlockInjectedScan == scan; // injected input (SendInput)
        return rec.Device == DeckDevice;
    }

    private Record? FindRecord(ushort scan, bool ext, bool up)
    {
        long now = Environment.TickCount64;
        foreach (var r in _pending)
        {
            if (r.Consumed || now - r.Tick > RecordLifetimeMs) continue;
            if (r.Up == up && new KeyId(r.Scan, r.Extended).Matches(scan, ext)) return r;
        }
        return null;
    }

    private void PumpRawInput()
    {
        while (Win32.PeekMessageW(out var msg, _hwnd, Win32.WM_INPUT, Win32.WM_INPUT, Win32.PM_REMOVE))
            Win32.DispatchMessageW(ref msg);
    }

    /// <summary>
    /// Hook query that Windows delivered inside NumDeck's own process. Out-of-context
    /// calls arrive on the sink thread (answer directly); keys for our own windows
    /// arrive on the UI thread (hop to the sink thread, which owns the raw records).
    /// </summary>
    internal bool OnHookQuery(int packedWParam, nint keyFlags)
    {
        Interlocked.Increment(ref HookQueryCount);
        if (Environment.CurrentManagedThreadId == _sinkThreadId) return ShouldBlock(packedWParam, keyFlags);
        if (_hwnd == 0) return false;
        Win32.SendMessageTimeoutW(_hwnd, _queryMsg, packedWParam, keyFlags, 0x2 /* SMTO_ABORTIFHUNG */, 100, out var result);
        return result == 1;
    }

    private static void SafeInvoke(Action a)
    {
        try { a(); } catch (Exception ex) { Debug.WriteLine(ex); }
    }

    public void Dispose()
    {
        if (_thread is null) return;
        if (_hwnd != 0) Win32.PostMessageW(_hwnd, Win32.WM_CLOSE, 0, 0);
        _thread.Join(2000);
        _thread = null;
    }
}
