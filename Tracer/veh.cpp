#include "veh.h"
#include "common.h"
#include "capstone.h"
#include "csv_dump.h"
#include "string_dump.hpp"
#include "stack-trace.h"

DWORD veh_beginPageAddress; // inclusive
DWORD veh_endPageAddress; // exclusive
DWORD veh_dataAddr;
DWORD veh_dataSize;

int flowId = 0;
int lastNByte = 0;
UINT32 ordinal = 1;
DWORD lastMemAddr2 = 0;
DWORD lastCodeAddr = 0;
DWORD lastMemAddr = 0;
LONG NTAPI Veh(PEXCEPTION_POINTERS pExceptionInfo) {
    LONG exceptionCode = pExceptionInfo->ExceptionRecord->ExceptionCode;

    if (exceptionCode == EXCEPTION_GUARD_PAGE) {
        lastMemAddr = pExceptionInfo->ExceptionRecord->ExceptionInformation[1];

        if (lastMemAddr < veh_beginPageAddress || lastMemAddr >= veh_endPageAddress)
            return EXCEPTION_CONTINUE_SEARCH;

        if (lastMemAddr >= veh_dataAddr && lastMemAddr < veh_dataAddr + veh_dataSize) {
            auto context = pExceptionInfo->ContextRecord;

            if (ordinal == 1)
                PrintStackTrace(context, 20);

            auto mode = pExceptionInfo->ExceptionRecord->ExceptionInformation[0];
            auto modeStr = mode == 0 ? "Read " : "Write ";
            auto codeAddr = (DWORD)pExceptionInfo->ExceptionRecord->ExceptionAddress;
            auto memOffset = (void *)(lastMemAddr - veh_dataAddr);
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
            if (cs_disasm(capstone_handle, (uint8_t *)codeAddr, 15, (uint64_t)codeAddr, 1, &instructions) > 0) {
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