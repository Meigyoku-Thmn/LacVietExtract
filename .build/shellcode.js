(() => {
    const defines = {};
    const entry = [null];
    function define(name, dependencies, factory) {
        defines[name] = { dependencies, factory };
        entry[0] = name;
    }
    define("require", ["exports"], (exports) => {
        Object.defineProperty(exports, "__cjsModule", { value: true });
        Object.defineProperty(exports, "default", { value: (name) => resolve(name) });
    });
    define("helpers/utils", ["require", "exports"], function (require, exports) {
        "use strict";
        Object.defineProperty(exports, "__esModule", { value: true });
        exports.printBackTrace = exports.sendCommand = exports.wrapCdeclInStdcall = exports.wstr = exports.str = exports.keep = void 0;
        const keeper = [];
        /**
         * Keep data from being collected by garbage collector.
         */
        function keep(...data) {
            data.forEach(datum => keeper.push(datum));
            return data;
        }
        exports.keep = keep;
        /**
         * Create utf8 string.
         */
        function str(text) {
            return Memory.allocUtf8String(text);
        }
        exports.str = str;
        /**
         * Create utf16 string
         */
        function wstr(text) {
            return Memory.allocUtf16String(text);
        }
        exports.wstr = wstr;
        /**
         * Wrap a cdecl function call in a stdcall function.
         */
        function wrapCdeclInStdcall(func, numArgs) {
            if (Process.arch !== 'ia32' || Process.platform !== 'windows')
                throw new Error('This function can only work on Windows 32-bit!');
            if (numArgs > 127 || numArgs < 0)
                throw new Error('numArgs is out of range [0, 127]');
            const wrapperFunc = Memory.alloc(Process.pageSize);
            Memory.patchCode(wrapperFunc, Process.pageSize, code => {
                const cw = new X86Writer(code, { pc: wrapperFunc });
                for (let i = 0; i < numArgs; i++) {
                    cw.putBytes(new Uint8Array([0xFF, 0x74, 0x24, 4 * numArgs]).buffer);
                } // push   DWORD PTR [esp + 4 * numArgs] -- push arguments for cdecl call
                cw.putCallAddress(func); // call   func                          -- call the cdecl function
                cw.putAddRegImm('esp', 4 * numArgs); // add    esp, 4 * numArg               -- clear the pushed arguments
                cw.putRetImm(4 * numArgs); // ret    4 * numArgs                   -- return and clear all arguments
                cw.flush();
            });
            keep(func);
            return wrapperFunc;
        }
        exports.wrapCdeclInStdcall = wrapCdeclInStdcall;
        function sendCommand(command) {
            let message;
            let data;
            send({ command });
            recv(command, (_payload, _data) => { message = _payload.message; data = _data; }).wait();
            return { message, data };
        }
        exports.sendCommand = sendCommand;
        function printBackTrace(context, mode, callback) {
            callback !== null && callback !== void 0 ? callback : (callback = e => console.log(`${e.address}\t${e.moduleName}!${e.name}`));
            Thread.backtrace(context, mode)
                .map(DebugSymbol.fromAddress)
                .forEach(callback);
        }
        exports.printBackTrace = printBackTrace;
    });
    define("helpers/frida-struct", ["require", "exports"], function (require, exports) {
        "use strict";
        Object.defineProperty(exports, "__esModule", { value: true });
        exports.createCStruct = exports.arr = exports.wtext = exports.utext = exports.text = exports.bool8 = exports.bool32 = exports.int8 = exports.uint8 = exports.int16 = exports.uint16 = exports.int32 = exports.uint32 = exports.int64 = exports.uint64 = exports.pointer = void 0;
        const PlainNumberTypes = [
            'int8', 'uint8', 'int16', 'uint16', 'int32', 'uint32', 'float', 'double'
        ];
        const BigNumberTypes = ['int64', 'uint64'];
        const BooleanTypes = ['bool32', 'bool8'];
        const StringTypes = ['str', 'wstr', 'ustr'];
        class ScalarArray {
            constructor(type, sample, address, typeInfo, size) {
                const elementSize = typeInfo.size.call(null);
                this.length = size / elementSize;
                for (let i = 0; i < this.length; i++) {
                    const fieldContext = {
                        offset: elementSize * i,
                        get ptr() { return address; },
                    };
                    Object.defineProperty(this, i, {
                        enumerable: true,
                        get: typeInfo.get.bind(fieldContext),
                        set: typeInfo.set.bind(fieldContext),
                    });
                }
            }
            [Symbol.iterator]() {
                let index = 0;
                let instance = this;
                return {
                    next() {
                        if (index >= instance.length)
                            return { value: undefined, done: true };
                        else
                            return { value: instance[index++], done: false };
                    }
                };
            }
        }
        function wrapArray(type, address, typeInfo, size) {
            if (PlainNumberTypes.indexOf(type) != -1)
                return new ScalarArray(type, 0, address, typeInfo, size);
            if (BigNumberTypes.indexOf(type) != -1)
                return new ScalarArray(type, BigInt(0), address, typeInfo, size);
            if (BooleanTypes.indexOf(type) != -1)
                return new ScalarArray(type, false, address, typeInfo, size);
            if (type == 'pointer')
                return new ScalarArray(type, ptr(0), address, typeInfo, size);
        }
        class StructArray {
            constructor(arr) {
                this.length = arr.length;
                for (let i = 0; i < this.length; i++) {
                    const _i = i;
                    Object.defineProperty(this, i, {
                        enumerable: true,
                        get: () => arr[_i],
                    });
                }
            }
            [Symbol.iterator]() {
                let index = 0;
                let instance = this;
                return {
                    next() {
                        if (index >= instance.length)
                            return { value: undefined, done: true };
                        else
                            return { value: instance[index++], done: false };
                    }
                };
            }
        }
        exports.pointer = { name: 'pointer' };
        exports.uint64 = { name: 'uint64' };
        exports.int64 = { name: 'int64' };
        exports.uint32 = { name: 'uint32' };
        exports.int32 = { name: 'int32' };
        exports.uint16 = { name: 'uint16' };
        exports.int16 = { name: 'int16' };
        exports.uint8 = { name: 'uint8' };
        exports.int8 = { name: 'int8' };
        exports.bool32 = { name: 'bool32' };
        exports.bool8 = { name: 'bool8' };
        const text = (size) => ({ name: 'str', length: size });
        exports.text = text;
        const utext = (size) => ({ name: 'ustr', length: size });
        exports.utext = utext;
        const wtext = (length) => ({ name: 'wstr', length });
        exports.wtext = wtext;
        const arr = (type, length) => [type, length];
        exports.arr = arr;
        const ScalarTypeMap = new Map([
            ['bool32', {
                    size: () => 4,
                    get() { return !!this.ptr.add(this.offset).readU32(); },
                    set(value) { return this.ptr.add(this.offset).writeU32(value ? 1 : 0); }
                }],
            ['bool8', {
                    size: () => 1,
                    get() { return !!this.ptr.add(this.offset).readU8(); },
                    set(value) { return this.ptr.add(this.offset).writeU8(value ? 1 : 0); }
                }],
            ['uint8', {
                    size: () => 1,
                    get() { return this.ptr.add(this.offset).readU8(); },
                    set(value) { return this.ptr.add(this.offset).writeU8(value); }
                }],
            ['int8', {
                    size: () => 1,
                    get() { return this.ptr.add(this.offset).readS8(); },
                    set(value) { return this.ptr.add(this.offset).writeS8(value); }
                }],
            ['uint16', {
                    size: () => 2,
                    get() { return this.ptr.add(this.offset).readU16(); },
                    set(value) { return this.ptr.add(this.offset).writeU16(value); }
                }],
            ['int16', {
                    size: () => 2,
                    get() { return this.ptr.add(this.offset).readS16(); },
                    set(value) { return this.ptr.add(this.offset).writeS16(value); }
                }],
            ['uint32', {
                    size: () => 4,
                    get() { return this.ptr.add(this.offset).readU32(); },
                    set(value) { return this.ptr.add(this.offset).writeU32(value); }
                }],
            ['int32', {
                    size: () => 4,
                    get() { return this.ptr.add(this.offset).readS32(); },
                    set(value) { return this.ptr.add(this.offset).writeS32(value); }
                }],
            ['uint64', {
                    size: () => 8,
                    get() { return this.ptr.add(this.offset).readU64(); },
                    set(value) { return this.ptr.add(this.offset).writeU64(value); }
                }],
            ['int64', {
                    size: () => 8,
                    get() { return this.ptr.add(this.offset).readS64(); },
                    set(value) { return this.ptr.add(this.offset).writeS64(value); }
                }],
            ['float', {
                    size: () => 4,
                    get() { return this.ptr.add(this.offset).readFloat(); },
                    set(value) { return this.ptr.add(this.offset).writeFloat(value); }
                }],
            ['double', {
                    size: () => 8,
                    get() { return this.ptr.add(this.offset).readDouble(); },
                    set(value) { return this.ptr.add(this.offset).writeDouble(value); }
                }],
            // this can be anything
            ['pointer', {
                    size: () => Process.pointerSize,
                    get() { return this.ptr.add(this.offset).readPointer(); },
                    set(value) { return this.ptr.add(this.offset).writePointer(value); }
                }],
            // fixed-length string
            ['str', {
                    size() { return this.length; },
                    get() { return this.ptr.add(this.offset).readAnsiString(this.length); },
                    set(value) { return this.ptr.add(this.offset).writeAnsiString(value); }
                }],
            ['ustr', {
                    size() { return this.length; },
                    get() { return this.ptr.add(this.offset).readUtf8String(this.length); },
                    set(value) { return this.ptr.add(this.offset).writeUtf8String(value); }
                }],
            ['wstr', {
                    size() { return this.length * 2; },
                    get() { return this.ptr.add(this.offset).readUtf16String(this.length); },
                    set(value) { return this.ptr.add(this.offset).writeUtf16String(value); }
                }],
        ]);
        function makeStructWrapper(memoryContext, protoObj) {
            let maxAlignSize = 0;
            let totalSize = 0;
            let offset = memoryContext.offset;
            const wrapper = Object.entries(protoObj).reduce((wrapper, [fieldName, fieldInfo]) => {
                const typeInfo = ScalarTypeMap.get(fieldInfo.name);
                if (typeInfo != null) {
                    const scalarFieldInfo = fieldInfo;
                    const fieldContext = {
                        offset: null,
                        get ptr() { return memoryContext.structPtr; },
                        length: scalarFieldInfo.name === 'str' || scalarFieldInfo.name === 'wstr' ? scalarFieldInfo.length : 0,
                    };
                    const size = typeInfo.size.call(fieldContext);
                    const alignSize = size;
                    const padding = (alignSize - totalSize % alignSize) % alignSize;
                    totalSize += padding;
                    memoryContext.offset += padding;
                    fieldContext.offset = memoryContext.offset;
                    Object.defineProperty(wrapper, fieldName, {
                        enumerable: true,
                        get: typeInfo.get.bind(fieldContext),
                        set: typeInfo.set.bind(fieldContext),
                    });
                    if (maxAlignSize < alignSize)
                        maxAlignSize = alignSize;
                    totalSize += size;
                    memoryContext.offset += size;
                }
                else if (Array.isArray(fieldInfo)) {
                    const sFieldInfo = fieldInfo[0];
                    const length = fieldInfo[1];
                    const sTypeInfo = ScalarTypeMap.get(sFieldInfo.name);
                    if (sTypeInfo != null) {
                        const sScalarFieldInfo = sFieldInfo;
                        if (StringTypes.indexOf(sScalarFieldInfo.name) != -1)
                            throw new Error('String types are not supported in array for now.');
                        const alignSize = sTypeInfo.size.call(null);
                        const size = length * alignSize;
                        const padding = (alignSize - totalSize % alignSize) % alignSize;
                        totalSize += padding;
                        memoryContext.offset += padding;
                        let array;
                        Object.defineProperty(wrapper, fieldName, {
                            enumerable: true,
                            get: () => array !== null && array !== void 0 ? array : (array = wrapArray(sScalarFieldInfo.name, memoryContext.structPtr.add(memoryContext.offset), sTypeInfo, size)),
                        });
                        if (maxAlignSize < alignSize)
                            maxAlignSize = alignSize;
                        totalSize += size;
                        memoryContext.offset += size;
                    }
                    else if (Array.isArray(sFieldInfo)) {
                        throw new Error("Nested array is not supported for now.");
                    }
                    else if (typeof (sFieldInfo) == 'object') {
                        const structProto = sFieldInfo;
                        const _offset = memoryContext.offset;
                        const [, sAlignSize, elementSize] = makeStructWrapper(memoryContext, structProto); // probing call
                        const size = length * elementSize;
                        memoryContext.offset = _offset; // rollback offset
                        const padding = (sAlignSize - totalSize % sAlignSize) % sAlignSize;
                        totalSize += padding;
                        memoryContext.offset += padding; // calculate the correct offset
                        const sWrappers = [];
                        for (let i = 0; i < length; i++) {
                            const [sWrapper] = makeStructWrapper(memoryContext, structProto); // real call
                            sWrappers.push(sWrapper);
                        }
                        let array;
                        Object.defineProperty(wrapper, fieldName, {
                            enumerable: true,
                            get: () => array !== null && array !== void 0 ? array : (array = new StructArray(sWrappers)),
                        });
                        if (maxAlignSize < sAlignSize)
                            maxAlignSize = sAlignSize;
                        totalSize += size;
                    }
                    else
                        new Error(`Unknown struct's field type: [${fieldName}: "${fieldInfo}"]`);
                }
                else if (typeof (fieldInfo) === 'object') {
                    const structProto = fieldInfo;
                    const _offset = memoryContext.offset;
                    const [, sAlignSize, sSize] = makeStructWrapper(memoryContext, structProto); // probing call
                    memoryContext.offset = _offset; // rollback offset
                    const padding = (sAlignSize - totalSize % sAlignSize) % sAlignSize;
                    totalSize += padding;
                    memoryContext.offset += padding; // calculate the correct offset
                    const [sWrapper] = makeStructWrapper(memoryContext, structProto); // real call
                    Object.defineProperty(wrapper, fieldName, {
                        enumerable: true,
                        get: () => sWrapper,
                    });
                    if (maxAlignSize < sAlignSize)
                        maxAlignSize = sAlignSize;
                    totalSize += sSize;
                }
                else
                    new Error(`Unknown struct's field type: [${fieldName}: "${fieldInfo}"]`);
                return wrapper;
            }, {});
            const padding = (maxAlignSize - totalSize % maxAlignSize) % maxAlignSize;
            totalSize += padding;
            memoryContext.offset += padding;
            wrapper.getPtr = () => memoryContext.structPtr.add(offset);
            wrapper.getSize = () => totalSize;
            return [wrapper, maxAlignSize, totalSize];
        }
        function createCStruct(protoObj, address) {
            const memoryContext = {
                structPtr: address,
                offset: 0,
            };
            const [wrapper, , size] = makeStructWrapper(memoryContext, protoObj);
            if (memoryContext.structPtr === undefined)
                memoryContext.structPtr = Memory.alloc(size);
            wrapper.setPtr = (ptr) => memoryContext.structPtr = ptr;
            return wrapper;
        }
        exports.createCStruct = createCStruct;
    });
    define("helpers/winapi", ["require", "exports", "helpers/frida-struct"], function (require, exports, frida_struct_1) {
        "use strict";
        Object.defineProperty(exports, "__esModule", { value: true });
        exports.VirtualProtect = exports._VirtualProtect = exports.VirtualQuery = exports.PAGE_GUARD = exports.PAGE_READWRITE = exports.AddVectoredExceptionHandler = exports.EXCEPTION_POINTERS = exports.EXCEPTION_RECORD = exports.EXCEPTION_CONTINUE_EXECUTION = exports.EXCEPTION_CONTINUE_SEARCH = exports.EXCEPTION_GUARD_PAGE = exports.STATUS_SINGLE_STEP = exports.GetModuleHandleExA = exports.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = exports.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = void 0;
        function getLastError(lastResult) {
            if (Process.platform === 'windows')
                return lastResult.lastError;
            return lastResult.errno;
        }
        const Kernel32 = Module.load('Kernel32.dll');
        exports.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
        exports.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;
        const _GetModuleHandleExA = new SystemFunction(Kernel32.getExportByName('GetModuleHandleExA'), 'bool', ['uint32', 'uint32', 'pointer'], 'stdcall');
        function GetModuleHandleExA(dwFlags, lpModuleName) {
            const phModule = Memory.alloc(Process.pointerSize);
            const result = _GetModuleHandleExA(dwFlags, typeof lpModuleName === 'string' ? Memory.allocUtf8String(lpModuleName).toUInt32() : lpModuleName, phModule);
            if (result.value == 0)
                throw Error(`GetModuleHandleExA failed with error code ${getLastError(result)}.`);
            return phModule.readPointer();
        }
        exports.GetModuleHandleExA = GetModuleHandleExA;
        exports.STATUS_SINGLE_STEP = 0x80000004;
        exports.EXCEPTION_GUARD_PAGE = 0x80000001;
        exports.EXCEPTION_CONTINUE_SEARCH = 0;
        exports.EXCEPTION_CONTINUE_EXECUTION = -1;
        const EXCEPTION_MAXIMUM_PARAMETERS = 15;
        exports.EXCEPTION_RECORD = {
            ExceptionCode: frida_struct_1.uint32,
            ExceptionFlags: frida_struct_1.uint32,
            ExceptionRecord: frida_struct_1.pointer,
            ExceptionAddress: frida_struct_1.pointer,
            NumberParameters: frida_struct_1.uint32,
            ExceptionInformation: (0, frida_struct_1.arr)(frida_struct_1.uint32, EXCEPTION_MAXIMUM_PARAMETERS),
        };
        exports.EXCEPTION_POINTERS = {
            ExceptionRecord: frida_struct_1.pointer,
            ContextRecord: frida_struct_1.pointer,
        };
        const _AddVectoredExceptionHandler = new SystemFunction(Kernel32.getExportByName('AddVectoredExceptionHandler'), 'uint32', ['uint32', 'pointer'], 'stdcall');
        function AddVectoredExceptionHandler(First, Handler) {
            const result = _AddVectoredExceptionHandler(First, Handler);
            if (result.value == 0)
                throw Error(`AddVectoredExceptionHandler failed with error code ${getLastError(result)}.`);
            return result.value;
        }
        exports.AddVectoredExceptionHandler = AddVectoredExceptionHandler;
        exports.PAGE_READWRITE = 0x04;
        exports.PAGE_GUARD = 0x100;
        const MEMORY_BASIC_INFORMATION = {
            BaseAddress: frida_struct_1.pointer,
            AllocationBase: frida_struct_1.pointer,
            AllocationProtect: frida_struct_1.uint32,
            RegionSize: frida_struct_1.uint32,
            State: frida_struct_1.uint32,
            Protect: frida_struct_1.uint32,
            Type: frida_struct_1.uint32,
        };
        const _VirtualQuery = new SystemFunction(Kernel32.getExportByName('VirtualQuery'), 'uint32', ['pointer', 'pointer', 'uint32'], 'stdcall');
        function VirtualQuery(address) {
            const mbi = (0, frida_struct_1.createCStruct)(MEMORY_BASIC_INFORMATION);
            const result = _VirtualQuery(address, mbi.getPtr(), mbi.getSize());
            if (result.value == 0)
                throw Error(`VirtualQuery failed with error code ${getLastError(result)}.`);
            return mbi;
        }
        exports.VirtualQuery = VirtualQuery;
        exports._VirtualProtect = new SystemFunction(Kernel32.getExportByName('VirtualProtect'), 'uint32', ['pointer', 'uint32', 'uint32', 'pointer'], 'stdcall');
        function VirtualProtect(address, size, newProtect) {
            const oldProtectPtr = Memory.alloc(4);
            const result = (0, exports._VirtualProtect)(address, size, newProtect, oldProtectPtr);
            if (result.value == 0)
                throw Error(`VirtualProtect failed with error code ${getLastError(result)}.`);
            return oldProtectPtr.readU32();
        }
        exports.VirtualProtect = VirtualProtect;
    });
    define("shellcode", ["require", "exports", "helpers/utils", "helpers/winapi"], function (require, exports, utils_1, winapi_1) {
        "use strict";
        Object.defineProperty(exports, "__esModule", { value: true });
        (0, utils_1.sendCommand)('Ping');
        const logFile = new File("D:/Draftbook/LacVietHack/log2.txt", "wb");
        const RefBaseOffset = 0xB00000;
        let lineNumber = 0;
        function getLineNumber() {
            return lineNumber++;
        }
        function logWriteLine(str) {
            const lineNumber = getLineNumber();
            logFile.write(lineNumber.toString());
            logFile.write(": ");
            logFile.write(str);
            logFile.write('\n');
            return lineNumber;
        }
        rpc.exports = {
            closeLog() {
                logFile.close();
            }
        };
        let incrementalId = 0;
        function getId() {
            return incrementalId++;
        }
        const FileHandlePool = new Map();
        const TargetFileNames = [
            'LVFV2000.DIT'
        ].map(e => e.toLowerCase());
        const mtdDataLIBName = 'mtdDataLIB.dll';
        const mtdDataLIB = Module.load(mtdDataLIBName);
        function norm_mtdDataLIB_addr(addr) {
            return addr.sub(mtdDataLIB.base).add(RefBaseOffset);
        }
        function logStackTrace(context) {
            (0, utils_1.printBackTrace)(context, Backtracer.FUZZY, e => {
                logWriteLine(`${e.address}\t${e.moduleName}!${e.name}`);
                const flags = winapi_1.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | winapi_1.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
                try {
                    const hModule = (0, winapi_1.GetModuleHandleExA)(flags, e.address.toUInt32());
                    if (hModule.toUInt32() == mtdDataLIB.base.toUInt32())
                        logWriteLine(`  Guess VA in mtdDataLIB.dll ${norm_mtdDataLIB_addr(e.address)}`);
                }
                catch (_a) { }
            });
        }
        (function patchCFileDisableBuffering() {
            var cFileCtor = Module.findBaseAddress(mtdDataLIBName).add(0x00BC32F4 - RefBaseOffset);
            Interceptor.attach(cFileCtor, {
                onEnter(args) {
                    args[1].or(0x10000);
                },
            });
        })();
        (function trackDecompressionRoutine() {
            var func = Module.findBaseAddress(mtdDataLIBName).add(0x00B01D10 - RefBaseOffset);
            Interceptor.attach(func, {
                onEnter(args) {
                    this.blockName = args[0].readCString();
                    this.outputObj = args[1];
                },
                onLeave(rs) {
                    if (this.blockName != 'Content0')
                        return;
                    const outputObj = this.outputObj;
                    const buffer = outputObj.readPointer();
                    const bufferSize = rs.toInt32();
                    const beginPageAddress = (0, winapi_1.VirtualQuery)(buffer).BaseAddress;
                    const endPageAddress = (0, winapi_1.VirtualQuery)(buffer.add(bufferSize - 1)).BaseAddress.add(Process.pageSize);
                    const cm = new CModule(`
                #include <stdio.h>
                __attribute__((stdcall)) extern int VirtualProtect(
                   void *lpAddress,
                   unsigned int dwSize,
                   unsigned int flNewProtect,
                   unsigned int *lpflOldProtect
                );
                struct EXCEPTION_POINTERS {
                   struct EXCEPTION_RECORD *ExceptionRecord;
                   int ContextRecord; // pointer
                };
                #define EXCEPTION_MAXIMUM_PARAMETERS 15
                struct EXCEPTION_RECORD {
                   unsigned int ExceptionCode;
                   unsigned int ExceptionFlags;
                   struct EXCEPTION_RECORD *ExceptionRecord;
                   void *ExceptionAddress;
                   unsigned int NumberParameters;
                   unsigned int ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
                };
                #define EXCEPTION_GUARD_PAGE 0x80000001
                #define STATUS_SINGLE_STEP 0x80000004
                #define EXCEPTION_CONTINUE_SEARCH 0
                #define EXCEPTION_CONTINUE_EXECUTION -1
                #define PAGE_READWRITE 0x04
                #define PAGE_GUARD 0x100
                void *beginPageAddress = (void *)${beginPageAddress};
                void *endPageAddress = (void *)${endPageAddress};
                const int arrSize = ${bufferSize};
                char *arr = (char *)${buffer};
                #define NULL ((void *)0)
                void *currentMemoryAddress = NULL;
                __attribute__((stdcall)) int veh(struct EXCEPTION_POINTERS *pExceptionInfo) {
                   int exceptionCode = pExceptionInfo->ExceptionRecord->ExceptionCode;
    
                   if (exceptionCode == EXCEPTION_GUARD_PAGE) {
                      currentMemoryAddress = (void *)pExceptionInfo->ExceptionRecord->ExceptionInformation[1];
                      return EXCEPTION_CONTINUE_EXECUTION;
                      
                      if (currentMemoryAddress < beginPageAddress || currentMemoryAddress >= endPageAddress)
                         return EXCEPTION_CONTINUE_SEARCH;
             
                      if (currentMemoryAddress >= arr && currentMemoryAddress < arr + arrSize) {
                         unsigned int mode = pExceptionInfo->ExceptionRecord->ExceptionInformation[0];
                         char *modeStr =
                            mode == 0 ? "Read" :
                            mode == 1 ? "Write" : "Execute";
             
                      }
             
                      // Set up for STATUS_SINGLE_STEP
                      // *(unsigned int *)(pExceptionInfo->ContextRecord + 192) |= 0x00000100;
             
                      return EXCEPTION_CONTINUE_EXECUTION;
               
                   } else if (exceptionCode == STATUS_SINGLE_STEP) {
                      if (currentMemoryAddress == NULL)
                         return EXCEPTION_CONTINUE_SEARCH;
             
                      unsigned int oldProtect;
                      VirtualProtect(currentMemoryAddress, 1, PAGE_READWRITE | PAGE_GUARD, &oldProtect);
                      currentMemoryAddress = NULL;
             
                      // Remove flag for STATUS_SINGLE_STEP
                      *(unsigned int *)(pExceptionInfo->ContextRecord + 192) &= ~0x00000100;
             
                      return EXCEPTION_CONTINUE_EXECUTION;           
                   }           
                   return EXCEPTION_CONTINUE_SEARCH;
                }
             `, { VirtualProtect: winapi_1._VirtualProtect });
                    const Handler = cm['_veh@4'];
                    (0, winapi_1.AddVectoredExceptionHandler)(1, Handler);
                    // logWriteLine(`Memory access: ${modeStr} buffer+${offset} from ${instrAddr} ${nByte} byte(s)`);
                    (0, winapi_1.VirtualProtect)(buffer, bufferSize, winapi_1.PAGE_READWRITE | winapi_1.PAGE_GUARD);
                }
            });
        })();
        (function trackCreateFile() {
            var pCreateFileA = Module.getExportByName("kernel32.dll", 'CreateFileA');
            Interceptor.attach(pCreateFileA, {
                onEnter(args) {
                    const fileName = args[0].readCString().toLowerCase();
                    this.fileName = TargetFileNames.find(name => fileName.endsWith(name));
                    if (this.fileName != null) {
                        this.fileId = getId();
                        logWriteLine(`CreateFileA: (${this.fileId}) ${this.fileName}`);
                    }
                },
                onLeave(rs) {
                    const retValue = rs.toInt32();
                    if (retValue === -1)
                        return;
                    if (this.fileName == null)
                        return;
                    FileHandlePool.set(retValue, { fileName: this.fileName, id: this.fileId });
                }
            });
        })();
        (function trackCloseHandle() {
            var pCloseHandle = Module.getExportByName("kernel32.dll", "CloseHandle");
            Interceptor.attach(pCloseHandle, {
                onEnter(args) {
                    const fileHandle = args[0].toUInt32();
                    FileHandlePool.delete(fileHandle);
                },
            });
        })();
        (function trackReadFile() {
            var pReadFile = Module.getExportByName("kernel32.dll", "ReadFile");
            let dumpString = false;
            Interceptor.attach(pReadFile, {
                onEnter(args) {
                    const fileHandle = args[0].toUInt32();
                    const file = FileHandlePool.get(fileHandle);
                    if (file == null)
                        return;
                    const nToRead = args[2].toUInt32();
                    const lpBuffer = args[1];
                    this.lpBuffer = lpBuffer;
                    this.fileHandle = fileHandle;
                    this.nToRead = nToRead;
                },
                onLeave(rs) {
                    const fileHandle = this.fileHandle;
                    if (fileHandle == null)
                        return;
                    const retValue = rs.toInt32();
                    if (retValue === 0)
                        return;
                    const lpBuffer = this.lpBuffer;
                    const file = FileHandlePool.get(fileHandle);
                    const nToRead = this.nToRead;
                    const hexDump = new Uint8Array(ArrayBuffer.wrap(lpBuffer, nToRead).slice(0, 20))
                        .reduce((acc, e) => acc += ' ' + e.toString(16).padStart(2, '0'), "");
                    if (dumpString === false)
                        var lineNumber = logWriteLine(`ReadFile: (${file.id}) 0x${nToRead.toString(16)} byte(s) ${file.fileName} ${hexDump}`);
                    else {
                        const strDump = lpBuffer.readAnsiString(nToRead);
                        var lineNumber = logWriteLine(`ReadFile: (${file.id}) 0x${nToRead.toString(16)} byte(s) ${file.fileName} ${hexDump} (${strDump})`);
                    }
                    if (lineNumber == 1902)
                        logStackTrace(this.context);
                    if (nToRead === 1)
                        dumpString = true;
                    else
                        dumpString = false;
                }
            });
        })();
        (function trackSetFilePointer() {
            const MoveMethod = {
                0: 'FILE_BEGIN',
                1: 'FILE_CURRENT',
                2: 'FILE_END',
            };
            var pSetFilePointer = Module.getExportByName("kernel32.dll", "SetFilePointer");
            Interceptor.attach(pSetFilePointer, {
                onEnter(args) {
                    const fileHandle = args[0].toUInt32();
                    const file = FileHandlePool.get(fileHandle);
                    if (file == null)
                        return;
                    const lDistanceToMove = args[1].toUInt32();
                    const dwMoveMethod = MoveMethod[args[3].toUInt32()];
                    logWriteLine(`SetFilePointer: (${file.id}) offset 0x${lDistanceToMove.toString(16)} byte(s) from ${dwMoveMethod} ${file.fileName}`);
                },
            });
        })();
        console.log("Done setup");
    });
    //# sourceMappingURL=shellcode.js.map
    'marker:resolver';

    function get_define(name) {
        if (defines[name]) {
            return defines[name];
        }
        else if (defines[name + '/index']) {
            return defines[name + '/index'];
        }
        else {
            const dependencies = ['exports'];
            const factory = (exports) => {
                try {
                    Object.defineProperty(exports, "__cjsModule", { value: true });
                    Object.defineProperty(exports, "default", { value: require(name) });
                }
                catch (_a) {
                    throw Error(['module "', name, '" not found.'].join(''));
                }
            };
            return { dependencies, factory };
        }
    }
    const instances = {};
    function resolve(name) {
        if (instances[name]) {
            return instances[name];
        }
        if (name === 'exports') {
            return {};
        }
        const define = get_define(name);
        instances[name] = {};
        const dependencies = define.dependencies.map(name => resolve(name));
        define.factory(...dependencies);
        const exports = dependencies[define.dependencies.indexOf('exports')];
        instances[name] = (exports['__cjsModule']) ? exports.default : exports;
        return instances[name];
    }
    if (entry[0] !== null) {
        return resolve(entry[0]);
    }
})();