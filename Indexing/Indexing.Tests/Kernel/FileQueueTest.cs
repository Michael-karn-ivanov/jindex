using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Indexing.FileSystem;
using Indexing.Kernel;
using Indexing.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Indexing.Tests.Kernel
{
    [TestClass]
    public class FileQueueTest
    {
        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestQueueSimpleStartStop()
        {
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
            }
        }

        [TestMethod]
        [TestCategory("DiskTests")]
        public void QueueStartAddDirectoryWithFiles()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var fileName1 = directory.FullName + "\\" + Guid.NewGuid().ToString();
            var fileName2 = directory.FullName + "\\" + Guid.NewGuid().ToString();
            using (var fs = File.Create(fileName1))
            {
            }
            using (var fs = File.Create(fileName2))
            {
            }
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(directory.FullName);
                Thread.Sleep(1000);
                Assert.AreEqual(2, storage.Actions.Count);
                var file1 = storage.Actions.Dequeue();
                var file2 = storage.Actions.Dequeue();
                Assert.IsTrue((file1.Item1 == fileName1 && file2.Item1 == fileName2)
                              || (file1.Item1 == fileName2 && file2.Item1 == fileName1));
                Assert.AreEqual(file1.Item2, StorageMock._Add);
                Assert.AreEqual(file2.Item2, StorageMock._Add);
            }
            File.Delete(fileName1);
            File.Delete(fileName2);
            Directory.Delete(directory.FullName);
        }

        private class LexerMock : ILexer
        {
            public IEnumerable<string> Tokenize(StreamReader reader)
            {
                return new string[0];
            }
        }

        private class StorageMock : IStorage
        {
            public const string _Add = "Add";
            public const string _Change = "Change";
            public const string _Delete = "Delete";
            public const string _MoveFrom = "MoveFrom";
            public const string _MoveTo = "MoveTo";
            public Queue<Tuple<string, string>> Actions = new Queue<Tuple<string, string>>();

            public Task Add(IEnumerable<string> words, string filePath)
            {
                Actions.Enqueue(new Tuple<string, string>(filePath, _Add));
                return Task.Delay(0);
            }

            public Task Change(IEnumerable<string> words, string filePath)
            {
                Actions.Enqueue(new Tuple<string, string>(filePath, _Change));
                return Task.Delay(0);
            }

            public Task Delete(string filePath)
            {
                Actions.Enqueue(new Tuple<string, string>(filePath, _Delete));
                return Task.Delay(0);
            }

            public Task Move(string filePathFrom, string filePathTo)
            {
                Actions.Enqueue(new Tuple<string, string>(filePathFrom, _MoveFrom));
                Actions.Enqueue(new Tuple<string, string>(filePathTo, _MoveTo));
                return Task.Delay(0);
            }
        }
    }
}
