#include "common.h"
#include "tracer.h"
#include "capstone.h"
#include "csv_dump.h"
#include "string_dump.hpp"
#include "helper.hpp"
#include <dbghelp.h>
#pragma comment(lib, "Dbghelp.lib")

LONG NTAPI VeHandler(PEXCEPTION_POINTERS pExceptionInfo);
LRESULT CALLBACK KeyboardProc(int code, WPARAM wParam, LPARAM lParam);
LONG HandleGuardPage(PEXCEPTION_POINTERS pExceptionInfo);
LONG HandleSingleStep(PEXCEPTION_POINTERS pExceptionInfo);
void SetPageGuard();

struct State {
    PBYTE beginPageAddress = nullptr;
    PBYTE endPageAddress = nullptr;
    PBYTE beginAddress = nullptr;
    PBYTE endAddress = nullptr;
    DWORD threadId = 0;
    vector<PBYTE> callStack;
} state;
void ResetState() {
    state = State();
}

struct VehState {
    size_t flowId = 0;
    size_t lastNByte = 0;
    size_t ordinal = 1;
    bool needReGuard = false;
    PBYTE lastMemoryAddress = nullptr;
    PBYTE lastCodeAddress = nullptr;
} vehState;
void ResetVehState() {
    vehState = VehState();
}

void GetStackTraceItem(ostream &output, PBYTE address) {
    DWORD64 displacement;
    static char symbolInfoBuffer[sizeof(SYMBOL_INFO) + MAX_SYM_NAME * sizeof(CHAR)];
    static auto &symbolInfo = *(PSYMBOL_INFO)symbolInfoBuffer;
    symbolInfo.SizeOfStruct = sizeof(SYMBOL_INFO);
    symbolInfo.MaxNameLen = MAX_SYM_NAME;
    string moduleName;

    if (SymFromAddr(GetCurrentProcess(), (DWORD64)address, &displacement, &symbolInfo) == FALSE) {
        GetModuleFromAddress(address);
        auto baseModuleAddr = GetModuleFromAddress(address);
        GetModuleNameA(baseModuleAddr, moduleName);
        if (GetModuleNameA(baseModuleAddr, moduleName) == false) {
            output << "0x" << (PVOID)address << "\n";
            return;
        }
        auto offset = address - (PBYTE)baseModuleAddr;
        output << "0x" << (PVOID)address << " " << moduleName << "+0x" << (PVOID)offset << "\n";
        return;
    }
    HMODULE baseModuleAddr = (HMODULE)symbolInfo.ModBase;
    if (baseModuleAddr == NULL)
        baseModuleAddr = GetModuleFromAddress((PVOID)symbolInfo.Address);
    if (GetModuleNameA(baseModuleAddr, moduleName) == false) {
        output << "0x" << (PVOID)address << "\n";
        return;
    }
    auto symbolName = symbolInfo.Name;
    auto symbolAddr = symbolInfo.Address;
    auto offset = address - (PBYTE)symbolAddr;
    auto offset2 = address - (PBYTE)baseModuleAddr;
    output << "0x" << (PVOID)address << " " << moduleName << "!" << symbolName << "+0x" << (PVOID)offset
        << " (base+0x" << (PVOID)offset2 << ")" << "\n";
}

bool symUtilInitialized = false;
string GetStackTrace(PVOID topAddress, ostream *output = nullptr) {
    if (symUtilInitialized == false) {
        SymInitialize(GetCurrentProcess(), NULL, TRUE);
        symUtilInitialized = true;
    }
    auto noOutput = output == nullptr;
    if (output == nullptr)
        output = new stringstream();
    GetStackTraceItem(*output, (PBYTE)topAddress);
    for (auto it = state.callStack.rbegin(); it != state.callStack.rend(); ++it)
        GetStackTraceItem(*output, *it);
    if (noOutput) {
        auto rs = ((stringstream*)output)->str();
        delete output;
        return rs;
    }
    return string();
}

bool tracerInitialized = false;
void InitializeTracerCurrentThread() {
    if (tracerInitialized == true)
        return;
    AddVectoredExceptionHandler(1, VeHandler);
    SetWindowsHookExW(WH_KEYBOARD, KeyboardProc, 0, GetCurrentThreadId());
    tracerInitialized = true;
}

void SetTrapFlagCurrentThread() {
    __asm {
        PUSHF
        OR WORD PTR[ESP], 0x0100
        POPF
    }
}

void SetPageGuard() {
    DWORD _;
    VirtualProtect(state.beginAddress, state.endAddress - state.beginAddress, PAGE_READWRITE | PAGE_GUARD, &_);
}

bool stopTracing = true;

void StartTracing(PBYTE beginAddress, PBYTE endAddress) {
    StopTracing();
    cout << "Start tracing." << "\n";
    stopTracing = false;

    SYSTEM_INFO sysInfo;
    GetSystemInfo(&sysInfo);
    auto pageSize = sysInfo.dwPageSize;

    MEMORY_BASIC_INFORMATION mbi;
    VirtualQuery(beginAddress, &mbi, sizeof(mbi));
    state.beginPageAddress = (PBYTE)mbi.BaseAddress;
    VirtualQuery(endAddress - 1, &mbi, sizeof(mbi));
    state.endPageAddress = (PBYTE)mbi.BaseAddress + pageSize;

    state.beginAddress = beginAddress;
    state.endAddress = endAddress;
    state.threadId = GetCurrentThreadId();

    SetPageGuard();
    SetTrapFlagCurrentThread();
}

void StopTracing() {
    if (stopTracing == true)
        return;

    stopTracing = true;

    cout << "Stop tracing." << "\n";

    DWORD _;
    VirtualProtect(state.beginAddress, state.endAddress - state.beginAddress, PAGE_READWRITE, &_);

    ResetState();
    ResetVehState();
}

LRESULT CALLBACK KeyboardProc(int code, WPARAM wParam, LPARAM lParam) {
    // on Q key pressed 
    if (code == HC_ACTION && wParam == 0x51 && (HIWORD(lParam) & KF_UP) == KF_UP)
        StopTracing();
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

bool isLogging = true;
template <typename Functor>
void Logging(Functor &&callback, bool setGuard = true) {
    isLogging = true;
    callback();
    isLogging = false;
    if (setGuard)
        SetPageGuard();
}

LONG NTAPI VeHandler(PEXCEPTION_POINTERS pExceptionInfo) {
    if (state.threadId != GetCurrentThreadId())
        return EXCEPTION_CONTINUE_SEARCH;

    switch (pExceptionInfo->ExceptionRecord->ExceptionCode) {
        case EXCEPTION_GUARD_PAGE:
            if (isLogging)
                return EXCEPTION_CONTINUE_EXECUTION;
            return HandleGuardPage(pExceptionInfo);

        case STATUS_SINGLE_STEP:
            return HandleSingleStep(pExceptionInfo);

        default:
            return EXCEPTION_CONTINUE_SEARCH;
    }
}

void SetTrapFlagContext(PCONTEXT context) {
    context->EFlags |= 0x00000100;
}

const char* GetAccessMode(PEXCEPTION_POINTERS pExceptionInfo) {
    auto mode = pExceptionInfo->ExceptionRecord->ExceptionInformation[0];
    return
        mode == 0 ? "Read" :
        mode == 1 ? "Write" : "Execute";
}

bool CheckIfCall(PBYTE address) {
    auto opcode = *address;

    cs_insn *instructions;
    cs_disasm(capstone_handle, (uint8_t *)address, 15, (uint64_t)address, 1, &instructions);
    auto isCall = instructions[0].id == X86_INS_CALL;
    cs_free(instructions, 1);

    return isCall;
}

bool CheckIfRet(PBYTE address) {
    auto opcode = *address;

    cs_insn *instructions;
    cs_disasm(capstone_handle, (uint8_t *)address, 15, (uint64_t)address, 1, &instructions);
    auto isRet = instructions[0].id == X86_INS_RET;
    cs_free(instructions, 1);

    return isRet;
}

LONG HandleGuardPage(PEXCEPTION_POINTERS pExceptionInfo) {
    auto memoryAddress = (PBYTE)pExceptionInfo->ExceptionRecord->ExceptionInformation[1];

    if (memoryAddress < state.beginPageAddress || memoryAddress >= state.endPageAddress)
        return EXCEPTION_CONTINUE_SEARCH;

    vehState.needReGuard = true;

    Logging([=]() {
        if (memoryAddress >= state.beginAddress && memoryAddress < state.endAddress) {
            auto context = pExceptionInfo->ContextRecord;
            auto mode = GetAccessMode(pExceptionInfo);
            auto codeAddress = (PBYTE)pExceptionInfo->ExceptionRecord->ExceptionAddress;
            auto stackTrace = GetStackTrace(codeAddress);
            auto memOffset = memoryAddress - state.beginAddress;
            auto esi = context->Esi;
            auto edi = context->Edi;
            auto eax = context->Eax;
            auto ebx = context->Ebp;
            auto ecx = context->Ecx;
            auto edx = context->Edx;
            auto esp = context->Esp;
            auto ebp = context->Ebp;
            if (vehState.lastCodeAddress != codeAddress) {
                vehState.lastCodeAddress = codeAddress;
                if (vehState.lastMemoryAddress + vehState.lastNByte != memoryAddress)
                    vehState.flowId++;
            }
            vehState.lastMemoryAddress = memoryAddress;
            cs_insn *instructions;
            if (cs_disasm(capstone_handle, (uint8_t *)codeAddress, 15, (uint64_t)codeAddress, 1, &instructions) > 0) {
                auto mnemonic = instructions[0].mnemonic;
                auto operands = instructions[0].op_str;
                auto x86 = &instructions[0].detail->x86;
                int nByte = 0;
                if (x86->op_count > 0)
                    nByte = (int)x86->operands[x86->op_count - 1].size;
                vehState.lastNByte = nByte;
                auto data = memoryAddress;
                WriteCsvRecord(
                    to_string(vehState.ordinal++), mode, to_string(nByte), mnemonic, operands,
                    to_hex(memOffset), dump_hex(data, nByte), to_hex(codeAddress), to_string(vehState.flowId),
                    to_hex(esi), to_hex(edi),
                    to_hex(eax), to_hex(ebx), to_hex(ecx), to_hex(edx),
                    to_hex(esp), to_hex(ebp),
                    forward<string>(stackTrace)
                );
                cs_free(instructions, 1);

            } else {
                WriteCsvRecord(
                    to_string(vehState.ordinal++), mode, "", "Error", "",
                    to_hex(memOffset), "", to_hex(codeAddress), to_string(vehState.flowId),
                    to_hex(esi), to_hex(edi),
                    to_hex(eax), to_hex(ebx), to_hex(ecx), to_hex(edx),
                    to_hex(esp), to_hex(ebp),
                    forward<string>(stackTrace)
                );
            }
        }
    }, false);

    SetTrapFlagContext(pExceptionInfo->ContextRecord);

    return EXCEPTION_CONTINUE_EXECUTION;
}

LONG HandleSingleStep(PEXCEPTION_POINTERS pExceptionInfo) {
    if (vehState.needReGuard == true) {
        SetPageGuard();
        vehState.needReGuard = false;
    }

    if (stopTracing == true)
        return EXCEPTION_CONTINUE_EXECUTION;

    static bool readReturnAddress = false;
    auto codeAddress = (PBYTE)pExceptionInfo->ExceptionRecord->ExceptionAddress;


    Logging([=]() {
        if (readReturnAddress == true) {
            readReturnAddress = false;
            auto returnAddress = *(PBYTE *)pExceptionInfo->ContextRecord->Esp;
            state.callStack.push_back(returnAddress);
        }

        if (CheckIfCall(codeAddress)) {
            readReturnAddress = true;

        } else if (CheckIfRet(codeAddress)) {
            auto returnAddress = *(PBYTE *)pExceptionInfo->ContextRecord->Esp;
            if (state.callStack.size() == 0) {
            } else if (state.callStack.back() == returnAddress) {
                state.callStack.pop_back();
            } else {
                cout << "Wrong return address:\n";
                GetStackTraceItem(cout, returnAddress);
                cout << "Last stack trace:\n";
                GetStackTrace(codeAddress, &cout);
                StopTracing();
            }
        }
    });

    if (stopTracing == false)
        SetTrapFlagContext(pExceptionInfo->ContextRecord);

    return EXCEPTION_CONTINUE_EXECUTION;
}