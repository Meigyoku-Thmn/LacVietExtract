#pragma once
#include "common.h"

using namespace std;

template <typename T>
string to_hex(T val, size_t width = sizeof(T) * 2) {
    stringstream ss;
    ss << setfill('0') << setw(width) << hex << ((ULONG_PTR)val);
    return ss.str();
}

string dump_hex(UCHAR *data, size_t size) {
    stringstream ss;
    ss << setfill('0') << hex;
    for (auto i = 0; i < size; i++)
        ss << setw(2) << (data[i] | 0) << " ";
    return ss.str();
}