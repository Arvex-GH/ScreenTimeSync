#pragma once
#include <windows.h>

#ifdef NATIVEHOOK_EXPORTS
#define NATIVEHOOK_API extern "C" __declspec(dllexport)
#else
#define NATIVEHOOK_API extern "C" __declspec(dllimport)
#endif

// Callback when foreground changes: processName (UTF-16)
typedef void(__stdcall* ForegroundCallback)(const wchar_t* processName);

NATIVEHOOK_API bool __cdecl InstallHook(int threadId);
NATIVEHOOK_API void __cdecl UninstallHook();
NATIVEHOOK_API void __cdecl SetCallback(ForegroundCallback cb);

// Idle helpers
NATIVEHOOK_API DWORD __cdecl GetIdleMilliseconds();
