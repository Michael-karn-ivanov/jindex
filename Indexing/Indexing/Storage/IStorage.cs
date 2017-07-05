using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Indexing.Storage
{
    public interface IStorage
    {
        Task Add(IEnumerable<string> words, string filePath);
        Task Delete(string filePath);
        Task Move(string filePathFrom, string filePathTo);
    }
}