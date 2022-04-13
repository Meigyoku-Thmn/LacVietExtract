using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{

    public static class Log
    {
        public static int IndentLevel { get; set; }
        public static void Write(object message)
        {
            Console.Write(new string(' ', IndentLevel * 2));
            Console.WriteLine(message);
        }
    }
}
