#pragma once
#include "winapi_config.h"

void InitializeTracerCurrentThread();

void StartTracing(PBYTE beginAddress, PBYTE endAddress);

void StopTracing();