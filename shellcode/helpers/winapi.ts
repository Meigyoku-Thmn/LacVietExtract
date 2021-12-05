import { arr, createCStruct, pointer, StructInstance, uint32 } from './frida-struct';

function getLastError<T extends NativeFunctionReturnValue>(lastResult: SystemFunctionResult<T>): number {
   if (Process.platform === 'windows')
      return (lastResult as WindowsSystemFunctionResult<T>).lastError;
   return (lastResult as UnixSystemFunctionResult<T>).errno;
}

const Kernel32 = Module.load('Kernel32.dll');

export const GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
export const GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;

const _GetModuleHandleExA = new SystemFunction(
   Kernel32.getExportByName('GetModuleHandleExA'),
   'bool', ['uint32', 'uint32', 'pointer'],
   'stdcall'
);
export function GetModuleHandleExA(dwFlags: number, lpModuleName: string | number): NativePointer {
   const phModule = Memory.alloc(Process.pointerSize);
   const result = _GetModuleHandleExA(
      dwFlags,
      typeof lpModuleName === 'string' ? Memory.allocUtf8String(lpModuleName).toUInt32() : lpModuleName,
      phModule
   );
   if (result.value == 0)
      throw Error(`GetModuleHandleExA failed with error code ${getLastError(result)}.`);
   return phModule.readPointer();
}