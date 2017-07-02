﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting.Channels;
using Indexing.FileSystem;
using Indexing.Storage;

namespace Indexing.Kernel
{
    public class FileQueue
    {
        private ConcurrentDictionary<string, bool> _fileQueue = new ConcurrentDictionary<string, bool>();
        private ConcurrentDictionary<string, FileSystemWatcher> _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        private IStorage _Storage;
        private TokenProvider _Provider;

        private void Enqueue(string filePath, bool reportedByWatcher)
        {
            _fileQueue.AddOrUpdate(filePath, reportedByWatcher, (fp, existedFlag) => existedFlag && reportedByWatcher);
        }

        private void EnqueueDirectory(string directoryPath, bool reportedByWatcher)
        {
            foreach (var directory in Directory.GetDirectories(directoryPath))
            {
                EnqueueDirectory(directory, reportedByWatcher);
            }
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                Enqueue(file, reportedByWatcher);
            }
        }

        private async void Process()
        {
            var frozenQueue = _fileQueue;
            _fileQueue = new ConcurrentDictionary<string, bool>();

            foreach (var key in frozenQueue.Keys)
            {
                if (frozenQueue[key] && File.Exists(key))
                {
                    // file was changed and worker knows about it
                    await _Storage.Change(_Provider.Provide(key), key);
                }
                else if (frozenQueue[key] && !File.Exists(key))
                {
                    // file was deleted and worker knows about it
                    await _Storage.Delete(key);
                    FileSystemWatcher watcherUnsubscribe;
                    if (_watchers.TryRemove(key, out watcherUnsubscribe))
                    {
                        watcherUnsubscribe.EnableRaisingEvents = false;
                        watcherUnsubscribe.Dispose();
                    }
                }
                else if (!frozenQueue[key] && !File.Exists(key))
                {
                    // someone tries to track unexisting file
                }
                else
                {
                    // !frozenQueue[key] && File.Exists(key) === we added new file for tracking
                    await _Storage.Add(_Provider.Provide(key), key);
                }
            }
        }

        private void AddFile(string filePath)
        {
            Enqueue(filePath, false);
            var fileName = Path.GetFileName(filePath);
            var watcher = new FileSystemWatcher();
            watcher.Filter = fileName;
            watcher.Path = filePath.Substring(0, filePath.Length - fileName.Length);
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            watcher.Changed += (sender, eventArgs) => Enqueue(filePath, true);
            watcher.Renamed += (sender, eventArgs) =>
            {
                Enqueue(eventArgs.OldFullPath, true);
                Enqueue(eventArgs.FullPath, true);
            };
            watcher.Deleted += (sender, eventArgs) => Enqueue(filePath, true);
            if (_watchers.TryAdd(filePath, watcher))
                watcher.EnableRaisingEvents = true;

        }

        private void AddDirectory(string directoryPath)
        {
            EnqueueDirectory(directoryPath, false);
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = directoryPath;
            watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
            watcher.Created += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    // false here as it's a new file and we want it to be added
                    Enqueue(eventArgs.FullPath, false);
            };
            watcher.Changed += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    Enqueue(eventArgs.FullPath, true);
            };
            watcher.Renamed += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.OldFullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    Enqueue(eventArgs.OldFullPath, true);
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    Enqueue(eventArgs.FullPath, true);
            };
            watcher.Deleted += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                    Enqueue(eventArgs.FullPath, true);
            };
            if (_watchers.TryAdd(directoryPath, watcher))
                watcher.EnableRaisingEvents = true;
        }

        public void Add(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) == FileAttributes.Directory)
                AddDirectory(path);
            else
                AddFile(path);
        }

        public void Delete(string path)
        {
            FileSystemWatcher watcherUnsubscribe;
            if (_watchers.TryRemove(path, out watcherUnsubscribe))
            {
                watcherUnsubscribe.EnableRaisingEvents = false;
                watcherUnsubscribe.Dispose();
            }
            // TODO clear storage
        }
    }
}