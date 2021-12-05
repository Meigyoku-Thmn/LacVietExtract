#include "pch.h"
#include <Shlwapi.h>
#include <cstdio>
#include <iostream>
#include <string>
#include <sstream>
#include <iomanip>
#include <iterator> // missing in csv2
#include <vector>
#include <easyhook.h>
#include <capstone/capstone.h>
#include <csv2/writer.hpp>
#include <csv2/reader.hpp>
#include <string_view>

#pragma comment (lib, "delayimp")
#pragma comment (lib, "Shlwapi")

using namespace std;
using namespace csv2;

// Shut up
#pragma warning(disable: 6031)

template <typename T>
string to_hex(T val, size_t width = sizeof(T) * 2) {
    stringstream ss;
    ss << setfill('0') << setw(width) << hex << (val | 0);
    return ss.str();
}

string dump_hex(UCHAR *data, size_t size) {
    stringstream ss;
    ss << setfill('0') << hex;
    for (auto i = 0; i < size; i++)
        ss << setw(2) << (data[i] | 0) << " ";
    return ss.str();
}

using CsvWriter = Writer<delimiter<';'>>;

wstring logPath;
ofstream *logStream = nullptr;
CsvWriter *csvWriter = nullptr;
UINT32 ordinal = 1;
bool csvWriterInitialized = false;

void WriteCsvRecord(
    string &&ordinal, string &&type, string &&nByte, string &&mnemonic, string &&operands,
    string &&memoryOffset, string &&data, string &&codeAddress, string &&flowId,
    string &&esi, string &&edi, string &&eax, string &&ebx, string &&ecx, string &&edx, string &&esp, string &&ebp
) {
    csvWriter->write_row(vector<string> {
        ordinal, type, nByte, mnemonic, operands, memoryOffset, data, codeAddress, flowId,
            esi, edi, eax, ebx, ecx, edx, esp, ebp,
    });
}

void InitializeCSVWriter() {
    if (csvWriterInitialized)
        return;

    logStream = new ofstream(logPath, ios_base::out | ios_base::binary);
    csvWriter = new CsvWriter(*logStream);

    WriteCsvRecord(
        "Ordinal", "Type", "nByte", "Mnemonic", "Operands",
        "MemoryOffset", "Data", "CodeAddress", "FlowId",
        "ESI", "EDI", "EAX", "EBX", "ECX", "EDX", "ESP", "EBP"
    );

    csvWriterInitialized = true;
}

HMODULE tracerModule;
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    tracerModule = hModule;
    switch (ul_reason_for_call) {
        case DLL_PROCESS_ATTACH:
            break;
        case DLL_THREAD_ATTACH:
            break;
        case DLL_THREAD_DETACH:
            break;
        case DLL_PROCESS_DETACH:
            delete csvWriter;
            break;
    }
    return TRUE;
}

LONG NTAPI VEH(PEXCEPTION_POINTERS pExceptionInfo);
size_t __fastcall MyRoutine(DWORD *instance, void *_, char *block_name, char **outputObj);
#define RoutineType decltype(&MyRoutine)
RoutineType targetRoutine;

csh capstoneHandle;
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
    GetModuleFileNameW(tracerModule, dirPath, MAX_PATH);
    PathRemoveFileSpecW(dirPath);
    SetCurrentDirectoryW(dirPath);

    if (cs_open(CS_ARCH_X86, CS_MODE_32, &capstoneHandle) != CS_ERR_OK) {
        cout << "Cannot open Capstone Engine.";
        capstoneState = -1;
    } else if (cs_option(capstoneHandle, CS_OPT_DETAIL, CS_OPT_ON)) {
        cout << "Cannot configure Capstone Engine.";
        capstoneState = -1;
    }
    SetCurrentDirectoryW(lastDirPath);

    if (capstoneHandle < 0)
        return false;
    capstoneState = 1;
    return true;
}

#define TARGET_LIB_NAME "mtdDataLIB.dll"
HMODULE targetLibModule;
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
        cout << "Failed to get " TARGET_LIB_NAME " module";
        return;
    }
    targetRoutine = (RoutineType)((DWORD)targetLibModule + 0xB01D10 - 0xB00000);

    HOOK_TRACE_INFO hHook = {NULL};
    auto rs = LhInstallHook(targetRoutine, MyRoutine, NULL, &hHook);
    if (FAILED(rs)) {
        cout << "Failed to hook target function: " << RtlGetLastErrorString() << "\n";
        return;
    }
    cout << "Hook installed successfully.\n";
    ULONG ACLEntries[1] = {0};
    LhSetExclusiveACL(ACLEntries, 1, &hHook);

    AddVectoredExceptionHandler(1, VEH);
}

DWORD beginPageAddress; // inclusive
DWORD endPageAddress; // exclusive
DWORD dataAddr;
DWORD dataSize;
size_t __fastcall MyRoutine(DWORD *instance, void *_, char *block_name, char **outputObj) {
    auto dataSize = targetRoutine(instance, _, block_name, outputObj);
    if (strcmp(block_name, "Content0") != 0)
        return dataSize;
    cout << "Hit Content0" << "\n";

    if (InitializeCapstone() == false)
        return dataSize;

    InitializeCSVWriter();

    auto data = *outputObj;
    dataAddr = (DWORD)data;
    ::dataSize = dataSize;

    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);
    auto pageSize = sysInfo.dwPageSize;

    MEMORY_BASIC_INFORMATION mbi;
    VirtualQuery(data, &mbi, sizeof(mbi));
    beginPageAddress = (DWORD)mbi.BaseAddress;
    VirtualQuery(data + dataSize - 1, &mbi, sizeof(mbi));
    endPageAddress = (DWORD)mbi.BaseAddress + pageSize;

    DWORD oldProtect;
    VirtualProtect(data, dataSize, PAGE_READWRITE | PAGE_GUARD, &oldProtect);

    return dataSize;
}

int flowId = 0;
int lastNByte = 0;
DWORD lastMemAddr2 = 0;
DWORD lastCodeAddr = 0;
DWORD lastMemAddr = 0;
LONG NTAPI VEH(PEXCEPTION_POINTERS pExceptionInfo) {
    LONG exceptionCode = pExceptionInfo->ExceptionRecord->ExceptionCode;

    if (exceptionCode == EXCEPTION_GUARD_PAGE) {
        lastMemAddr = pExceptionInfo->ExceptionRecord->ExceptionInformation[1];

        if (lastMemAddr < beginPageAddress || lastMemAddr >= endPageAddress)
            return EXCEPTION_CONTINUE_SEARCH;

        if (lastMemAddr >= dataAddr && lastMemAddr < dataAddr + dataSize) {
            auto context = pExceptionInfo->ContextRecord;
            auto mode = pExceptionInfo->ExceptionRecord->ExceptionInformation[0];
            auto modeStr = mode == 0 ? "Read " : "Write ";
            auto codeAddr = (DWORD)pExceptionInfo->ExceptionRecord->ExceptionAddress;
            auto memOffset = (void *)(lastMemAddr - dataAddr);
            auto esi = context->Esi;
            auto edi = context->Edi;
            auto eax = context->Eax;
            auto ebx = context->Ebp;
            auto ecx = context->Ecx;
            auto edx = context->Edx;
            auto esp = context->Esp;
            auto ebp = context->Ebp;
            if (lastCodeAddr != codeAddr) {
                lastCodeAddr = codeAddr;
                if (lastMemAddr2 + lastNByte != lastMemAddr)
                    flowId++;
            }
            lastMemAddr2 = lastMemAddr;
            cs_insn *instructions;
            if (cs_disasm(capstoneHandle, (uint8_t *)codeAddr, 15, (uint64_t)codeAddr, 1, &instructions) > 0) {
                auto mnemonic = instructions[0].mnemonic;
                auto operands = instructions[0].op_str;
                auto x86 = &instructions[0].detail->x86;
                int nByte = 0;
                if (x86->op_count > 0)
                    nByte = (int)x86->operands[x86->op_count - 1].size;
                lastNByte = nByte;
                auto data = (UCHAR*)lastMemAddr;
                WriteCsvRecord(
                    to_string(ordinal++), modeStr, to_string(nByte), mnemonic, operands,
                    to_hex((DWORD)memOffset), dump_hex(data, nByte), to_hex(codeAddr), to_string(flowId),
                    to_hex(esi), to_hex(edi),
                    to_hex(eax), to_hex(ebx), to_hex(ecx), to_hex(edx),
                    to_hex(esp), to_hex(ebp)
                );
                cs_free(instructions, 1);

            } else {
                WriteCsvRecord(
                    to_string(ordinal++), modeStr, "", "Error", "",
                    to_hex((DWORD)memOffset), "", to_hex(codeAddr), to_string(flowId),
                    to_hex(esi), to_hex(edi),
                    to_hex(eax), to_hex(ebx), to_hex(ecx), to_hex(edx),
                    to_hex(esp), to_hex(ebp)
                );
            }
        }

        // Set up for STATUS_SINGLE_STEP
        pExceptionInfo->ContextRecord->EFlags |= 0x00000100;

        return EXCEPTION_CONTINUE_EXECUTION;

    } else if (exceptionCode == STATUS_SINGLE_STEP) {
        if (lastMemAddr == 0)
            return EXCEPTION_CONTINUE_SEARCH;

        DWORD oldProtect;
        auto rs = VirtualProtect((void *)lastMemAddr, 1, PAGE_READWRITE | PAGE_GUARD, &oldProtect);
        lastMemAddr = 0;

        // Remove flag for STATUS_SINGLE_STEP
        pExceptionInfo->ContextRecord->EFlags &= ~0x00000100;

        return EXCEPTION_CONTINUE_EXECUTION;

    }
    return EXCEPTION_CONTINUE_SEARCH;
}
