using EasyHook;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DLLInjector
{
    class Program
    {
        static readonly string TracerDllPath = Path.GetFullPath("../../../../Debug/Tracer.dll");
        const string TargetPath = "C:/Program Files (x86)/LacViet/mtdFVP/mtd2012_dbg.exe";
        static readonly string LogPath = Path.GetFullPath("../../../../log2.csv");

        static unsafe void Main(string[] args)
        {
            Console.WriteLine("Create process suspended ...");
            NativeAPI.RtlCreateSuspendedProcess(TargetPath, null, 0, out var processId, out var threadId);
            Console.WriteLine("Inject the tracer.dll library...");
            try
            {
                fixed (char* logPathPtr = LogPath)
                {
                    NativeAPI.RhInjectLibrary(
                        processId, threadId,
                        NativeAPI.EASYHOOK_INJECT_DEFAULT,
                        TracerDllPath, null,
                        new IntPtr(logPathPtr), LogPath.Length * 2
                    );
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Process.GetProcessById(processId).Kill();
                Console.Error.WriteLine("The target process has been terminated.");
            }
        }
    }
}
