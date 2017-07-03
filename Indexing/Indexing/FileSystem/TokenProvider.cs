using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Indexing.FileSystem
{
    public class TokenProvider
    {
        private ILexer _lexer;

        public TokenProvider(ILexer lexer)
        {
            _lexer = lexer;
        }

        public IEnumerable<string> Provide(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            {
                return _lexer.Tokenize(reader);
            }
        }
    }
}
