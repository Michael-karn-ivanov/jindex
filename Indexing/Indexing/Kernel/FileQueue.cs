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
        private ConcurrentDictionary<string, FileSystemWatcher> _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        private ConcurrentDictionary<string, Timer> _timers = new ConcurrentDictionary<string, Timer>();
        private ConcurrentDictionary<string, FileSystemEventArgs> _eventArgses = new ConcurrentDictionary<string, FileSystemEventArgs>();
        private TimerPool _timerPool = new TimerPool();
        private IStorage _storage;
        private TokenProvider _provider;

        public FileQueue(IStorage storage, TokenProvider provider)
        {
            _storage = storage;
            _provider = provider;
        }

        private void EnqueueDirectory(string directoryPath)
        {
            foreach (var directory in Directory.GetDirectories(directoryPath))
            {
                EnqueueDirectory(directory);
            }
            foreach (var filePath in Directory.GetFiles(directoryPath))
            {
                Process(filePath);
            }
        }

        private void Process(string fileName)
        {
            try
            {
                FileSystemEventArgs eventArgs;
                if (_eventArgses.TryGetValue(fileName, out eventArgs))
                {
                    var renamedEventArgs = eventArgs as RenamedEventArgs;
                    if (renamedEventArgs != null)
                    {
                        _storage.Move(renamedEventArgs.OldFullPath, renamedEventArgs.FullPath);
                    }
                    else if (eventArgs != null)
                    {
                        if (File.Exists(eventArgs.FullPath))
                            _storage.Add(_provider.Provide(eventArgs.FullPath), eventArgs.FullPath);
                    }
                    _eventArgses.TryRemove(fileName, out eventArgs);
                    Timer timer = null;
                    if (_timers != null) _timers.TryRemove(fileName, out timer);
                    if (timer != null) _timerPool.Put(timer);
                }
                else
                {
                    _storage.Add(_provider.Provide(fileName), fileName);
                }
            }
            catch (Exception)
            {
                setupTimer(fileName);
            }
        }

        private void setupTimer(string filePath)
        {
            _timers.AddOrUpdate(filePath,
                    key =>
                    {
                        var newTimer = _timerPool.Get();
                        newTimer.Elapsed += (o, args) => Process(filePath);
                        newTimer.Start();
                        return newTimer;
                    }, (key, runningTimer) =>
                    {
                        runningTimer.Interval = TimerPool.IntervalMS;
                        runningTimer.Start();
                        return runningTimer;
                    });
        }

        private void AddFile(string filePath)
        {
            Process(filePath);
            var fileName = Path.GetFileName(filePath);
            var watcher = new FileSystemWatcher();
            watcher.Filter = fileName;
            watcher.Path = filePath.Substring(0, filePath.Length - fileName.Length);
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            watcher.Changed += (sender, eventArgs) =>
            {
                _eventArgses.AddOrUpdate(eventArgs.FullPath, eventArgs, (key, args) => eventArgs);
                setupTimer(eventArgs.FullPath);
            };
            watcher.Renamed += (sender, eventArgs) =>
            {
                watcher.Filter = eventArgs.Name;
                _eventArgses.AddOrUpdate(filePath, eventArgs, (key, args) => eventArgs);
                setupTimer(eventArgs.FullPath);

                Timer timer = null;
                if (_timers != null) _timers.TryRemove(eventArgs.OldFullPath, out timer);
                if (timer != null) _timerPool.Put(timer);

                FileSystemEventArgs fsEventArgs;
                _eventArgses.TryRemove(eventArgs.OldFullPath, out fsEventArgs);

                FileSystemWatcher watcherForFile;
                if (_watchers != null)
                {
                    if (_watchers.TryRemove(eventArgs.FullPath, out watcherForFile))
                    {
                        _watchers.TryAdd(filePath, watcherForFile);
                    }
                }
            };
            watcher.Deleted += (sender, eventArgs) =>
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();

                Timer timer = null;
                if (_timers != null) _timers.TryRemove(eventArgs.FullPath, out timer);
                if (timer != null) _timerPool.Put(timer);

                FileSystemEventArgs fsEventArgs;
                _eventArgses.TryRemove(eventArgs.FullPath, out fsEventArgs);

                FileSystemWatcher watcherUnsubscribe;
                if (_watchers != null) _watchers.TryRemove(eventArgs.FullPath, out watcherUnsubscribe);

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
            watcher.Changed += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                {
                    _eventArgses.AddOrUpdate(eventArgs.FullPath, eventArgs, (key, args) => eventArgs);
                    setupTimer(eventArgs.FullPath);
                }
            };
            watcher.Renamed += (sender, eventArgs) =>
            {
                if ((File.GetAttributes(eventArgs.FullPath) & FileAttributes.Directory) != FileAttributes.Directory)
                {
                    _eventArgses.AddOrUpdate(eventArgs.FullPath, eventArgs, (key, args) => eventArgs);
                    setupTimer(eventArgs.FullPath);
                }
            };
            watcher.Deleted += (sender, eventArgs) =>
            {
                Timer timer = null;
                if (_timers != null) _timers.TryRemove(eventArgs.FullPath, out timer);
                if (timer != null) _timerPool.Put(timer);

                FileSystemEventArgs fsEventArgs;
                _eventArgses.TryRemove(eventArgs.FullPath, out fsEventArgs);

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
            GC.SuppressFinalize(this);
        }
    }
}