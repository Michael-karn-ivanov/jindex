using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.Threading.Tasks;

namespace Indexing.Storage
{
    public interface IStorage
    {
        void Add(IEnumerable<string> words, string filePath);
        void Delete(string filePath);
        void Move(string filePathFrom, string filePathTo);

        IEnumerable<string> Lookup(params string[] words);
    }
}