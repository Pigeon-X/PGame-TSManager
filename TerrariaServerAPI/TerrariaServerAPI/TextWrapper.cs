using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Console = System.Console;

namespace GameLauncher
{
    internal class TextWrapper : TextReader
    {
        private readonly TextReader orig;
        public TextWrapper(TextReader orig)
        {
            this.orig = orig;
        }

        public override string ReadLine()
        {
            Console.Out.WriteLine();
            return orig.ReadLine();
        }
    }

}
