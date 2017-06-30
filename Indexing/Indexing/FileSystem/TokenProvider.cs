using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Indexing.FileSystem
{
    class TokenProvider
    {
        public ILexer Lexer { get; set; }

        private IEnumerable<string> Provide(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            {
                return Lexer.Tokenize(reader);
            }
        }
    }
}
