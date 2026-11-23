using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NumDeck.Input.Native;

namespace NumDeck.Input;

/// <summary>Loads the native WH_KEYBOARD hook DLL and routes its queries to the InputSink.</summary>
internal sealed unsafe class HookManager : IDisposable
{
    private static InputSink? s_sink;
    private nint _module;
    private delegate* unmanaged<int> _uninstall;

    public static string DllPath => Path.Combine(AppContext.BaseDirectory, "native", "NumDeckHook64.dll");

    /// <returns>Null on success, or a description of what failed.</returns>
    public string? Install(InputSink sink)
    {
        if (!File.Exists(DllPath)) return "NumDeckHook64.dll não encontrado — as teclas do deck não serão bloqueadas.";

        _module = Win32.LoadLibraryW(DllPath);
        if (_module == 0) return "Falha ao carregar NumDeckHook64.dll.";
        var install = (delegate* unmanaged<delegate* unmanaged<nint, nint, int>, int>)Win32.GetProcAddress(_module, "NumDeck_InstallHook");
        _uninstall = (delegate* unmanaged<int>)Win32.GetProcAddress(_module, "NumDeck_UninstallHook");
        if (install == null || _uninstall == null) return "NumDeckHook64.dll inválido.";

        s_sink = sink;
        if (install(&OnQuery) == 0) return "SetWindowsHookEx falhou — as teclas do deck não serão bloqueadas.";
        return null;
    }

    /// <summary>
    /// Called by the hook when Windows runs it inside NumDeck: on the sink thread
    /// (out-of-context calls for other apps) or on our own UI thread (our windows).
    /// </summary>
    [UnmanagedCallersOnly]
    private static int OnQuery(nint packedWParam, nint keyFlags)
    {
        try { return s_sink?.OnHookQuery((int)packedWParam, keyFlags) == true ? 1 : 0; }
        catch { return 0; }
    }

    public void Dispose()
    {
        if (_uninstall != null) _uninstall();
        _uninstall = null;
        s_sink = null;
        // If Windows injected the DLL elsewhere, those copies unload on their own.
        if (_module != 0) Win32.FreeLibrary(_module);
        _module = 0;
    }
}
