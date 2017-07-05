using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Threading;
using Indexing.FileSystem;
using Indexing.Storage;
using Timer = System.Timers.Timer;

namespace Indexing.Kernel
{
    public class FileQueue : IDisposable
    {
        public const int ProcessPeriodMS = 250;
        private ConcurrentDictionary<string, FileSystemEventArgs> _fileQueue = new ConcurrentDictionary<string, FileSystemEventArgs>();
        private ConcurrentDictionary<string, FileSystemWatcher> _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        private IStorage _storage;
        private TokenProvider _provider;
        private Timer _timer;

        public FileQueue(IStorage storage, TokenProvider provider)
        {
            _storage = storage;
            _provider = provider;
            _timer = new Timer(ProcessPeriodMS);
            _timer.Elapsed += (e, s) => Process();
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void Enqueue(string filePath, FileSystemEventArgs eventArgs)
        {
            _fileQueue.AddOrUpdate(filePath, eventArgs, (fp, ea) => eventArgs);
        }

        private void EnqueueDirectory(string directoryPath)
        {
            foreach (var directory in Directory.GetDirectories(directoryPath))
            {
                EnqueueDirectory(directory);
            }
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                Enqueue(file, null);
            }
        }

        private async void Process()
        {
            var frozenQueue = _fileQueue;
            _fileQueue = new ConcurrentDictionary<string, FileSystemEventArgs>();
            
            foreach (var key in frozenQueue.Keys)
            {
                var eventArgs = frozenQueue[key];
                try
                {
                    if (File.Exists(key))
                    {
                        if (eventArgs == null || eventArgs.ChangeType != WatcherChangeTypes.Renamed)
                        {
                            if (File.Exists(key))
                                await _storage.Add(_provider.Provide(key), key);
                        }
                        else
                        {
                            var renamedEventArgs = eventArgs as RenamedEventArgs;
                            if (renamedEventArgs != null)
                            {
                                if (frozenQueue.ContainsKey(renamedEventArgs.OldFullPath))
                                {
                                    await _storage.Delete(renamedEventArgs.OldFullPath);
                                    await _storage.Add(_provider.Provide(key), renamedEventArgs.FullPath);
                                }
                                else
                                    await _storage.Move(renamedEventArgs.OldFullPath, renamedEventArgs.FullPath);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    _fileQueue.TryAdd(key, eventArgs);
                }
            }
        }

        private void AddFile(string filePath)
        {
            Enqueue(filePath, null);
            var fileName = Path.GetFileName(filePath);
            var watcher = new FileSystemWatcher();
            watcher.Filter = fileName;
            watcher.Path = filePath.Substring(0, filePath.Length - fileName.Length);
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            watcher.Changed += (sender, eventArgs) => Enqueue(filePath, eventArgs);
            watcher.Renamed += (sender, eventArgs) =>
            {
                watcher.Filter = eventArgs.Name;

                FileSystemWatcher watcherForFile;
                if (_watchers != null)
                {
                    if (_watchers.TryRemove(eventArgs.FullPath, out watcherForFile))
                    {
                        _watchers.TryAdd(filePath, watcherForFile);
                    }
                }
                Enqueue(eventArgs.FullPath, eventArgs);
            };
            watcher.Deleted += (sender, eventArgs) =>
            {
                watcher.EnableRaisingEvents = false;
                
                FileSystemEventArgs fsEventArgs;
                _fileQueue.TryRemove(eventArgs.FullPath, out fsEventArgs);

                FileSystemWatcher watcherUnsubscribe;
                if (_watchers != null) _watchers.TryRemove(eventArgs.FullPath, out watcherUnsubscribe);

                watcher.Dispose();
                _storage.Delete(eventArgs.FullPath);
            };
            if (_watchers != null && _watchers.TryAdd(filePath, watcher))
                watcher.EnableRaisingEvents = true;
            else
                watcher.Dispose();

        }

        private void AddDirectory(string directoryPath)
        {
            EnqueueDirectory(directoryPath);
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = directoryPath;
            watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
            watcher.Created += (sender, eventArgs) => Enqueue(eventArgs.FullPath, eventArgs);
            watcher.Changed += (sender, eventArgs) => Enqueue(eventArgs.FullPath, eventArgs);
            watcher.Renamed += (sender, eventArgs) => Enqueue(eventArgs.FullPath, eventArgs);
            watcher.Deleted += (sender, eventArgs) =>
            {
                FileSystemEventArgs fsEventArgs;
                _fileQueue.TryRemove(eventArgs.FullPath, out fsEventArgs);

                FileSystemWatcher watcherUnsubscribe;
                if (_watchers != null && _watchers.TryRemove(eventArgs.FullPath, out watcherUnsubscribe))
                        watcherUnsubscribe.Dispose();

                _storage.Delete(eventArgs.FullPath);
            };
            if (_watchers != null && _watchers.TryAdd(directoryPath, watcher))
                watcher.EnableRaisingEvents = true;
            else
                watcher.Dispose();
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
            if (_watchers != null && _watchers.TryRemove(path, out watcherUnsubscribe))
            {
                watcherUnsubscribe.EnableRaisingEvents = false;
                watcherUnsubscribe.Dispose();
            }
            // TODO clear storage
        }

        public void Dispose()
        {
            var frozenWatchers = _watchers;
            _watchers = null;
            if (frozenWatchers != null)
            {
                foreach (var watcher in frozenWatchers.Values)
                {
                    watcher.Dispose();
                }
            }
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}