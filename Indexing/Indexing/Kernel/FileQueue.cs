using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Threading;
using Indexing.FileSystem;
using Indexing.Storage;
using Timer = System.Timers.Timer;
using System.Diagnostics;

namespace Indexing.Kernel
{
    public class FileQueue : IDisposable
    {
        private static readonly TraceSource Log = new TraceSource("Indexing.FileQueue");
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
            Log.TraceEvent(TraceEventType.Information, 100, "{0} enqueued", filePath);
            _fileQueue.AddOrUpdate(filePath, eventArgs, (fp, ea) => eventArgs);
        }

        private void EnqueueDirectory(string directoryPath)
        {
            Log.TraceEvent(TraceEventType.Information, 101, "{0} enqueued", directoryPath);
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
                            _storage.Add(_provider.Provide(key), key);
                        }
                        else
                        {
                            var renamedEventArgs = eventArgs as RenamedEventArgs;
                            if (renamedEventArgs != null)
                            {
                                if (frozenQueue.ContainsKey(renamedEventArgs.OldFullPath))
                                {
                                    _storage.Delete(renamedEventArgs.OldFullPath);
                                    _storage.Add(_provider.Provide(key), renamedEventArgs.FullPath);
                                }
                                else
                                    _storage.Move(renamedEventArgs.OldFullPath, renamedEventArgs.FullPath);
                            }
                        }
                    }
                    else Log.TraceEvent(TraceEventType.Information, 103, "File {0} not found for processing", key);
                }
                catch (Exception)
                {
                    Log.TraceEvent(TraceEventType.Information, 102, "{0} failed to process, re-adding", key);
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
            watcher.Changed += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} changing triggered {1} watcher event", eventArgs.FullPath, watcher.Path);
                Enqueue(filePath, eventArgs);
            };
            watcher.Renamed += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} renaming to {1} triggered {2} watcher event", eventArgs.OldFullPath, eventArgs.FullPath, watcher.Path);
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
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} deletion triggered {1} watcher event", eventArgs.FullPath, watcher.Path);
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
            foreach (var directory in Directory.GetDirectories(directoryPath))
            {
                AddDirectory(directory);
            }
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                Enqueue(file, null);
            }
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = directoryPath;
            watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
            watcher.Created += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105, 
                    "{0} creation triggered {1} watcher event", eventArgs.FullPath, watcher.Path);
                if (Directory.Exists(eventArgs.FullPath)) AddDirectory(eventArgs.FullPath);
                else Enqueue(eventArgs.FullPath, eventArgs);
            };
            watcher.Changed += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} changing triggered {1} watcher event", eventArgs.FullPath, watcher.Path);
                Enqueue(eventArgs.FullPath, eventArgs);
            };
            watcher.Renamed += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} renaming to {1} triggered {2} watcher event", eventArgs.OldFullPath, eventArgs.FullPath, watcher.Path);
                Enqueue(eventArgs.FullPath, eventArgs);
            };
            watcher.Deleted += (sender, eventArgs) =>
            {
                Log.TraceEvent(TraceEventType.Verbose, 105,
                    "{0} deletion triggered {1} watcher event", eventArgs.FullPath, watcher.Path);
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