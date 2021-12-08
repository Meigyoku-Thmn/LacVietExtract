// This is a precompiled header file.
// Files listed below are compiled only once, improving build performance for future builds.
// This also affects IntelliSense performance, including code completion and many code browsing features.
// However, files listed here are ALL re-compiled if any one of them is updated between builds.
// Do not add files here that you will be updating frequently as this negates the performance advantage.

#ifndef COMMON_H
#define COMMON_H

// Shut up
#pragma warning (disable: 6031)

#define DLLEXPORT __declspec(dllexport)

#include "winapi_config.h"
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

extern HMODULE g_tracerModule;

#endif
