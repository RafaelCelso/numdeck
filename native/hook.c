// NumDeck keyboard hook DLL (x64).
//
// A global WH_KEYBOARD hook is consulted whenever an app is about to receive a
// key message. Raw Input (which tells WHICH keyboard sent the key) has already
// been posted to NumDeck's sink window by then, so the hook asks NumDeck whether
// this keystroke came from the deck keyboard, and swallows it if so.
//
// Windows runs the hook in one of two ways:
//  * injected into the target process (classic behaviour, 64-bit targets): we
//    ask NumDeck with a synchronous SendMessageTimeout to its sink window;
//  * "out of context" on the thread that installed the hook (always for 32-bit
//    targets, and on recent Windows builds for everything): we're inside
//    NumDeck, so we call its callback directly.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define SINK_CLASS L"NumDeckInputSink"
#define QUERY_MSG L"NumDeck.HookQuery"
#define NOREMOVE_FLAG 0x10000
#define QUERY_TIMEOUT_MS 100

typedef int (*query_callback)(WPARAM packed, LPARAM keyFlags);

static HINSTANCE g_inst;
static HHOOK g_hook;
static UINT g_msg;
static HWND g_target;
// Only set inside NumDeck's own process (copies injected elsewhere keep zeros).
static DWORD g_installer_pid;
static query_callback g_callback;

static HWND find_target(void)
{
    if (g_target && IsWindow(g_target)) return g_target;
    g_target = FindWindowExW(HWND_MESSAGE, NULL, SINK_CLASS, NULL);
    return g_target;
}

static int ask_numdeck(WPARAM packed, LPARAM keyFlags)
{
    if (g_installer_pid == GetCurrentProcessId())
        return g_callback ? g_callback(packed, keyFlags) : 0;

    HWND target = find_target();
    if (!target) return 0;
    if (!g_msg) g_msg = RegisterWindowMessageW(QUERY_MSG);
    DWORD_PTR result = 0;
    return SendMessageTimeoutW(target, g_msg, packed, keyFlags, SMTO_ABORTIFHUNG | SMTO_BLOCK,
                               QUERY_TIMEOUT_MS, &result) && result == 1;
}

static LRESULT CALLBACK keyboard_proc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION || code == HC_NOREMOVE)
    {
        WPARAM packed = (wParam & 0xFFFF) | (code == HC_NOREMOVE ? NOREMOVE_FLAG : 0);
        if (ask_numdeck(packed, lParam)) return 1; // swallow: key came from the deck
    }
    return CallNextHookEx(g_hook, code, wParam, lParam);
}

__declspec(dllexport) BOOL NumDeck_InstallHook(query_callback callback)
{
    if (g_hook) return TRUE;
    g_installer_pid = GetCurrentProcessId();
    g_callback = callback;
    // Don't rely on DllMain having run: resolve our own module from a code address.
    if (!g_inst)
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCWSTR)(void *)keyboard_proc, &g_inst);
    g_hook = SetWindowsHookExW(WH_KEYBOARD, keyboard_proc, g_inst, 0);
    return g_hook != NULL;
}

__declspec(dllexport) BOOL NumDeck_UninstallHook(void)
{
    BOOL ok = TRUE;
    if (g_hook) ok = UnhookWindowsHookEx(g_hook);
    g_hook = NULL;
    g_callback = NULL;
    return ok;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_inst = inst;
        DisableThreadLibraryCalls(inst);
    }
    return TRUE;
}
