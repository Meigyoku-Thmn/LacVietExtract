using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{

    public static class Log
    {
#pragma warning disable CA2211
        public static int IndentLevel = 0;
#pragma warning restore CA2211
        public static void Write(object message)
        {
            Console.Write(new string(' ', IndentLevel * 2));
            Console.WriteLine(message);
        }
    }
}
