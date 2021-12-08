#include "stack-trace.h"
#include "common.h"
#include <dbghelp.h>

#pragma comment (lib, "dbghelp")

using namespace std;

HMODULE GetModuleFromAddress(void* address) {
    auto flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;

    HMODULE hModule = NULL;
    GetModuleHandleExW(flags, (LPCTSTR)address, &hModule);
    return hModule;
}

bool symInitialized = false;
void PrintStackTrace(CONTEXT *cpuContext, UINT maxFrames) {
    auto context = *cpuContext;
    HRESULT Hr;
    STACKFRAME64 stackFrame = {};

    stackFrame.AddrPC.Offset = context.Eip;
    stackFrame.AddrPC.Mode = AddrModeFlat;
    stackFrame.AddrFrame.Offset = context.Ebp;
    stackFrame.AddrFrame.Mode = AddrModeFlat;
    stackFrame.AddrStack.Offset = context.Esp;
    stackFrame.AddrStack.Mode = AddrModeFlat;

    auto processHandle = GetCurrentProcess();
    auto threadHandle = GetCurrentThread();

    if (symInitialized == false) {
        SymInitialize(processHandle, NULL, TRUE);
        symInitialized = true;
    }

    WCHAR modulePath[MAX_PATH + 1];
    modulePath[MAX_PATH] = L'\0';
    for (auto i = 0; i < maxFrames; i++) {
        if (!StackWalk64(IMAGE_FILE_MACHINE_I386, processHandle, threadHandle, &stackFrame, &context,
            NULL, SymFunctionTableAccess64, SymGetModuleBase64, NULL))
            break;

        if (stackFrame.AddrPC.Offset == 0)
            break;

        auto address = (void *)stackFrame.AddrPC.Offset;

        auto module = GetModuleFromAddress(address);

        auto offset = (void *)((DWORD)address - (DWORD)module);

        GetModuleFileNameW(module, modulePath, MAX_PATH);
        auto moduleName = PathFindFileNameW(modulePath);

        wcout << moduleName << "+" << offset << "\n";
    }
}
