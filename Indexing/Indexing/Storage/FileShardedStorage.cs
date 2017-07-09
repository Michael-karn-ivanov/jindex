using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Indexing.Storage
{
    class FileShardedStorage : IStorage
    {
        private ConcurrentDictionary<string, HashSet<string>> _fileWords 
            = new ConcurrentDictionary<string, HashSet<string>>(); 
        private ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _wordFiles
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

        public void Add(IEnumerable<string> words, string filePath)
        {
            var dictionary = new HashSet<string>();
            foreach (var word in words)
            {
                dictionary.Add(word);
                _wordFiles.AddOrUpdate(word, s => new ConcurrentDictionary<string, string>(
                    new[] { new KeyValuePair<string, string>(filePath, filePath) }),
                    (key, map) =>
                    {
                        map.TryAdd(filePath, filePath);
                        return map;
                    });
            }
            _fileWords.AddOrUpdate(filePath, dictionary, (file, map) => dictionary);
        }

        public void Delete(string filePath)
        {
            HashSet<string> words;
            if (_fileWords.TryRemove(filePath, out words))
            {
                ConcurrentDictionary<string, string> files;
                string removedFileName;
                foreach (var word in words)
                {
                    if (_wordFiles.TryGetValue(word, out files))
                    {
                        files.TryRemove(filePath, out removedFileName);
                    }
                }
            }
        }

        public IEnumerable<string> Lookup(params string[] words)
        {
            ConcurrentDictionary<string, string> files;
            foreach (var word in words)
            {
                if (_wordFiles.TryGetValue(word, out files))
                {
                    foreach (var key in files.Keys)
                    {
                        yield return key;
                    }
                }
            }
        }

        public void Move(string filePathFrom, string filePathTo)
        {
            HashSet<string> words;
            if (_fileWords.TryRemove(filePathFrom, out words))
            {
                _fileWords.AddOrUpdate(filePathTo, words, (s, set) => words);
                ConcurrentDictionary<string, string> files;
                string devNull;
                foreach (var word in words)
                {
                    if (_wordFiles.TryGetValue(word, out files))
                    {
                        if (files.TryRemove(filePathFrom, out devNull)) files.TryAdd(filePathTo, filePathTo);
                    }
                }
            }
        }
    }
}
