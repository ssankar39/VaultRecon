using BetterFileSys.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    public class BackgroundIndexService : IDisposable
    {
        private const int DebounceMilliseconds = 1000;
        private const int BatchSize = 1;
        private const int MaxDepth = 8;
        private const int StartupDelayMilliseconds = 1500;
        private const int BatchDelayMilliseconds = 120;
        private const int PerFileDelayMilliseconds = 30;
        private const int WorkChunkSize = 40;
        private const int WorkChunkDelayMilliseconds = 250;
        private const long MaxFileSizeBytes = 256 * 1024;
        private const long MaxBinaryFileSizeBytes = 10 * 1024 * 1024; // 10 MB for documents and images

        private readonly LanceDbIndexService _indexService;
        private readonly EmbeddingService _embeddingService;
        private readonly ConcurrentQueue<IndexWorkItem> _workQueue = new();
        private readonly ConcurrentDictionary<string, PendingEvent> _pendingEvents = new(StringComparer.OrdinalIgnoreCase);
        private readonly AutoResetEvent _queueSignal = new(false);
        private readonly ManualResetEventSlim _pauseGate = new(true);
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "BetterFileSys_Debug.log");
        private readonly string[] _rootPaths;

        private Thread? _workerThread;
        private Timer? _debounceTimer;
        private FileSystemWatcher[] _watchers = Array.Empty<FileSystemWatcher>();
        private volatile bool _isRunning;
        private volatile bool _isPaused;

        public event Action<string>? StatusChanged;
        public event Action<bool>? IndexingStateChanged;

        public BackgroundIndexService(LanceDbIndexService indexService, EmbeddingService embeddingService, string[] rootPaths)
        {
            _indexService = indexService;
            _embeddingService = embeddingService;
            _rootPaths = rootPaths;
        }

        public async Task StartAsync()
        {
            if (_isRunning)
                return;

            await _indexService.InitializeAsync();

            _isRunning = true;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "BetterFileSys.IndexWorker"
            };
            _workerThread.Start();

            _debounceTimer = new Timer(_ => FlushPendingEvents(), null, DebounceMilliseconds, DebounceMilliseconds);
            _watchers = CreateWatchers(_rootPaths);

            ScheduleStartupScan();
        }

        private void WorkerLoop()
        {
            while (_isRunning)
            {
                _queueSignal.WaitOne();

                while (_workQueue.TryDequeue(out var workItem))
                {
                    _pauseGate.Wait();

                    try
                    {
                        switch (workItem.Type)
                        {
                            case IndexWorkType.StartupScan:
                                RunStartupDeltaScanAsync().GetAwaiter().GetResult();
                                break;
                            case IndexWorkType.Upsert:
                                IndexFileAsync(workItem.Path).GetAwaiter().GetResult();
                                break;
                            case IndexWorkType.Delete:
                                DeleteFileAsync(workItem.Path).GetAwaiter().GetResult();
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[INDEX] Worker error: {ex.Message}");
                    }
                }
            }
        }

        private async Task RunStartupDeltaScanAsync()
        {
            IndexingStateChanged?.Invoke(true);
            StatusChanged?.Invoke("Indexing: delta scan (startup)");

            var existingMetadata = await _indexService.GetAllMetadataAsync();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batch = new List<FileIndexRecord>();

            foreach (var root in _rootPaths)
            {
                if (!Directory.Exists(root))
                    continue;

                StatusChanged?.Invoke($"Indexing: scanning {root}");

                int workCount = 0;

                foreach (var filePath in EnumerateFilesSafe(root))
                {
                    if (!_isRunning)
                        return;

                    _pauseGate.Wait();

                    seenPaths.Add(filePath);

                    if (!ShouldIndexFile(filePath))
                        continue;

                    var info = new FileInfo(filePath);
                    bool isText = IsSupportedTextFile(filePath);
                    long maxAllowedSize = isText ? MaxFileSizeBytes : MaxBinaryFileSizeBytes;
                    if (info.Length > maxAllowedSize)
                        continue;
                    long modifiedTicks = info.LastWriteTimeUtc.ToFileTimeUtc();

                    if (existingMetadata.TryGetValue(filePath, out var existing))
                    {
                        if (existing.ModifiedTicksUtc == modifiedTicks && existing.Size == info.Length)
                            continue;
                    }

                    var record = await BuildRecordAsync(filePath, info, modifiedTicks);
                    if (record == null)
                        continue;

                    batch.Add(record);

                    if (batch.Count >= BatchSize)
                    {
                        await _indexService.UpsertBatchAsync(batch);
                        batch.Clear();
                        await Task.Delay(BatchDelayMilliseconds);
                    }

                    workCount++;
                    if (workCount >= WorkChunkSize)
                    {
                        workCount = 0;
                        await Task.Delay(WorkChunkDelayMilliseconds);
                    }
                    else
                    {
                        await Task.Delay(PerFileDelayMilliseconds);
                    }
                }
            }

            if (batch.Count > 0)
            {
                await _indexService.UpsertBatchAsync(batch);
                batch.Clear();
            }

            var stalePaths = existingMetadata.Keys
                .Where(path => IsUnderRoots(path) && !seenPaths.Contains(path))
                .ToList();

            if (stalePaths.Count > 0)
            {
                await _indexService.DeleteByPathsAsync(stalePaths);
            }

            await _indexService.OptimizeAsync();

            StatusChanged?.Invoke("Indexing: idle");
            IndexingStateChanged?.Invoke(false);
        }

        public void PauseIndexing()
        {
            if (_isPaused)
                return;

            _isPaused = true;
            _pauseGate.Reset();
            StatusChanged?.Invoke("Indexing: paused");
        }

        public void ResumeIndexing()
        {
            if (!_isPaused)
                return;

            _isPaused = false;
            _pauseGate.Set();
            StatusChanged?.Invoke("Indexing: idle");
        }

        private async Task IndexFileAsync(string filePath)
        {
            if (!ShouldIndexFile(filePath))
                return;

            var info = new FileInfo(filePath);
            bool isText = IsSupportedTextFile(filePath);
            long maxAllowedSize = isText ? MaxFileSizeBytes : MaxBinaryFileSizeBytes;
            if (info.Length > maxAllowedSize)
                return;
            long modifiedTicks = info.LastWriteTimeUtc.ToFileTimeUtc();

            var record = await BuildRecordAsync(filePath, info, modifiedTicks);
            if (record == null)
                return;

            await _indexService.UpsertBatchAsync(new[] { record });
            await Task.Delay(PerFileDelayMilliseconds);
        }

        private async Task DeleteFileAsync(string filePath)
        {
            await _indexService.DeleteByPathsAsync(new[] { filePath });
        }

        private async Task<FileIndexRecord?> BuildRecordAsync(string filePath, FileInfo info, long modifiedTicks)
        {
            string content = "";

            if (IsSupportedTextFile(filePath))
            {
                content = await ReadFileContentAsync(filePath, maxLines: 100);
            }
            else
            {
                // Parse binary document or image content using local Python script
                content = await ParseBinaryDocumentAsync(filePath);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                content = Path.GetFileNameWithoutExtension(info.Name);
            }

            // Prepend the file path and file name to include path context (Downloads, Documents, Pictures, etc.) in the semantic embedding
            string textToEmbed = $"File Path: {filePath}\nFile Name: {info.Name}\n\nContent:\n{content}";
            if (textToEmbed.Length > 2000)
            {
                // Truncate to fit model sequence/token constraints comfortably
                textToEmbed = textToEmbed.Substring(0, 2000);
            }

            var embedding = await _embeddingService.GetEmbeddingAsync(textToEmbed);
            if (embedding == null || embedding.Length == 0)
                return null;

            if (embedding.Length != 384)
                return null;

            return new FileIndexRecord
            {
                FilePath = filePath,
                FileName = info.Name,
                FileType = info.Extension,
                FileSize = info.Length,
                ModifiedTicksUtc = modifiedTicks,
                Embedding = embedding
            };
        }

        private string GetParserScriptPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var current = new DirectoryInfo(baseDir);
            while (current != null)
            {
                var scriptPath = Path.Combine(current.FullName, "FS", "scripts", "parse_document.py");
                if (File.Exists(scriptPath))
                    return scriptPath;

                scriptPath = Path.Combine(current.FullName, "scripts", "parse_document.py");
                if (File.Exists(scriptPath))
                    return scriptPath;

                current = current.Parent;
            }
            throw new FileNotFoundException("Could not locate parse_document.py script relative to the application base directory.");
        }

        private async Task<string> ParseBinaryDocumentAsync(string filePath)
        {
            try
            {
                string scriptPath = GetParserScriptPath();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{scriptPath}\" \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync();

                string output = outputTask.Result;
                string error = errorTask.Result;

                if (process.ExitCode != 0)
                {
                    Log($"[INDEX] Parser process exited with code {process.ExitCode}. Error: {error}");
                    return Path.GetFileNameWithoutExtension(filePath);
                }

                return output;
            }
            catch (Exception ex)
            {
                Log($"[INDEX] Error parsing binary document {filePath}: {ex.Message}");
                return Path.GetFileNameWithoutExtension(filePath);
            }
        }

        private static bool ShouldIndexFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            if (Directory.Exists(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".py" or ".js" or ".html" or ".css"
                or ".pdf" or ".docx" or ".doc" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".png" or ".jpg" or ".jpeg";
        }

        private static bool IsSupportedTextFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".py" or ".js" or ".html" or ".css";
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (current, depth) = stack.Pop();

                if (depth > MaxDepth)
                    continue;

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (var dir in directories)
                {
                    try
                    {
                        var name = Path.GetFileName(dir).ToLowerInvariant();
                        if (name is ".venv" or "node_modules" or "bin" or "obj" or ".git" or ".idea" or ".vs" or "appdata")
                            continue;

                        var info = new DirectoryInfo(dir);
                        if ((info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    stack.Push((dir, depth + 1));
                }
            }
        }

        private static async Task<string> ReadFileContentAsync(string filePath, int maxLines)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                        return string.Empty;

                    return string.Join(Environment.NewLine, File.ReadLines(filePath).Take(maxLines));
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        private FileSystemWatcher[] CreateWatchers(string[] roots)
        {
            var watchers = new List<FileSystemWatcher>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName
                };

                watcher.Created += (_, e) => QueueEvent(e.FullPath, IndexWorkType.Upsert);
                watcher.Changed += (_, e) => QueueEvent(e.FullPath, IndexWorkType.Upsert);
                watcher.Deleted += (_, e) => QueueEvent(e.FullPath, IndexWorkType.Delete);
                watcher.Renamed += (_, e) =>
                {
                    QueueEvent(e.OldFullPath, IndexWorkType.Delete);
                    QueueEvent(e.FullPath, IndexWorkType.Upsert);
                };

                watchers.Add(watcher);
            }

            return watchers.ToArray();
        }

        private void QueueEvent(string path, IndexWorkType type)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            _pendingEvents[path] = new PendingEvent(type, DateTime.UtcNow);
        }

        private void FlushPendingEvents()
        {
            var threshold = DateTime.UtcNow.AddMilliseconds(-DebounceMilliseconds);

            foreach (var entry in _pendingEvents.ToArray())
            {
                if (entry.Value.LastUpdatedUtc > threshold)
                    continue;

                if (_pendingEvents.TryRemove(entry.Key, out var pending))
                {
                    EnqueueWork(new IndexWorkItem(pending.Type, entry.Key));
                }
            }
        }

        private void EnqueueWork(IndexWorkItem workItem)
        {
            _workQueue.Enqueue(workItem);
            _queueSignal.Set();
        }

        private void ScheduleStartupScan()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(StartupDelayMilliseconds);
                EnqueueWork(new IndexWorkItem(IndexWorkType.StartupScan, string.Empty));
            });
        }

        private bool IsUnderRoots(string path)
        {
            foreach (var root in _rootPaths)
            {
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            _isRunning = false;
            _queueSignal.Set();
            _pauseGate.Set();

            // Wait for background worker thread to terminate gracefully
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(TimeSpan.FromSeconds(3));
            }

            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _debounceTimer?.Dispose();
            _queueSignal.Dispose();
            _pauseGate.Dispose();
            _indexService.Dispose();
        }

        private enum IndexWorkType
        {
            StartupScan,
            Upsert,
            Delete
        }

        private sealed record PendingEvent(IndexWorkType Type, DateTime LastUpdatedUtc);

        private sealed record IndexWorkItem(IndexWorkType Type, string Path);
    }
}
