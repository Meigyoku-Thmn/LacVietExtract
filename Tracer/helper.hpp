#pragma once
#include "common.h"

HMODULE GetModuleFromAddress(PVOID address) {
    HMODULE hModule = NULL;
    auto options = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
    if (GetModuleHandleExW(options, (LPCWSTR)address, &hModule))
        return hModule;
    return NULL;
}

bool GetModuleNameA(HMODULE hModule, string &output) {
    CHAR moduleName[MAX_PATH + 1];
    moduleName[MAX_PATH] = '\0';
    if (GetModuleFileNameA(hModule, moduleName, MAX_PATH) == 0)
        return false;
    PathStripPathA(moduleName);
    output = moduleName;
    return true;
}