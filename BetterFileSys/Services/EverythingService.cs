using BetterFileSys.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    /// <summary>
    /// Service for fast file search (Phase 1: File System based, Phase 2: Everything SDK integration)
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
        /// Search for files by keyword (filename match)
        /// Currently using System.IO; will integrate Everything SDK in Phase 1b
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

                    // Phase 1a: Use Documents folder as search root
                    string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    Log($"[SERVICE] Searching in: {documentsPath}");

                    if (!Directory.Exists(documentsPath))
                    {
                        Log($"[SERVICE] Documents folder does not exist: {documentsPath}");
                        return results;
                    }

                    // Recursively search with permission error handling
                    SearchDirectory(documentsPath, $"*{query}*", results, maxResults, ref results);

                    Log($"[SERVICE] Search complete. Returned {results.Count} results");
                }
                catch (Exception ex)
                {
                    Log($"[SERVICE] Search error: {ex}");
                }

                return results;
            });
        }

        private void SearchDirectory(string path, string pattern, List<SearchResult> results, int maxResults, ref List<SearchResult> resultList)
        {
            try
            {
                if (results.Count >= maxResults)
                    return;

                var files = Directory.GetFiles(path, pattern);

                int rank = 0;
                foreach (var file in files)
                {
                    if (results.Count >= maxResults)
                        break;

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
                            RelevanceScore = 1.0 - (rank / (double)maxResults),
                            SearchType = SearchType.Keyword
                        });
                        Log($"[SERVICE] Added: {fileInfo.Name}");
                        rank++;
                    }
                    catch (Exception ex)
                    {
                        Log($"[SERVICE] Error processing file {file}: {ex.Message}");
                    }
                }

                // Recursively search subdirectories
                try
                {
                    var directories = Directory.GetDirectories(path);
                    foreach (var dir in directories)
                    {
                        if (results.Count >= maxResults)
                            break;

                        SearchDirectory(dir, pattern, results, maxResults, ref resultList);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log($"[SERVICE] Access denied to: {path}");
                }
                catch (Exception ex)
                {
                    Log($"[SERVICE] Error enumerating directory {path}: {ex.Message}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"[SERVICE] Access denied to: {path}");
            }
            catch (Exception ex)
            {
                Log($"[SERVICE] Error searching directory {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all text files in a directory (for Phase 2 indexing)
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

                    var textExtensions = new[] { ".txt", ".md", ".log", ".json", ".xml", ".cs", ".py", ".js", ".html", ".css" };

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
                    System.Diagnostics.Debug.WriteLine($"Directory scan error: {ex.Message}");
                }

                return results;
            });
        }

        /// <summary>
        /// Get file content for preview or Phase 2 indexing
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
            // Cleanup if needed (Everything SDK integration will go here in Phase 1b)
        }
    }
}
