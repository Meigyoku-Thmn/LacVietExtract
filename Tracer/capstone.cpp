#include "common.h"
#include "capstone.h"

using namespace std;

csh capstone_handle;
int capstoneState = 0;
bool InitializeCapstone() {
    if (capstoneState < 0)
        return false;
    if (capstoneState > 0)
        return true;

    WCHAR lastDirPath[MAX_PATH + 1];
    GetCurrentDirectoryW(MAX_PATH + 1, lastDirPath);

    WCHAR dirPath[MAX_PATH + 1];
    dirPath[MAX_PATH] = L'\0';
    GetModuleFileNameW(g_tracerModule, dirPath, MAX_PATH);
    PathRemoveFileSpecW(dirPath);
    SetCurrentDirectoryW(dirPath);

    if (cs_open(CS_ARCH_X86, CS_MODE_32, &capstone_handle) != CS_ERR_OK) {
        cout << "Cannot open Capstone Engine.";
        capstoneState = -1;
    } else if (cs_option(capstone_handle, CS_OPT_DETAIL, CS_OPT_ON)) {
        cout << "Cannot configure Capstone Engine.";
        capstoneState = -1;
    }
    SetCurrentDirectoryW(lastDirPath);

    if (capstone_handle < 0)
        return false;
    capstoneState = 1;
    return true;
}