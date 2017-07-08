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
                Thread.Sleep((int)(FileQueue.ProcessPeriodMS * 1.25));
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
                Thread.Sleep((int) (1.25*FileQueue.ProcessPeriodMS));
                Assert.AreEqual(0, storage.Actions.Count);
                var fileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                using (var fs = File.Create(fileName))
                {
                    fs.Write(new byte[] {1}, 0, 1);
                }
                Thread.Sleep((int) (1.25*FileQueue.ProcessPeriodMS));
                Assert.AreEqual(1, storage.Actions.Count);
                using (StreamWriter sw = new StreamWriter(fileName))
                {
                    sw.Write('a');
                    sw.Flush();
                    sw.Close();
                }
                Thread.Sleep((int) (1.25*FileQueue.ProcessPeriodMS));
                Assert.AreEqual(2, storage.Actions.Count);
                var newFileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                File.Move(fileName, newFileName);
                Thread.Sleep((int) (1.25*FileQueue.ProcessPeriodMS));
                Assert.AreEqual(4, storage.Actions.Count);
                File.Delete(newFileName);
                Thread.Sleep((int) (1.25*FileQueue.ProcessPeriodMS));
                Assert.AreEqual(5, storage.Actions.Count);
                var tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._MoveFrom, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._MoveTo, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
            }
            Directory.Delete(directory.FullName);
        }

        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestAddDirectoryCreateFileChangeFileRenameFileRemoveFileFast()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(directory.FullName);
                var fileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                using (var fs = File.Create(fileName))
                {
                    fs.Write(new byte[] { 1 }, 0, 1);
                }
                using (StreamWriter sw = new StreamWriter(fileName))
                {
                    sw.Write('a');
                    sw.Flush();
                    sw.Close();
                }
                var newFileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                File.Move(fileName, newFileName);
                File.Delete(newFileName);
                Thread.Sleep((int)(0.1 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(1, storage.Actions.Count);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(1, storage.Actions.Count);
                var tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
            }
            Directory.Delete(directory.FullName);
        }

        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestAddExplicitlyFileChangeFileRenameDeleteFileFast()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var fileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
            using (var fs = File.Create(fileName))
            {
                fs.Write(new byte[] { 1 }, 0, 1);
                fs.Close();
            }
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(fileName);
                var newFileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                using (StreamWriter sw = new StreamWriter(fileName))
                {
                    sw.Write('a');
                    sw.Flush();
                    sw.Close();
                }
                File.Move(fileName, newFileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(2, storage.Actions.Count);
                File.Delete(newFileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(3, storage.Actions.Count);
                var tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
            }
        }

        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestAddExplicitlyFileChangeFileRenameDeleteFileSlow()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var fileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
            using (var fs = File.Create(fileName))
            {
                fs.Write(new byte[] { 1 }, 0, 1);
                fs.Close();
            }
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(fileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(1, storage.Actions.Count);
                var newFileName = directory.FullName + "\\" + Guid.NewGuid().ToString();
                using (StreamWriter sw = new StreamWriter(fileName))
                {
                    sw.Write('a');
                    sw.Flush();
                    sw.Close();
                }
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(2, storage.Actions.Count);
                File.Move(fileName, newFileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(4, storage.Actions.Count);
                File.Delete(newFileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(5, storage.Actions.Count);
                var tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(fileName, tuple.Item1);
                Assert.AreEqual(StorageMock._MoveFrom, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._MoveTo, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
            }
        }

        [TestMethod]
        [TestCategory("DiskTests")]
        public void TestCreateNestedDirectoryCopyFileCreateFileCMoveFileUpDeleteFileSlow()
        {
            var directoryName = Guid.NewGuid().ToString();
            var directory = Directory.CreateDirectory(directoryName);
            var helperDirectory = Directory.CreateDirectory(Guid.NewGuid().ToString());
            var fileNameWOPath = Guid.NewGuid().ToString();
            var fileName = helperDirectory.FullName + "\\" + fileNameWOPath;
            using (var fs = File.Create(fileName))
            {
                fs.Write(new byte[] {1}, 0, 1);
            }
            var provider = new TokenProvider(new LexerMock());
            var storage = new StorageMock();
            using (var objectUnderTest = new FileQueue(storage, provider))
            {
                objectUnderTest.Add(directory.FullName);
                var subdirectory = directory.CreateSubdirectory(Guid.NewGuid().ToString());
                var newFileName = subdirectory.FullName + "\\" + fileNameWOPath;
                File.Move(fileName, newFileName);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(1, storage.Actions.Count);
                var createdFileName = subdirectory.FullName + "\\" + Guid.NewGuid().ToString();
                using (var fs = File.Create(createdFileName))
                {
                    fs.Write(new byte[] {1}, 0, 1);
                }
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(2, storage.Actions.Count);
                var newDestination = directory.FullName + "\\" + Guid.NewGuid().ToString();
                File.Move(createdFileName, newDestination);
                Thread.Sleep((int)(1.25 * FileQueue.ProcessPeriodMS));
                Assert.AreEqual(4, storage.Actions.Count);
                File.Delete(newDestination);
                Assert.AreEqual(5, storage.Actions.Count);
                File.Delete(newFileName);

                Assert.AreEqual(6, storage.Actions.Count);
                var tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(createdFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(createdFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newDestination, tuple.Item1);
                Assert.AreEqual(StorageMock._Add, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newDestination, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
                tuple = storage.Actions.Dequeue();
                Assert.AreEqual(newFileName, tuple.Item1);
                Assert.AreEqual(StorageMock._Delete, tuple.Item2);
            }
            Directory.Delete(helperDirectory.FullName);
            Directory.Delete(directory.FullName, true);
        }

        [TestMethod]
        [TestCategory("DiskTests")]
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
