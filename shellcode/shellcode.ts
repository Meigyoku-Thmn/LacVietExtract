import { printBackTrace, sendCommand } from "./helpers/utils";
import {
   GetModuleHandleExA,
   GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
   GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
} from "./helpers/winapi";

sendCommand('Ping');

const logFile = new File("D:/Draftbook/LacVietHack/logs/log2.txt" as any, "wb") as any;

const RefBaseOffset = 0xB00000;

let lineNumber = 0;
function getLineNumber(noCount = false) {
   if (noCount)
      return lineNumber
   return lineNumber++;
}

function logWriteLine(str: string, noCount = false) {
   const lineNumber = getLineNumber(noCount);
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

const FileHandlePool = new Map<number, {
   fileName: string;
   id: number;
}>();

const TargetFileNames = [
   'image.lvz'
].map(e => e.toLowerCase());
const mtdDataLIBName = 'mtdDataLIB.dll';
const mainModuleName = 'mtd2012.exe';

const mtdDataLIB = Module.load(mtdDataLIBName);
const mainModule = Module.load(mainModuleName);

function norm_mtdDataLIB_addr(addr: NativePointer): NativePointer {
   return addr.sub(mtdDataLIB.base).add(RefBaseOffset)
}
function norm_addr(addr: NativePointer): NativePointer {
   return addr.sub(mainModule.base).add(RefBaseOffset)
}

function logStackTrace(context: CpuContext) {
   printBackTrace(context, Backtracer.FUZZY, e => {
      logWriteLine(`${norm_addr(e.address)}\t${e.moduleName}!${e.name}`, true);
      const flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
      try {
         const hModule = GetModuleHandleExA(flags, e.address.toUInt32());
         if (hModule.toUInt32() == mtdDataLIB.base.toUInt32())
            logWriteLine(`  Guess VA in mtdDataLIB.dll ${norm_mtdDataLIB_addr(e.address)}`, true);
      } catch { }
   });
}

(function patchCFileDisableBuffering() {
   var dllcFileCtor = Module.findBaseAddress(mtdDataLIBName).add(0x00BC3484 - RefBaseOffset);
   var cFileCtor = Module.findBaseAddress(mainModuleName).add(0x00C0550A - RefBaseOffset);
   Interceptor.attach(dllcFileCtor, {
      onEnter(args) {
         args[1].or(0x10000);
      },
   });
   Interceptor.attach(cFileCtor, {
      onEnter(args) {
         args[1].or(0x10000);
      },
   })
})();

(function trackCreateFile() {
   var pCreateFileA = Module.getExportByName("kernel32.dll", 'CreateFileA');
   Interceptor.attach(pCreateFileA, {
      onEnter(args) {
         const fileName = args[0].readCString().toLowerCase();
         console.log(fileName);
         this.fileName = TargetFileNames.find(name => fileName.endsWith(name))
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
         FileHandlePool.set(retValue, { fileName: this.fileName, id: this.fileId })
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
         const fileHandle = this.fileHandle as number;
         if (fileHandle == null)
            return;
         const retValue = rs.toInt32();
         if (retValue === 0)
            return;

         const lpBuffer = this.lpBuffer as NativePointer;
         const file = FileHandlePool.get(fileHandle);
         const nToRead = this.nToRead as number;

         const hexDump = new Uint8Array(ArrayBuffer.wrap(lpBuffer, nToRead).slice(0, 20))
            .reduce((acc, e) => acc += ' ' + e.toString(16).padStart(2, '0'), "");
         if (dumpString === false)
            var lineNumber = logWriteLine(`ReadFile: (${file.id}) 0x${nToRead.toString(16)} byte(s) ${file.fileName} ${hexDump}`);
         else {
            const strDump = lpBuffer.readAnsiString(nToRead);
            var lineNumber = logWriteLine(`ReadFile: (${file.id}) 0x${nToRead.toString(16)} byte(s) ${file.fileName} ${hexDump} (${strDump})`);
         }

         if (lineNumber == 9)
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