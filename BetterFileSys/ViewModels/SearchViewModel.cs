using BetterFileSys.Models;
using BetterFileSys.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterFileSys.ViewModels
{
    /// <summary>
    /// ViewModel for hybrid search (keyword + semantic)
    /// M2: Wired up for semantic embedding pipeline
    /// </summary>
    public partial class SearchViewModel : ObservableObject
    {
        private readonly EverythingService _everythingService;
        private readonly EmbeddingService _embeddingService;
        private readonly LanceDbIndexService _indexService;
        private readonly BackgroundIndexService _backgroundIndexService;
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

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SearchResult> searchResults = new();

        [ObservableProperty]
        private bool isSearching = false;

        [ObservableProperty]
        private string statusMessage = "Ready";

        [ObservableProperty]
        private string indexStatusMessage = "Indexing: idle";

        [ObservableProperty]
        private bool isIndexing = false;

        public SearchViewModel()
        {
            _everythingService = new EverythingService();
            _embeddingService = new EmbeddingService();
            _indexService = new LanceDbIndexService(GetIndexPath());
            _backgroundIndexService = new BackgroundIndexService(_indexService, _embeddingService, GetRootPaths());
            _backgroundIndexService.StatusChanged += message => IndexStatusMessage = message;
            _backgroundIndexService.IndexingStateChanged += isRunning => IsIndexing = isRunning;
            InitializeAsync();
            Log("[INIT] SearchViewModel initialized with M2 semantic pipeline");
        }

        private async void InitializeAsync()
        {
            try
            {
                await _embeddingService.InitializeAsync();
                await _backgroundIndexService.StartAsync();
                Log("[INIT] EmbeddingService initialized");
            }
            catch (Exception ex)
            {
                Log($"[INIT] EmbeddingService error: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task PerformSearch()
        {
            Log($"[SEARCH] PerformSearch called with query: '{SearchQuery}'");

            IsSearching = true;
            StatusMessage = "Searching...";
            Log("[SEARCH] Hybrid search started");

            _backgroundIndexService.PauseIndexing();

            try
            {
                string query = string.IsNullOrWhiteSpace(SearchQuery) ? "*" : SearchQuery;
                Log($"[SEARCH] Query: {query}");

                var enhancedResults = new List<SearchResult>();

                if (_indexService.IsReady && query != "*")
                {
                    var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);

                    if (queryEmbedding != null)
                    {
                        enhancedResults = await _indexService.SearchByVectorAsync(queryEmbedding, limit: 50);
                        Log($"[SEARCH] LanceDB search: {enhancedResults.Count} results");
                    }
                }

                if (enhancedResults.Count == 0)
                {
                    // M1: Keyword search (fast, filename-based)
                    var keywordResults = await _everythingService.SearchByKeywordAsync(query, maxResults: 50);
                    Log($"[SEARCH] Keyword search: {keywordResults.Count} results");

                    if (!IsIndexing && query != "*" && keywordResults.Count > 0)
                    {
                        // M2: Semantic search - enhance top results only when indexer is idle
                        enhancedResults = await EnhanceWithSemanticScoresAsync(keywordResults.Take(12).ToList(), query);
                    }
                    else
                    {
                        enhancedResults = keywordResults;
                    }
                }
                
                SearchResults.Clear();
                foreach (var result in enhancedResults.Take(50))
                {
                    SearchResults.Add(result);
                }

                StatusMessage = $"Found {SearchResults.Count} results";
                Log($"[SEARCH] Complete: {StatusMessage}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Log($"[SEARCH] Error: {ex}");
            }
            finally
            {
                IsSearching = false;
                _backgroundIndexService.ResumeIndexing();
            }
        }

        /// <summary>
        /// M2: Enhance keyword results with semantic scoring
        /// Attempts to compute embeddings for query and file content
        /// Falls back to keyword-only if model unavailable
        /// </summary>
        private async Task<List<SearchResult>> EnhanceWithSemanticScoresAsync(List<SearchResult> keywordResults, string query)
        {
            try
            {
                // Try to get query embedding
                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);
                
                if (queryEmbedding == null)
                {
                    Log("[SEARCH] Semantic search unavailable - using keyword results only");
                    return keywordResults;
                }

                Log("[SEARCH] Computing semantic scores for keyword results");

                var enhancedResults = new List<SearchResult>();

                foreach (var result in keywordResults)
                {
                    try
                    {
                        // Read first 100 lines of file for semantic comparison
                        string fileContent = await _everythingService.ReadFileContentAsync(result.FilePath, maxLines: 100);
                        
                        if (string.IsNullOrWhiteSpace(fileContent))
                        {
                            // No content available, keep keyword score
                            result.SearchType = SearchType.Keyword;
                            enhancedResults.Add(result);
                            continue;
                        }

                        // Get file content embedding
                        var fileEmbedding = await _embeddingService.GetEmbeddingAsync(fileContent);
                        
                        if (fileEmbedding != null)
                        {
                            // Calculate semantic similarity (0-1)
                            double semanticScore = EmbeddingService.CosineSimilarity(queryEmbedding, fileEmbedding);
                            
                            // Blend: 70% semantic + 30% keyword match
                            double keywordScore = result.RelevanceScore;
                            result.RelevanceScore = (0.7 * semanticScore) + (0.3 * keywordScore);
                            result.SearchType = SearchType.Hybrid;
                            
                            Log($"[SEARCH] {result.FileName}: keyword={keywordScore:F2} semantic={semanticScore:F2} blended={result.RelevanceScore:F2}");
                        }
                        else
                        {
                            result.SearchType = SearchType.Keyword;
                        }

                        enhancedResults.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Log($"[SEARCH] Error computing semantic score for {result.FileName}: {ex.Message}");
                        result.SearchType = SearchType.Keyword;
                        enhancedResults.Add(result);
                    }
                }

                // Sort by blended score
                return enhancedResults.OrderByDescending(r => r.RelevanceScore).ToList();
            }
            catch (Exception ex)
            {
                Log($"[SEARCH] Semantic enhancement error: {ex.Message}");
                return keywordResults;
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchQuery = string.Empty;
            SearchResults.Clear();
            StatusMessage = "Ready";
            Log("[SEARCH] Search cleared");
        }

        public void Cleanup()
        {
            _everythingService?.Dispose();
            _embeddingService?.Dispose();
            _backgroundIndexService?.Dispose();
        }

        private static string GetIndexPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "BetterFileSys", "index");
        }

        private static string[] GetRootPaths()
        {
            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
        }
    }
}
