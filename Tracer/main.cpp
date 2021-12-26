#include "common.h"
#include "csv_dump.h"
#include "capstone.h"
#include "tracer.h"

#pragma comment (lib, "delayimp")
#pragma comment (lib, "Shlwapi")

using namespace std;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    g_tracerModule = hModule;
    if (ul_reason_for_call == DLL_PROCESS_DETACH)
        DisposeCSVWriter();
    return TRUE;
}

wstring logPath;
size_t __fastcall MyRoutine(PDWORD instance, PVOID _, PCHAR block_name, PBYTE *outputObj);

constexpr auto TARGET_LIB_NAME = "mtdDataLIB.dll";
decltype(&MyRoutine) targetRoutine;
extern "C" void DLLEXPORT PASCAL NativeInjectionEntryPoint(REMOTE_ENTRY_INFO * inRemoteInfo) {
    AllocConsole();
    freopen("CONIN$", "r", stdin);
    freopen("CONOUT$", "w", stdout);
    freopen("CONERR$", "w", stderr);

    cout << "Tracer.dll injected.\n";

    RhWakeUpProcess();

    logPath = wstring((WCHAR *)inRemoteInfo->UserData, inRemoteInfo->UserDataSize / 2);
    wcout << "Log path: " << logPath << "\n";

    auto targetLibModule = GetModuleHandleA(TARGET_LIB_NAME);
    if (targetLibModule == NULL) {
        cout << "Failed to get " << TARGET_LIB_NAME << " module";
        return;
    }
    targetRoutine = (decltype(&MyRoutine))((DWORD)targetLibModule + 0xB01D10 - 0xB00000);

    HOOK_TRACE_INFO hHook = {NULL};
    auto rs = LhInstallHook(targetRoutine, MyRoutine, NULL, &hHook);
    if (FAILED(rs)) {
        cout << "Failed to hook target function: " << RtlGetLastErrorString() << "\n";
        return;
    }
    cout << "Hook installed successfully.\n";
    ULONG ACLEntries[1] = {0};
    LhSetExclusiveACL(ACLEntries, 1, &hHook);
}

size_t __fastcall MyRoutine(PDWORD instance, PVOID _, PCHAR block_name, PBYTE *outputObj) {
    auto dataSize = targetRoutine(instance, _, block_name, outputObj);

    if (strcmp(block_name, "Content0") != 0)
        return dataSize;

    cout << "Hit " << block_name << "\n";

    if (InitializeCapstone() == false)
        return dataSize;

    InitializeCSVWriter(logPath.c_str());
    InitializeTracerCurrentThread();

    auto data = *outputObj;
    StartTracing(data, data + dataSize);

    return dataSize;
}

