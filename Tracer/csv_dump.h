#pragma once

#define STRING std::string

void InitializeCSVWriter(const wchar_t *logPath);

void DisposeCSVWriter();

void WriteCsvRecord(
    STRING &&ordinal, STRING &&type, STRING &&nByte, STRING &&mnemonic, STRING &&operands,
    STRING &&memoryOffset, STRING &&data, STRING &&codeAddress, STRING &&flowId,
    STRING &&esi, STRING &&edi, STRING &&eax, STRING &&ebx, STRING &&ecx, STRING &&edx, STRING &&esp, STRING &&ebp,
    STRING &&stackTrace
);

#undef STRING