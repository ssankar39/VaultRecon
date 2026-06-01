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
        private readonly FileSystemService _fileSystemService = new();
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

        [ObservableProperty]
        private int selectedTab = 0; // 0 = Search, 1 = File Explorer

        [ObservableProperty]
        private ObservableCollection<DirectoryColumnViewModel> columns = new();

        [ObservableProperty]
        private SearchResult? selectedFileItem;

        [ObservableProperty]
        private string selectedFileContentPreview = string.Empty;

        [ObservableProperty]
        private bool isFilePreviewLoading = false;

        [ObservableProperty]
        private bool isSelectedFileText = false;

        private List<SearchResult> _cachedSearchResults = new();

        [ObservableProperty]
        private string currentSortColumn = "Score";

        [ObservableProperty]
        private bool isSortAscending = false;

        private SearchResult? _selectedSearchResult;
        public SearchResult? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set
            {
                if (SetProperty(ref _selectedSearchResult, value))
                {
                    if (value != null && value.FileType != "Folder" && !Directory.Exists(value.FilePath))
                    {
                        SelectedFileItem = value;
                        _ = LoadPreviewAsync(value.FilePath);
                    }
                    else
                    {
                        SelectedFileItem = null;
                        SelectedFileContentPreview = string.Empty;
                    }
                }
            }
        }

        public SearchViewModel()
        {
            _everythingService = new EverythingService();
            _embeddingService = new EmbeddingService();
            _indexService = new LanceDbIndexService(GetIndexPath());
            _backgroundIndexService = new BackgroundIndexService(_indexService, _embeddingService, GetRootPaths());
            _backgroundIndexService.StatusChanged += message => IndexStatusMessage = message;
            _backgroundIndexService.IndexingStateChanged += isRunning => IsIndexing = isRunning;
            InitializeAsync();
            _ = LoadInitialColumnsAsync();
            Log("[INIT] SearchViewModel initialized with M2 semantic pipeline and M5 Miller Columns");
        }

        private async void InitializeAsync()
        {
            try
            {
                StatusMessage = "Initializing AI system...";
                await _embeddingService.InitializeAsync(progress => StatusMessage = progress);
                StatusMessage = "AI system ready";
                await _backgroundIndexService.StartAsync();
                Log("[INIT] EmbeddingService initialized");
            }
            catch (Exception ex)
            {
                StatusMessage = $"AI error: {ex.Message}";
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

                if (query == "*")
                {
                    // Fall back to showing all files via keyword search
                    enhancedResults = await _everythingService.SearchByKeywordAsync(query, maxResults: 50);
                }
                else
                {
                    // 1. Get LanceDB vector results
                    var vectorResults = new List<SearchResult>();
                    if (_indexService.IsReady)
                    {
                        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query);
                        if (queryEmbedding != null)
                        {
                            vectorResults = await _indexService.SearchByVectorAsync(queryEmbedding, limit: 50);
                            Log($"[SEARCH] LanceDB search raw: {vectorResults.Count} results");
                        }
                    }

                    // 2. Get Keyword search results
                    var keywordResults = await _everythingService.SearchByKeywordAsync(query, maxResults: 50);
                    Log($"[SEARCH] Keyword search raw: {keywordResults.Count} results");

                    // 3. Separate keyword-only results from those already found via LanceDB
                    var keywordOnlyResults = keywordResults
                        .Where(kr => !vectorResults.Any(vr => string.Equals(vr.FilePath, kr.FilePath, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    // 4. Enhance the top keyword-only results on the fly
                    var topKeywordOnlyToEnhance = keywordOnlyResults.Take(12).ToList();
                    var remainingKeywordOnly = keywordOnlyResults.Skip(12).ToList();

                    var enhancedKeywordOnly = await EnhanceWithSemanticScoresAsync(topKeywordOnlyToEnhance, query);
                    var allKeywordOnlyBlended = enhancedKeywordOnly.Concat(remainingKeywordOnly).ToList();

                    // 5. Score and blend LanceDB vector results
                    var blendedVectorResults = new List<SearchResult>();
                    foreach (var vr in vectorResults)
                    {
                        // Look for a matching keyword result to get its precise keyword score
                        var matchingKr = keywordResults.FirstOrDefault(kr => string.Equals(kr.FilePath, vr.FilePath, StringComparison.OrdinalIgnoreCase));
                        
                        double keywordScore = matchingKr != null 
                            ? matchingKr.RelevanceScore 
                            : _everythingService.CalculateFilenameRelevance(vr.FileName, query);

                        double semanticScore = vr.RelevanceScore; // ScoreFromDistance is semantic similarity
                        vr.RelevanceScore = (0.7 * semanticScore) + (0.3 * keywordScore);
                        vr.SearchType = SearchType.Hybrid;

                        Log($"[SEARCH] LanceDB result {vr.FileName}: semantic={semanticScore:F2} keyword={keywordScore:F2} blended={vr.RelevanceScore:F2}");
                        blendedVectorResults.Add(vr);
                    }

                    // 6. Combine everything
                    var combinedResults = blendedVectorResults.Concat(allKeywordOnlyBlended).ToList();

                    // 7. No noise gate: retain all combined hybrid and content-matching search results
                    enhancedResults = combinedResults;

                    Log($"[SEARCH] Combined & filtered results: {enhancedResults.Count} (out of {combinedResults.Count} total)");
                }
                
                _cachedSearchResults = enhancedResults;
                ApplySorting();

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
                        string textToEmbed = "";

                        if (IsSupportedTextFile(result.FilePath))
                        {
                            string fileContent = await _everythingService.ReadFileContentAsync(result.FilePath, maxLines: 100);
                            if (!string.IsNullOrWhiteSpace(fileContent))
                            {
                                textToEmbed = fileContent;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(textToEmbed))
                        {
                            textToEmbed = Path.GetFileNameWithoutExtension(result.FileName);
                        }

                        // Prepend the file path and file name to include path context (Downloads, Documents, Pictures, etc.) in the semantic embedding
                        string fullTextToEmbed = $"File Path: {result.FilePath}\nFile Name: {result.FileName}\n\nContent:\n{textToEmbed}";
                        if (fullTextToEmbed.Length > 2000)
                        {
                            fullTextToEmbed = fullTextToEmbed.Substring(0, 2000);
                        }

                        // Get embedding
                        var fileEmbedding = await _embeddingService.GetEmbeddingAsync(fullTextToEmbed);
                        
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

        [RelayCommand]
        public void SelectSearchTab()
        {
            SelectedTab = 0;
            Log("[NAV] Switched to Search tab");
        }

        [RelayCommand]
        public void SelectExplorerTab()
        {
            SelectedTab = 1;
            Log("[NAV] Switched to Explorer tab");
        }

        [RelayCommand]
        public void SortResults(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return;

            if (CurrentSortColumn == columnName)
            {
                IsSortAscending = !IsSortAscending;
            }
            else
            {
                CurrentSortColumn = columnName;
                IsSortAscending = true;
            }

            Log($"[SORT] Sorting by {columnName} (Ascending={IsSortAscending})");
            ApplySorting();
        }

        private void ApplySorting()
        {
            if (_cachedSearchResults == null || _cachedSearchResults.Count == 0) return;

            IEnumerable<SearchResult> sorted = _cachedSearchResults;

            switch (CurrentSortColumn)
            {
                case "Name":
                    sorted = IsSortAscending 
                        ? sorted.OrderBy(r => r.FileName) 
                        : sorted.OrderByDescending(r => r.FileName);
                    break;
                case "Date modified":
                    sorted = IsSortAscending 
                        ? sorted.OrderBy(r => r.Modified) 
                        : sorted.OrderByDescending(r => r.Modified);
                    break;
                case "Type":
                    sorted = IsSortAscending 
                        ? sorted.OrderBy(r => r.FileType) 
                        : sorted.OrderByDescending(r => r.FileType);
                    break;
                case "Size":
                    sorted = IsSortAscending 
                        ? sorted.OrderBy(r => r.FileSize) 
                        : sorted.OrderByDescending(r => r.FileSize);
                    break;
                case "Score":
                default:
                    sorted = IsSortAscending 
                        ? sorted.OrderBy(r => r.RelevanceScore) 
                        : sorted.OrderByDescending(r => r.RelevanceScore);
                    break;
            }

            SearchResults.Clear();
            foreach (var result in sorted.Take(50))
            {
                SearchResults.Add(result);
            }
            
            StatusMessage = $"Sorted by {CurrentSortColumn} {(IsSortAscending ? "▲" : "▼")} - Found {SearchResults.Count} results";
        }

        public void Cleanup()
        {
            _backgroundIndexService?.Dispose();
            _embeddingService?.Dispose();
            _everythingService?.Dispose();
        }

        [RelayCommand]
        public void OpenFile()
        {
            if (SelectedFileItem != null)
            {
                try
                {
                    string path = SelectedFileItem.FilePath;
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to open: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void OpenFolder()
        {
            if (SelectedFileItem != null)
            {
                try
                {
                    string path = SelectedFileItem.FilePath;
                    string folder = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
                    if (Directory.Exists(folder))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to open folder: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void CopyPath()
        {
            if (SelectedFileItem != null)
            {
                try
                {
                    System.Windows.Clipboard.SetText(SelectedFileItem.FilePath);
                    StatusMessage = "Path copied to clipboard!";
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to copy path: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public async Task DeleteFile()
        {
            if (SelectedFileItem != null)
            {
                try
                {
                    string path = SelectedFileItem.FilePath;
                    bool success = false;
                    
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        success = true;
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        success = true;
                    }

                    if (success)
                    {
                        StatusMessage = $"Deleted: {Path.GetFileName(path)}";
                        Log($"[SYSTEM] Deleted: {path}");
                        
                        // Force refresh of explorer view / search results
                        if (SelectedTab == 0)
                        {
                            await PerformSearch();
                        }
                        else
                        {
                            // Reload active column elements
                            if (Columns.Count > 0)
                            {
                                var col = Columns.Last();
                                if (Directory.Exists(col.Path))
                                {
                                    var subItems = await _fileSystemService.GetDirectoryContentsAsync(col.Path);
                                    col.Items.Clear();
                                    foreach (var sub in subItems)
                                    {
                                        col.Items.Add(sub);
                                    }
                                }
                            }
                        }
                        SelectedFileItem = null;
                        SelectedFileContentPreview = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to delete: {ex.Message}");
                    StatusMessage = $"Delete failed: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void RunAsAdmin()
        {
            if (SelectedFileItem != null && File.Exists(SelectedFileItem.FilePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SelectedFileItem.FilePath,
                        Verb = "runas",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to run as admin: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void EditInNotepad()
        {
            if (SelectedFileItem != null && File.Exists(SelectedFileItem.FilePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "notepad.exe",
                        Arguments = $"\"{SelectedFileItem.FilePath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to edit in Notepad: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void ShowProperties()
        {
            if (SelectedFileItem != null)
            {
                try
                {
                    string path = SelectedFileItem.FilePath;
                    var info = File.Exists(path) ? (FileSystemInfo)new FileInfo(path) : new DirectoryInfo(path);
                    string msg = $"Name: {info.Name}\n" +
                                 $"Path: {info.FullName}\n" +
                                 $"Created: {info.CreationTime}\n" +
                                 $"Modified: {info.LastWriteTime}\n" +
                                 $"Attributes: {info.Attributes}";
                    if (info is FileInfo)
                    {
                        msg += $"\nSize: {SelectedFileItem.FormattedSize}";
                    }
                    
                    System.Windows.MessageBox.Show(msg, "Properties", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log($"[SYSTEM] Failed to show properties: {ex.Message}");
                }
            }
        }

        [ObservableProperty]
        private string activeFolderName = "sarve";

        [ObservableProperty]
        private string activeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        [ObservableProperty]
        private string activeFolderItemCount = "0 items";

        [ObservableProperty]
        private string activeFolderTotalSize = "";

        [RelayCommand]
        public async Task NavigateDocuments() => await NavigateToFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        [RelayCommand]
        public async Task NavigatePictures() => await NavigateToFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

        [RelayCommand]
        public async Task NavigateDesktop() => await NavigateToFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

        [RelayCommand]
        public async Task NavigateDownloads() => await NavigateToFolderAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        private async Task NavigateToFolderAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            Columns.Clear();

            var rootCol = new DirectoryColumnViewModel
            {
                Path = "QuickAccess",
                Name = "Quick Access",
                OnSelectionChanged = OnColumnSelectionChanged
            };

            var paths = GetRootPaths();
            var labels = new[] { "Documents", "Pictures", "Desktop", "Downloads" };

            FileItem? selectedItemToLoad = null;

            for (int i = 0; i < paths.Length; i++)
            {
                var p = paths[i];
                var name = labels[i];
                if (Directory.Exists(p))
                {
                    var dirInfo = new DirectoryInfo(p);
                    var item = new FileItem
                    {
                        Name = name,
                        Path = p,
                        IsDirectory = true,
                        Modified = dirInfo.LastWriteTime,
                        FileType = "Folder"
                    };
                    rootCol.Items.Add(item);

                    if (string.Equals(p, folderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedItemToLoad = item;
                    }
                }
            }

            Columns.Add(rootCol);

            if (selectedItemToLoad != null)
            {
                rootCol.SelectedItem = selectedItemToLoad;
            }
            
            UpdateActiveFolderDetails();
        }

        private void UpdateActiveFolderDetails()
        {
            try
            {
                var lastRealFolderCol = Columns.LastOrDefault(c => c.Path != "QuickAccess");
                if (lastRealFolderCol != null)
                {
                    ActiveFolderName = lastRealFolderCol.Name;
                    ActiveFolderPath = lastRealFolderCol.Path;
                    int itemsCount = lastRealFolderCol.Items.Count;
                    ActiveFolderItemCount = $"{itemsCount} items";

                    long totalBytes = lastRealFolderCol.Items.Where(i => !i.IsDirectory).Sum(i => i.Size);
                    ActiveFolderTotalSize = FormatBytes(totalBytes);
                }
                else
                {
                    ActiveFolderName = "sarve";
                    ActiveFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    ActiveFolderItemCount = "4 items";
                    ActiveFolderTotalSize = "";
                }
            }
            catch (Exception ex)
            {
                Log($"[EXPLORER] Error updating folder details: {ex.Message}");
            }
        }

        private async Task LoadInitialColumnsAsync()
        {
            try
            {
                var rootCol = new DirectoryColumnViewModel
                {
                    Path = "QuickAccess",
                    Name = "Quick Access",
                    OnSelectionChanged = OnColumnSelectionChanged
                };

                var paths = GetRootPaths();
                var labels = new[] { "Documents", "Pictures", "Desktop", "Downloads" };

                for (int i = 0; i < paths.Length; i++)
                {
                    var p = paths[i];
                    var name = labels[i];
                    if (Directory.Exists(p))
                    {
                        var dirInfo = new DirectoryInfo(p);
                        rootCol.Items.Add(new FileItem
                        {
                            Name = name,
                            Path = p,
                            IsDirectory = true,
                            Modified = dirInfo.LastWriteTime,
                            FileType = "Folder"
                        });
                    }
                }

                Columns.Add(rootCol);
                UpdateActiveFolderDetails();
            }
            catch (Exception ex)
            {
                Log($"[EXPLORER] Failed to load initial column: {ex.Message}");
            }
        }

        private async void OnColumnSelectionChanged(DirectoryColumnViewModel column, FileItem? selectedItem)
        {
            try
            {
                int index = Columns.IndexOf(column);
                if (index == -1) return;

                // Clear columns after this one
                while (Columns.Count > index + 1)
                {
                    Columns.RemoveAt(Columns.Count - 1);
                }

                if (selectedItem == null)
                {
                    SelectedFileItem = null;
                    SelectedFileContentPreview = string.Empty;
                    UpdateActiveFolderDetails();
                    return;
                }

                if (selectedItem.IsDirectory)
                {
                    SelectedFileItem = null;
                    SelectedFileContentPreview = string.Empty;

                    // Load sub-directory items
                    var subItems = await _fileSystemService.GetDirectoryContentsAsync(selectedItem.Path);
                    var newCol = new DirectoryColumnViewModel
                    {
                        Path = selectedItem.Path,
                        Name = selectedItem.Name,
                        OnSelectionChanged = OnColumnSelectionChanged
                    };
                    
                    foreach (var sub in subItems)
                    {
                        newCol.Items.Add(sub);
                    }

                    Columns.Add(newCol);
                }
                else
                {
                    // It is a file! Show preview
                    var result = new SearchResult
                    {
                        FilePath = selectedItem.Path,
                        FileName = selectedItem.Name,
                        FileSize = selectedItem.Size,
                        Modified = selectedItem.Modified,
                        FileType = selectedItem.FileType,
                        SearchType = SearchType.Keyword, // Default exploration
                        RelevanceScore = 1.0
                    };
                    
                    SelectedFileItem = result;
                    await LoadPreviewAsync(selectedItem.Path);
                }
                
                UpdateActiveFolderDetails();
            }
            catch (Exception ex)
            {
                Log($"[EXPLORER] Error in selection changed: {ex.Message}");
            }
        }

        private async Task LoadPreviewAsync(string filePath)
        {
            IsFilePreviewLoading = true;
            SelectedFileContentPreview = "Loading preview...";
            IsSelectedFileText = IsSupportedTextFile(filePath);
            
            try
            {
                if (IsSelectedFileText)
                {
                    string content = await Task.Run(() =>
                    {
                        try
                        {
                            if (!File.Exists(filePath))
                                return "File does not exist.";

                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var reader = new StreamReader(fs))
                            {
                                var lines = new List<string>();
                                for (int i = 0; i < 100; i++)
                                {
                                    var line = reader.ReadLine();
                                    if (line == null) break;
                                    lines.Add(line);
                                }
                                return string.Join(Environment.NewLine, lines);
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"Error reading file content: {ex.Message}";
                        }
                    });
                    SelectedFileContentPreview = content;
                }
                else
                {
                    SelectedFileContentPreview = string.Empty;
                }
            }
            catch (Exception ex)
            {
                SelectedFileContentPreview = $"Failed to generate preview: {ex.Message}";
                IsSelectedFileText = false;
            }
            finally
            {
                IsFilePreviewLoading = false;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F1} {sizes[order]}";
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
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };
        }

        private static bool IsSupportedTextFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".py" or ".js" or ".html" or ".css";
        }
    }
}
