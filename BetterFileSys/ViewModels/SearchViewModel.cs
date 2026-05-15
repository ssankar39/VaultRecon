using BetterFileSys.Models;
using BetterFileSys.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace BetterFileSys.ViewModels
{
    /// <summary>
    /// ViewModel for search operations and results
    /// </summary>
    public partial class SearchViewModel : ObservableObject
    {
        private readonly EverythingService _everythingService;
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

        public SearchViewModel()
        {
            _everythingService = new EverythingService();
            Log("[INIT] SearchViewModel initialized");
        }

        [RelayCommand]
        public async Task PerformSearch()
        {
            Log($"[SEARCH] PerformSearch called with query: '{SearchQuery}'");

            IsSearching = true;
            StatusMessage = "Searching...";
            Log("[SEARCH] Search started");

            try
            {
                // Treat empty query as "show all files"
                string query = string.IsNullOrWhiteSpace(SearchQuery) ? "*" : SearchQuery;
                Log($"[SEARCH] Searching for: {query}");
                var results = await _everythingService.SearchByKeywordAsync(query, maxResults: 50);
                Log($"[SEARCH] Got {results.Count} results from service");
                
                SearchResults.Clear();

                foreach (var result in results)
                {
                    SearchResults.Add(result);
                    Log($"[SEARCH] Added result: {result.FileName}");
                }

                StatusMessage = $"Found {SearchResults.Count} results";
                Log($"[SEARCH] Status updated: {StatusMessage}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Log($"[SEARCH] Search error: {ex}");
            }
            finally
            {
                IsSearching = false;
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
        }
    }
}
