using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Extractor
{
    using static NativeApi;
    using static NativeApi.ConsoleModes;
    using static NativeApi.StdHandleNumber;
    static class Tools
    {
        static readonly Regex VtTermRegex = new(string.Join('|', new[] {
            "^xterm", // xterm, PuTTY, Mintty
            "^rxvt", // RXVT
            "^eterm", // Eterm
            "^screen", // GNU screen, tmux
            "^tmux", // tmux
            "^vt100", "^vt102", "^vt220", "^vt320", // DEC VT series
            "ansi", // ANSI
            "scoansi", // SCO ANSI
            "cygwin", // Cygwin, MinGW
            "linux", // Linux console
            "konsole", // Konsole
            "bvterm" // Bitvise SSH Client
        }), RegexOptions.Compiled);
        // adapted from https://github.com/keqingrong/supports-ansi/blob/master/index.js
        public static bool GuessVTSequenceSupport()
        {
            if (Console.IsOutputRedirected)
                return false;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                GetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), out var consoleModes);
                if ((ENABLE_VIRTUAL_TERMINAL_PROCESSING & consoleModes) == ENABLE_VIRTUAL_TERMINAL_PROCESSING)
                    return true;

                if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 14393)
                    return true;

                static bool IsMinGW()
                {
                    try
                    {
                        var proc = Process.Start(new ProcessStartInfo("uname") {
                            RedirectStandardOutput = true,
                        });
                        var rs = proc.StandardOutput.ReadToEnd().Contains("mingw", StringComparison.OrdinalIgnoreCase);
                        proc.WaitForExit();
                        return rs;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (IsMinGW())
                    return true;
            }

            if (VtTermRegex.IsMatch(Environment.GetEnvironmentVariable("TERM") ?? ""))
                return true;

            if ("on".Equals(Environment.GetEnvironmentVariable("ConEmuANSI"), StringComparison.OrdinalIgnoreCase))
                return true;

            if (Environment.GetEnvironmentVariable("ANSICON")?.Length > 0)
                return true;

            return false;
        }
    }
}
