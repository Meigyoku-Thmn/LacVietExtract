using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extractor
{
    public class VtProgressBar : IDisposable
    {
        static readonly bool Enabled = Tools.GuessVTSequenceSupport();
        bool On = false;

        static readonly VtProgressBar Instance = new();
        static public VtProgressBar Get() => Instance;

        public string Title = "";
        public char DoneChr = '█';
        public char OngoingChr = '░';

        int NColumns = 0;
        int NRows = 0;

        public int Count = 0;
        public int Total = 100;

        public VtProgressBar()
        {
            NColumns = Console.WindowWidth;
            NRows = Console.WindowHeight;

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
            Console.CancelKeyPress += (_, _) => Dispose();
        }

        public void Initialize()
        {
            if (!Enabled)
                return;
            On = true;

            NColumns = Console.WindowWidth;
            NRows = Console.WindowHeight;

            Console.Write("\u001bD");                 // Add 1 line down, keep column position
            Console.Write("\u001b7");                 // Save the cursor position
            Console.Write($"\u001b[0;{NRows - 1}r");  // Set scroll region that reserves the bottom line
            Console.Write("\u001b8");                 // Restore the cursor position
        }

        public void Tick(int step = 1)
        {
            if (!Enabled || !On)
                return;

            Count += step;
            if (Count > Total)
                Count = Total;

            var meta = $"[{(Title.Length > 0 ? Title + ' ' : "")}{Count}/{Total}] ";
            if (NColumns <= meta.Length)
                meta = "";

            var barWidth = Math.Abs(NColumns - meta.Length);
            var barDone = barWidth * Count / Total;
            var doneStr = new string(DoneChr, barDone);
            var todoStr = new string(OngoingChr, barWidth - barDone);
            var progressBar = $"{meta}{doneStr}{todoStr}";

            Console.Write("\u001b7");               // Save cursor position
            Console.Write($"\u001b[{NRows};0f");    // Move cursor to the bottom margin

            Console.Write(progressBar);             // Render the progress bar

            Console.Write("\u001b8");               // Restore cursor position
        }

        public void Dispose()
        {
            if (!Enabled || !On)
                return;
            GC.SuppressFinalize(this);

            Console.Write("\u001b7");                   // Save the cursor position
            Console.Write($"\u001b[0;{NRows}r", NRows); // Restore the default scroll region
            Console.Write($"\u001b[{NRows};0f", NRows); // Move the cursor to the bottom line
            Console.Write("\u001b[0K");                 // Erase the entire line
            Console.Write("\u001b8");

            On = false;
        }
    }
}
