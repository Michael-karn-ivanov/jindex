using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Indexing.FileSystem;

namespace Indexing.Tests.FileSystem
{
    [TestClass]
    public class NaiveLexerTest
    {
        [TestMethod]
        public void TestCommonTextWithAllSeparators()
        {
            var objectUnderTest = new NaiveLexer();
            HashSet<string> resultSet;
            using (var memoryStream = new MemoryStream())
            {
                var streamWriter = new StreamWriter(memoryStream);
                streamWriter.Write("Hello, friend. Is this just a test?"
                    + Environment.NewLine + "No! See: there" + Environment.NewLine + "are lines, friend...");
                streamWriter.Flush();

                memoryStream.Position = 0;

                using (var streamReader = new StreamReader(memoryStream))
                {
                    resultSet = new HashSet<string>(objectUnderTest.Tokenize(streamReader));
                }
                streamWriter.Close();
            }
            Assert.IsNotNull(resultSet);
            Assert.AreEqual(12, resultSet.Count);
            Assert.IsTrue(resultSet.Contains("Hello"));
            Assert.IsTrue(resultSet.Contains("friend"));
            Assert.IsTrue(resultSet.Contains("Is"));
            Assert.IsTrue(resultSet.Contains("this"));
            Assert.IsTrue(resultSet.Contains("just"));
            Assert.IsTrue(resultSet.Contains("a"));
            Assert.IsTrue(resultSet.Contains("test"));
            Assert.IsTrue(resultSet.Contains("No"));
            Assert.IsTrue(resultSet.Contains("See"));
            Assert.IsTrue(resultSet.Contains("there"));
            Assert.IsTrue(resultSet.Contains("are"));
            Assert.IsTrue(resultSet.Contains("lines"));
        }
    }
}
