using BetterFileSys.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    /// <summary>
    /// Service for fast file search with Everything SDK integration roadmap
    /// M1: Walking Skeleton - Fast system-wide search using optimized System.IO
    /// 
    /// NOTE: EverythingNet 1.0.75 targets .NET Framework, not .NET 8.0.
    /// Phase 1b will integrate native Everything SDK via C++/CLI wrapper or P/Invoke.
    /// For MVP, using parallel directory search across key system paths.
    /// </summary>
    public class EverythingService
    {
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "BetterFileSys_Debug.log");

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
                Console.WriteLine(message);
            }
            catch { }
        }

        /// <summary>
        /// M1: Search for files across common system paths
        /// TODO: Phase 1b - Replace with native Everything SDK for &lt;500ms results
        /// </summary>
        public async Task<List<SearchResult>> SearchByKeywordAsync(string query, int maxResults = 50)
        {
            return await Task.Run(() =>
            {
                var results = new List<SearchResult>();

                try
                {
                    if (string.IsNullOrWhiteSpace(query))
                    {
                        Log("[SERVICE] Search query is empty");
                        return results;
                    }

                    Log($"[SERVICE] M1 Search for: {query}");
                    results = SearchSystemPaths(query, maxResults);
                    Log($"[SERVICE] Search complete. Returned {results.Count} results");
                }
                catch (Exception ex)
                {
                    Log($"[SERVICE] Search error: {ex}");
                }

                return results;
            });
        }

        /// <summary>
        /// Search across key system directories with intelligent path prioritization
        /// Searches in parallel for faster results
        /// </summary>
        private List<SearchResult> SearchSystemPaths(string query, int maxResults)
        {
            var allResults = new List<SearchResult>();
            var searchPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            };

            Log($"[SERVICE] Searching {searchPaths.Length} system paths");

            // Search all paths and collect results
            var pathTasks = searchPaths
                .AsParallel()
                .Where(p => Directory.Exists(p))
                .Select(path =>
                {
                    var pathResults = new List<SearchResult>();
                    try
                    {
                        SearchDirectoryRecursive(path, query, pathResults, maxResults);
                    }
                    catch (Exception ex)
                    {
                        Log($"[SERVICE] Error searching {path}: {ex.Message}");
                    }
                    return pathResults;
                })
                .ToList();

            // Merge results and sort by relevance
            foreach (var pathResults in pathTasks)
            {
                allResults.AddRange(pathResults);
            }

            // Remove duplicates and sort
            allResults = allResults
                .GroupBy(r => r.FilePath)
                .Select(g => g.First())
                .OrderByDescending(r => r.RelevanceScore)
                .Take(maxResults)
                .ToList();

            return allResults;
        }

        /// <summary>
        /// Recursively search directory with permission handling
        /// </summary>
        private void SearchDirectoryRecursive(string path, string query, List<SearchResult> results, int maxResults)
        {
            try
            {
                if (results.Count >= maxResults)
                    return;

                // Search files
                try
                {
                    var files = Directory.GetFiles(path, $"*{query}*");

                    foreach (var file in files.Take(maxResults - results.Count))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            double relevanceScore = CalculateFilenameRelevance(fileInfo.Name, query);

                            results.Add(new SearchResult
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                FileSize = fileInfo.Length,
                                FileType = fileInfo.Extension,
                                Modified = fileInfo.LastWriteTime,
                                RelevanceScore = relevanceScore,
                                SearchType = SearchType.Keyword
                            });
                        }
                        catch (Exception ex)
                        {
                            Log($"[SERVICE] Error processing file: {ex.Message}");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip protected directories
                }

                // Recursively search subdirectories (limit depth to avoid system folders)
                if (GetDirectoryDepth(path) < 6) // Limit recursion depth
                {
                    try
                    {
                        var directories = Directory.GetDirectories(path);
                        foreach (var dir in directories)
                        {
                            if (results.Count >= maxResults)
                                break;

                            // Skip system/hidden directories
                            var dirInfo = new DirectoryInfo(dir);
                            if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                                continue;
                            if (dirInfo.Name.StartsWith("."))
                                continue;

                            SearchDirectoryRecursive(dir, query, results, maxResults);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip if access denied
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVICE] Error searching directory {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get directory depth to limit recursion
        /// </summary>
        private int GetDirectoryDepth(string path)
        {
            return path.Split(Path.DirectorySeparatorChar).Length;
        }

        /// <summary>
        /// Calculate relevance score based on filename match
        /// </summary>
        private double CalculateFilenameRelevance(string fileName, string query)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(query))
                return 0.0;

            fileName = fileName.ToLower();
            query = query.ToLower();

            // Exact match
            if (fileName == query)
                return 1.0;

            // Filename starts with query
            if (fileName.StartsWith(query))
                return 0.95;

            // Query matches at start of filename (without extension)
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToLower();
            if (nameWithoutExt.StartsWith(query))
                return 0.90;

            // Contains query
            if (fileName.Contains(query))
                return 0.70;

            return 0.5;
        }

        /// <summary>
        /// Get all text files in a directory (for M3: indexing phase)
        /// </summary>
        public async Task<List<SearchResult>> GetTextFilesInDirectoryAsync(string directory)
        {
            return await Task.Run(() =>
            {
                var results = new List<SearchResult>();

                try
                {
                    if (!Directory.Exists(directory))
                        return results;

                    var textExtensions = new[] { ".txt", ".md", ".log", ".json", ".xml", ".cs", ".py", ".js", ".html", ".css", ".pdf", ".docx" };

                    var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                        .Where(f => textExtensions.Contains(Path.GetExtension(f).ToLower()));

                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            results.Add(new SearchResult
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                FileSize = fileInfo.Length,
                                FileType = fileInfo.Extension,
                                Modified = fileInfo.LastWriteTime,
                                RelevanceScore = 0.0,
                                SearchType = SearchType.Keyword
                            });
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[SERVICE] Directory scan error: {ex.Message}");
                }

                return results;
            });
        }

        /// <summary>
        /// Get file content for preview or M3: indexing
        /// </summary>
        public async Task<string> ReadFileContentAsync(string filePath, int maxLines = 500)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                        return string.Empty;

                    var lines = File.ReadLines(filePath).Take(maxLines);
                    return string.Join(Environment.NewLine, lines);
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        public void Dispose()
        {
            // Cleanup
        }
    }
}
