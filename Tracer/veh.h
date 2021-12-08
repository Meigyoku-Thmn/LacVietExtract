#pragma once
#include "winapi_config.h"

extern DWORD veh_beginPageAddress; // inclusive
extern DWORD veh_endPageAddress; // exclusive
extern DWORD veh_dataAddr;
extern DWORD veh_dataSize;

LONG NTAPI Veh(PEXCEPTION_POINTERS pExceptionInfo);