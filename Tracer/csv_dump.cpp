#include "common.h"
#include "csv_dump.h"

using namespace std;

ofstream *logStream = nullptr;
CsvWriter *csvWriter = nullptr;
bool csvWriterInitialized = false;

void InitializeCSVWriter(const wchar_t *logPath) {
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

void DisposeCSVWriter() {
    delete csvWriter;
    delete logStream;
}

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