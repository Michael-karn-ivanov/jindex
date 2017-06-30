using System.Collections.Generic;
using System.IO;

namespace Indexing.FileSystem
{
    public interface ILexer
    {
        IEnumerable<string> Tokenize(StreamReader reader);
    }
}