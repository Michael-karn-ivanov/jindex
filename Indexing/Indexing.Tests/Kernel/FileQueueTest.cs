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
        public void TestQueueStartAddDirectoryWithFiles()
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

        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestAddDirectoryCreateFileChangeFileRenameFileRemoveFileSlow()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(directory.FullName);
                Thread.Sleep((int)(1.25 * TimerPool.IntervalMS));
                Assert.AreEqual(0, storage.Actions.Count);
                var fileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                using (var fs = File.Create(fileName))
                {
                    fs.Write(new byte[] {1}, 0, 1);
                }
                Thread.Sleep((int)(1.25 * TimerPool.IntervalMS));
                Assert.AreEqual(1, storage.Actions.Count);
                using (StreamWriter sw = new StreamWriter(fileName))
                {
                    sw.Write('a');
                    sw.Flush();
                    sw.Close();
                }
                Thread.Sleep((int)(1.25 * TimerPool.IntervalMS));
                Assert.AreEqual(2, storage.Actions.Count);
                var newFileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                File.Move(fileName, newFileName);
                Thread.Sleep((int)(1.25 * TimerPool.IntervalMS));
                Assert.AreEqual(3, storage.Actions.Count);
                File.Delete(newFileName);
                Thread.Sleep((int)(1.25 * TimerPool.IntervalMS));
                Assert.AreEqual(4, storage.Actions.Count);
            }
        }

        public void TestAddExplicitlyFileChangeFileRenameDeleteFile()
        {
        }

        public void TestCreateNestedDirectoryCopyFileCreateFileChangeFileMoveFileUpDeleteFile()
        {
        }

        public void TestConcurrentFileCreateChangeDelete()
        {
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
