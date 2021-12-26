#include "common.h"
#include <regex>
#include "csv_dump.h"

using namespace std;

auto doubleQuoteRegex = std::regex("\"");

class CsvWriter {
    ofstream& _outputStream;
    char _delimiter;

public:
    CsvWriter(ofstream& outputStream, char delimiter = ','): _outputStream(outputStream) {
        _delimiter = delimiter;
    }

    ~CsvWriter() {
        _outputStream.close();
    }

private:
    void writeValue(const string& str) {
        bool hasSpecialChar = false;
        for (auto chr : str)
            if (chr == _delimiter || chr == '\r' || chr == '\n' || chr == '"') {
                hasSpecialChar = true;
                break;
            }
        if (hasSpecialChar)
            _outputStream << '"';
        for (auto chr : str) {
            if (chr == '"')
                _outputStream << "\"\"";
            else
                _outputStream << chr;
        }
        if (hasSpecialChar)
            _outputStream << '"';
    }

public:
    void writeRow(const vector<string>& strings) {
        for (size_t i = 0; i < strings.size(); i++) {
            writeValue(strings[i]);
            if (i + 1 < strings.size())
                _outputStream << _delimiter;
        }
        _outputStream << "\n";
    }
};

ofstream *logOutputStream = nullptr;
CsvWriter *csvWriter = nullptr;
bool csvWriterInitialized = false;
void InitializeCSVWriter(const wchar_t *logPath) {
    if (csvWriterInitialized)
        return;

    logOutputStream = new ofstream(logPath, ios_base::out | ios_base::binary);
    csvWriter = new CsvWriter(*logOutputStream, ';');

    WriteCsvRecord(
        "Ordinal", "Type", "nByte", "Mnemonic", "Operands",
        "MemoryOffset", "Data", "CodeAddress", "FlowId",
        "ESI", "EDI", "EAX", "EBX", "ECX", "EDX", "ESP", "EBP",
        "StackTrace"
    );

    csvWriterInitialized = true;
}

void DisposeCSVWriter() {
    delete csvWriter;
    delete logOutputStream;
    csvWriter = nullptr;
    logOutputStream = nullptr;
}

void WriteCsvRecord(
    string &&ordinal, string &&type, string &&nByte, string &&mnemonic, string &&operands,
    string &&memoryOffset, string &&data, string &&codeAddress, string &&flowId,
    string &&esi, string &&edi, string &&eax, string &&ebx, string &&ecx, string &&edx, string &&esp, string &&ebp,
    string &&stackTrace
) {
    csvWriter->writeRow(vector<string> {
        ordinal, type, nByte, mnemonic, operands, memoryOffset, data, codeAddress, flowId,
            esi, edi, eax, ebx, ecx, edx, esp, ebp, stackTrace
    });
}