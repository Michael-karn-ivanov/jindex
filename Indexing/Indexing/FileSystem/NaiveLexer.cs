using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Indexing.FileSystem
{
    public class NaiveLexer : ILexer
    {
        public const int MaxWordLength = 200;
        private static HashSet<char> _separators = new HashSet<char>(new[] { ',', ':', '.', '?', '!', ' ', '\r', '\n' });
        public IEnumerable<string> Tokenize(StreamReader reader)
        {
            char[] word = new char[MaxWordLength];
            var length = 0;
            while (reader.Peek() > -1)
            {
                var next = (char)reader.Read();
                if (IsSeparator(next) || length == MaxWordLength)
                {
                    if (length > 0)
                        yield return new string(word, 0, length);
                    length = 0;
                }
                else
                {
                    word[length] = next;
                    length++;
                }
            }
        }

        public virtual bool IsSeparator(char ch)
        {
            return _separators.Contains(ch);
        }
    }
}
