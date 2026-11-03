#define NATIVEHOOK_EXPORTS
#include "NativeHook.h"
#include <psapi.h>
#include <string>

#pragma comment(lib, "psapi.lib")

static HHOOK g_hHook = nullptr;
static HINSTANCE g_hInst = nullptr;
static ForegroundCallback g_callback = nullptr;

static std::wstring GetProcessNameFromHwnd(HWND hwnd) {
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (!pid) return L"";
    HANDLE hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, pid);
    if (!hProc) return L"";
    wchar_t buf[MAX_PATH] = {0};
    if (GetModuleBaseNameW(hProc, nullptr, buf, MAX_PATH) == 0) {
        CloseHandle(hProc);
        return L"";
    }
    CloseHandle(hProc);
    std::wstring s(buf);
    // strip .exe
    auto pos = s.find_last_of(L".");
    if (pos != std::wstring::npos) s = s.substr(0, pos);
    return s;
}

static LRESULT CALLBACK ShellProc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode >= 0) {
        if (wParam == HSHELL_WINDOWACTIVATED || wParam == HSHELL_RUDEAPPACTIVATED) {
            HWND hwnd = (HWND)lParam;
            if (!hwnd) hwnd = GetForegroundWindow();
            auto name = GetProcessNameFromHwnd(hwnd);
            if (!name.empty() && g_callback) g_callback(name.c_str());
        }
    }
    return CallNextHookEx(g_hHook, nCode, wParam, lParam);
}

bool __cdecl InstallHook(int threadId) {
    if (g_hHook) return true;
    g_hHook = SetWindowsHookExW(WH_SHELL, ShellProc, g_hInst, threadId);
    return g_hHook != nullptr;
}

void __cdecl UninstallHook() {
    if (g_hHook) { UnhookWindowsHookEx(g_hHook); g_hHook = nullptr; }
}

void __cdecl SetCallback(ForegroundCallback cb) { g_callback = cb; }

DWORD __cdecl GetIdleMilliseconds() {
    LASTINPUTINFO lii{ sizeof(LASTINPUTINFO), 0 };
    if (!GetLastInputInfo(&lii)) return 0;
    return GetTickCount() - lii.dwTime;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) g_hInst = (HINSTANCE)hModule;
    return TRUE;
}
