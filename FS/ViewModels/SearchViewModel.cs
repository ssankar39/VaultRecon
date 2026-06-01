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
        private int selectedTab = 1; // 0 = Search, 1 = File Explorer

        [ObservableProperty]
        private bool isSearchOverlayOpen = false;

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

        [ObservableProperty]
        private string driveFreeSpaceText = "--- GB";

        [ObservableProperty]
        private double drivePercentUsed = 0;

        [ObservableProperty]
        private ObservableCollection<DriveItemViewModel> drives = new();

        [ObservableProperty]
        private string currentUserName = Environment.UserName;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? desktopIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? documentsIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? picturesIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? videosIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? musicIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? downloadsIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? programsIcon;

        [ObservableProperty]
        private System.Windows.Media.ImageSource? recycleBinIcon;

        [ObservableProperty]
        private ObservableCollection<FileItem> historyItems = new();

        [ObservableProperty]
        private ObservableCollection<BreadcrumbSegment> breadcrumbs = new();

        [ObservableProperty]
        private bool isEditingPath = false;

        [ObservableProperty]
        private string viewStyle = "Details"; // "Details", "Grid", "List"

        [ObservableProperty]
        private string editablePath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SidebarGroup> sidebarGroups = new();

        [ObservableProperty]
        private bool canNavigateBack = false;

        [ObservableProperty]
        private bool canNavigateForward = false;

        private string? _currentPathBeforeNavigation;
        private bool _isNavigatingInternally = false;
        private readonly List<string> _backStack = new();
        private readonly List<string> _forwardStack = new();

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
            InitializeSidebarIcons();
            InitializeSidebarGroups();
            _ = LoadInitialColumnsAsync();
            UpdateDriveSpaceDetails();
            LoadDrives();
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
            IsSearchOverlayOpen = true;
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
        public void ToggleSearchOverlay()
        {
            IsSearchOverlayOpen = !IsSearchOverlayOpen;
            Log($"[NAV] Toggled Search Overlay to {IsSearchOverlayOpen}");
        }

        [RelayCommand]
        public void CloseSearchOverlay()
        {
            IsSearchOverlayOpen = false;
            Log("[NAV] Closed Search Overlay");
        }

        [RelayCommand]
        public void StartEditPath()
        {
            EditablePath = ActiveFolderPath;
            IsEditingPath = true;
            Log("[BREADCRUMB] Started editing path");
        }

        [RelayCommand]
        public void CancelEditPath()
        {
            IsEditingPath = false;
            EditablePath = ActiveFolderPath;
            Log("[BREADCRUMB] Cancelled editing path");
        }

        [RelayCommand]
        public async Task SubmitPath()
        {
            if (string.IsNullOrWhiteSpace(EditablePath)) return;
            
            if (Directory.Exists(EditablePath))
            {
                await NavigateToFolderAsync(EditablePath);
                IsEditingPath = false;
            }
            else
            {
                System.Windows.MessageBox.Show($"Path does not exist: {EditablePath}", "Invalid Path", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateNewGroup()
        {
            string groupName = "New Group";
            try
            {
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter a name for the new sidebar group:",
                    "New Sidebar Group",
                    "New Group"
                );
                if (!string.IsNullOrWhiteSpace(input))
                {
                    groupName = input.Trim();
                }
                else
                {
                    return; // User cancelled
                }
            }
            catch
            {
                int count = SidebarGroups.Count(g => g.Name.StartsWith("New Group"));
                groupName = count == 0 ? "New Group" : $"New Group ({count + 1})";
            }
            
            var newGroup = new SidebarGroup { Name = groupName };
            SidebarGroups.Add(newGroup);
            Log($"[SIDEBAR] Created new group '{groupName}'");
        }

        [RelayCommand]
        public void RenameGroup(SidebarGroup group)
        {
            if (group == null) return;
            try
            {
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Enter a new name for the group '{group.Name}':",
                    "Rename Sidebar Group",
                    group.Name
                );
                
                if (!string.IsNullOrWhiteSpace(input))
                {
                    group.Name = input.Trim();
                    Log($"[SIDEBAR] Renamed group to '{group.Name}'");
                }
            }
            catch (Exception ex)
            {
                Log($"[SIDEBAR] Error renaming group: {ex.Message}");
            }
        }

        [RelayCommand]
        public void DeleteGroup(SidebarGroup group)
        {
            if (group == null) return;
            if (SidebarGroups.Contains(group))
            {
                SidebarGroups.Remove(group);
                Log($"[SIDEBAR] Deleted group '{group.Name}'");
            }
        }

        [RelayCommand]
        public void AddShortcutToGroup(SidebarGroup group)
        {
            if (group == null) return;
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = $"Select Folder to Add to {group.Name}",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                
                if (dialog.ShowDialog() == true)
                {
                    string selectedPath = dialog.FolderName;
                    if (Directory.Exists(selectedPath))
                    {
                        var di = new DirectoryInfo(selectedPath);
                        var item = new FileItem
                        {
                            Name = di.Name,
                            Path = selectedPath,
                            IsDirectory = true,
                            Modified = di.LastWriteTime,
                            FileType = "Folder"
                        };
                        
                        group.Items.Add(item);
                        Log($"[SIDEBAR] Added shortcut '{item.Name}' to group '{group.Name}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SIDEBAR] Error adding shortcut to group: {ex.Message}");
            }
        }

        [RelayCommand]
        public void RemoveShortcut(FileItem item)
        {
            if (item == null) return;
            foreach (var group in SidebarGroups)
            {
                if (group.Items.Contains(item))
                {
                    group.Items.Remove(item);
                    Log($"[SIDEBAR] Removed shortcut '{item.Name}' from group '{group.Name}'");
                    break;
                }
            }
        }

        [RelayCommand]
        public void PinFolderToSidebar(object? itemObj)
        {
            try
            {
                string? path = null;
                string? name = null;
                
                if (itemObj is FileItem fileItem && fileItem.IsDirectory)
                {
                    path = fileItem.Path;
                    name = fileItem.Name;
                }
                else if (itemObj is SearchResult searchResult && searchResult.FileType == "Folder")
                {
                    path = searchResult.FilePath;
                    name = searchResult.FileName;
                }
                
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    var targetGroup = SidebarGroups.FirstOrDefault();
                    if (targetGroup != null)
                    {
                        if (targetGroup.Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                        {
                            System.Windows.MessageBox.Show("This folder is already pinned to your sidebar.", "Pin to Sidebar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                            return;
                        }
                        
                        var item = new FileItem
                        {
                            Name = name ?? Path.GetFileName(path),
                            Path = path,
                            IsDirectory = true
                        };
                        targetGroup.Items.Add(item);
                        Log($"[SIDEBAR] Pinned folder '{item.Name}' to group '{targetGroup.Name}'");
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("Only folders can be pinned to the sidebar.", "Pin to Sidebar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"[SIDEBAR] Error pinning folder to sidebar: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task OpenFileFromItem(FileItem item)
        {
            if (item == null) return;
            if (item.IsDirectory)
            {
                await NavigateToFolderAsync(item.Path);
            }
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.Path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"[EXPLORER] Error opening file: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public void SetViewStyle(string style)
        {
            ViewStyle = style;
            Log($"[VIEW] Switched explorer view style to {style}");
        }

        [RelayCommand]
        public void SetSortDirection(bool ascending)
        {
            IsSortAscending = ascending;
            Log($"[SORT] Set sort direction to Ascending={ascending}");
            ApplySorting();
            SortExplorerColumn();
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
            SortExplorerColumn();
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

        private void SortExplorerColumn()
        {
            try
            {
                var activeCol = Columns.LastOrDefault();
                if (activeCol != null)
                {
                    List<FileItem> sortedItems;
                    switch (CurrentSortColumn)
                    {
                        case "Name":
                            sortedItems = IsSortAscending 
                                ? activeCol.Items.OrderBy(i => i.Name).ToList() 
                                : activeCol.Items.OrderByDescending(i => i.Name).ToList();
                            break;
                        case "Date modified":
                            sortedItems = IsSortAscending 
                                ? activeCol.Items.OrderBy(i => i.Modified).ToList() 
                                : activeCol.Items.OrderByDescending(i => i.Modified).ToList();
                            break;
                        case "Type":
                            sortedItems = IsSortAscending 
                                ? activeCol.Items.OrderBy(i => i.FileType).ToList() 
                                : activeCol.Items.OrderByDescending(i => i.FileType).ToList();
                            break;
                        case "Size":
                            sortedItems = IsSortAscending 
                                ? activeCol.Items.OrderBy(i => i.Size).ToList() 
                                : activeCol.Items.OrderByDescending(i => i.Size).ToList();
                            break;
                        default:
                            sortedItems = activeCol.Items.ToList();
                            break;
                    }

                    activeCol.Items.Clear();
                    foreach (var item in sortedItems)
                    {
                        activeCol.Items.Add(item);
                    }
                    Log($"[SORT] Sorted active explorer column by {CurrentSortColumn} (Ascending={IsSortAscending})");
                }
            }
            catch (Exception ex)
            {
                Log($"[SORT] Error sorting explorer column: {ex.Message}");
            }
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
        private string activeFolderName = Environment.UserName;

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

        [RelayCommand]
        public async Task NavigateToFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            await NavigateToFolderAsync(folderPath);
        }

        private async Task NavigateToFolderAsync(string folderPath)
        {
            IsSearchOverlayOpen = false;
            if (folderPath == "QuickAccess")
            {
                RecordNavigation("QuickAccess");
                await LoadInitialColumnsAsync();
                return;
            }

            if (!Directory.Exists(folderPath)) return;

            // Record navigation step
            RecordNavigation(folderPath);

            Columns.Clear();

            var di = new DirectoryInfo(folderPath);
            var col = new DirectoryColumnViewModel
            {
                Path = folderPath,
                Name = string.IsNullOrEmpty(di.Parent?.FullName) ? folderPath : di.Name,
                OnSelectionChanged = OnColumnSelectionChanged
            };

            var subItems = await _fileSystemService.GetDirectoryContentsAsync(folderPath);
            foreach (var sub in subItems)
            {
                col.Items.Add(sub);
            }

            Columns.Add(col);
            UpdateActiveFolderDetails();
            SortExplorerColumn(); // Immediately apply sorting to the newly loaded folder!
        }

        private void RecordNavigation(string newPath)
        {
            if (_isNavigatingInternally) return;

            if (!string.IsNullOrEmpty(_currentPathBeforeNavigation) && _currentPathBeforeNavigation != newPath)
            {
                _backStack.Add(_currentPathBeforeNavigation);
                _forwardStack.Clear();
                CanNavigateBack = _backStack.Count > 0;
                CanNavigateForward = _forwardStack.Count > 0;
            }
            _currentPathBeforeNavigation = newPath;

            AddToHistory(newPath);
        }

        private void AddToHistory(string path)
        {
            if (!Directory.Exists(path)) return;
            if (path == "QuickAccess") return;

            var existing = HistoryItems.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                HistoryItems.Remove(existing);
            }

            var di = new DirectoryInfo(path);
            var item = new FileItem
            {
                Name = string.IsNullOrEmpty(di.Parent?.FullName) ? path : di.Name,
                Path = path,
                IsDirectory = true,
                Modified = di.LastWriteTime,
                FileType = "Folder"
            };

            HistoryItems.Insert(0, item);

            while (HistoryItems.Count > 8)
            {
                HistoryItems.RemoveAt(HistoryItems.Count - 1);
            }
        }

        [RelayCommand]
        public async Task NavigateBack()
        {
            if (_backStack.Count == 0) return;

            string prevPath = _backStack[_backStack.Count - 1];
            _backStack.RemoveAt(_backStack.Count - 1);

            if (Directory.Exists(ActiveFolderPath))
            {
                _forwardStack.Add(ActiveFolderPath);
            }

            _isNavigatingInternally = true;
            try
            {
                if (prevPath == "QuickAccess")
                {
                    _currentPathBeforeNavigation = prevPath;
                    await LoadInitialColumnsAsync();
                }
                else
                {
                    await NavigateToFolderAsync(prevPath);
                    _currentPathBeforeNavigation = prevPath;
                }
            }
            finally
            {
                _isNavigatingInternally = false;
                CanNavigateBack = _backStack.Count > 0;
                CanNavigateForward = _forwardStack.Count > 0;
            }
        }

        [RelayCommand]
        public async Task NavigateForward()
        {
            if (_forwardStack.Count == 0) return;

            string nextPath = _forwardStack[_forwardStack.Count - 1];
            _forwardStack.RemoveAt(_forwardStack.Count - 1);

            if (Directory.Exists(ActiveFolderPath))
            {
                _backStack.Add(ActiveFolderPath);
            }

            _isNavigatingInternally = true;
            try
            {
                if (nextPath == "QuickAccess")
                {
                    _currentPathBeforeNavigation = nextPath;
                    await LoadInitialColumnsAsync();
                }
                else
                {
                    await NavigateToFolderAsync(nextPath);
                    _currentPathBeforeNavigation = nextPath;
                }
            }
            finally
            {
                _isNavigatingInternally = false;
                CanNavigateBack = _backStack.Count > 0;
                CanNavigateForward = _forwardStack.Count > 0;
            }
        }

        [RelayCommand]
        public async Task NavigateUp()
        {
            if (string.IsNullOrEmpty(ActiveFolderPath) || ActiveFolderPath == "QuickAccess") return;

            var parent = Path.GetDirectoryName(ActiveFolderPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                await NavigateToFolderAsync(parent);
            }
        }

        private void UpdateDriveSpaceDetails()
        {
            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    double freeGb = drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double usedGb = totalGb - freeGb;
                    double percent = (usedGb / totalGb) * 100;

                    DriveFreeSpaceText = $"{freeGb:F0} GB free";
                    DrivePercentUsed = percent;
                }
            }
            catch (Exception ex)
            {
                Log($"[SYSTEM] Failed to read drive space: {ex.Message}");
                DriveFreeSpaceText = "759 GB";
                DrivePercentUsed = 72;
            }
        }

        [RelayCommand]
        public void LoadDrives()
        {
            try
            {
                Drives.Clear();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        double freeGb = drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                        double usedGb = totalGb - freeGb;
                        double percent = (usedGb / totalGb) * 100;

                        Drives.Add(new DriveItemViewModel
                        {
                            Name = $"{drive.Name} Disk",
                            Path = drive.Name,
                            FreeSpaceText = $"{freeGb:F0} GB free",
                            PercentUsed = percent
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SYSTEM] Failed to load drives: {ex.Message}");
                Drives.Add(new DriveItemViewModel
                {
                    Name = "C: Main Disk",
                    Path = "C:\\",
                    FreeSpaceText = "759 GB free",
                    PercentUsed = 72
                });
            }
        }

        [RelayCommand]
        public async Task NavigateVideos() => await NavigateToFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        [RelayCommand]
        public async Task NavigateMusic() => await NavigateToFolderAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        [RelayCommand]
        public void NavigateRecycleBin()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "shell:RecycleBinFolder");
                StatusMessage = "Opened Recycle Bin";
            }
            catch (Exception ex)
            {
                Log($"[SYSTEM] Failed to open Recycle Bin: {ex.Message}");
            }
        }

        [RelayCommand]
        public void ShowAbout()
        {
            System.Windows.MessageBox.Show(
                "VaultRecon v1.0\n\n" +
                "A highly optimized, fully local hybrid search engine & file explorer for Windows 11.\n\n" +
                "Features:\n" +
                "• Natural Language Semantic Search powered by local ONNX embeddings\n" +
                "• Blazing-fast keyword filename matching\n" +
                "• IBM Docling layout-aware document indexer\n" +
                "• High-fidelity OneCommander Dark Glassmorphic Miller Columns UI\n\n" +
                "Built with Google DeepMind Antigravity.",
                "About VaultRecon",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information
            );
        }

        [RelayCommand]
        public void ShowSettings()
        {
            string dbStatus = _indexService.IsReady ? "Connected (LanceDB)" : "Disconnected";
            string modelStatus = _embeddingService.IsInitialized ? "Initialized (all-MiniLM-L6-v2)" : "Initializing...";

            System.Windows.MessageBox.Show(
                $"VaultRecon System Diagnostics:\n\n" +
                $"• Vector Database: {dbStatus}\n" +
                $"• Embedding Engine: {modelStatus}\n" +
                $"• Indexed Paths:\n" +
                $"  - Documents\n" +
                $"  - Pictures\n" +
                $"  - Desktop\n" +
                $"  - Downloads\n" +
                $"• Auto-indexing: Enabled (Background FileSystemWatcher)\n" +
                $"• Programmatic Mean Pooling: Enabled\n" +
                $"• Noise Gate Bypass: Enabled",
                "VaultRecon Settings & Status",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information
            );
        }

        [RelayCommand]
        public async Task TriggerDeltaScan()
        {
            if (IsIndexing)
            {
                System.Windows.MessageBox.Show("Background indexing is already in progress.", "Index Status", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "Forcing index delta scan...";
            Log("[SYSTEM] User triggered manual delta scan");
            await _backgroundIndexService.StartAsync();
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
                    ActiveFolderName = Environment.UserName;
                    ActiveFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    ActiveFolderItemCount = "4 items";
                    ActiveFolderTotalSize = "";
                }
                
                UpdateBreadcrumbs();
            }
            catch (Exception ex)
            {
                Log($"[EXPLORER] Error updating folder details: {ex.Message}");
            }
        }

        private async Task LoadInitialColumnsAsync()
        {
            IsSearchOverlayOpen = false;
            try
            {
                Columns.Clear();
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
                if (selectedItem == null)
                {
                    SelectedFileItem = null;
                    SelectedFileContentPreview = string.Empty;
                    return;
                }

                if (selectedItem.IsDirectory)
                {
                    SelectedFileItem = null;
                    SelectedFileContentPreview = string.Empty;
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

        private void InitializeSidebarIcons()
        {
            try
            {
                DesktopIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                DocumentsIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                PicturesIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                VideosIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                MusicIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
                DownloadsIcon = ShellIconHelper.GetIconForPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                ProgramsIcon = ShellIconHelper.GetIconForPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                RecycleBinIcon = ShellIconHelper.GetIconForPath("shell:::{645FF040-5081-101B-9F08-00AA002F954E}");
                Log("[INIT] Dynamic sidebar icons successfully loaded from shell files");
            }
            catch (Exception ex)
            {
                Log($"[INIT] Error loading sidebar icons: {ex.Message}");
            }
        }

        private void InitializeSidebarGroups()
        {
            SidebarGroups.Clear();
            
            var quickAccessGroup = new SidebarGroup { Name = CurrentUserName };
            
            AddDefaultShortcut(quickAccessGroup, Environment.SpecialFolder.Desktop, "Desktop");
            AddDefaultShortcut(quickAccessGroup, Environment.SpecialFolder.MyDocuments, "Documents");
            AddDefaultShortcut(quickAccessGroup, Environment.SpecialFolder.MyPictures, "Pictures");
            AddDefaultShortcut(quickAccessGroup, Environment.SpecialFolder.MyVideos, "Videos");
            AddDefaultShortcut(quickAccessGroup, Environment.SpecialFolder.MyMusic, "Music");
            
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                quickAccessGroup.Items.Add(new FileItem
                {
                    Name = "Downloads",
                    Path = downloadsPath,
                    IsDirectory = true
                });
            }
            
            string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(programsPath))
            {
                quickAccessGroup.Items.Add(new FileItem
                {
                    Name = "Programs",
                    Path = programsPath,
                    IsDirectory = true
                });
            }

            SidebarGroups.Add(quickAccessGroup);
        }

        private void AddDefaultShortcut(SidebarGroup group, Environment.SpecialFolder folder, string name)
        {
            string path = Environment.GetFolderPath(folder);
            if (Directory.Exists(path))
            {
                group.Items.Add(new FileItem
                {
                    Name = name,
                    Path = path,
                    IsDirectory = true
                });
            }
        }

        private void UpdateBreadcrumbs()
        {
            Breadcrumbs.Clear();
            
            Breadcrumbs.Add(new BreadcrumbSegment { Name = "This PC", Path = "QuickAccess" });
            
            if (string.IsNullOrEmpty(ActiveFolderPath) || ActiveFolderPath == "QuickAccess")
            {
                return;
            }
            
            try
            {
                string cleanPath = ActiveFolderPath;
                var parts = cleanPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                
                string currentAccumulatedPath = string.Empty;
                
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (i == 0)
                    {
                        currentAccumulatedPath = part + Path.DirectorySeparatorChar;
                    }
                    else
                    {
                        currentAccumulatedPath = Path.Combine(currentAccumulatedPath, part);
                    }
                    
                    Breadcrumbs.Add(new BreadcrumbSegment
                    {
                        Name = part,
                        Path = currentAccumulatedPath
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"[BREADCRUMB] Error updating breadcrumbs: {ex.Message}");
            }
        }

        private static bool IsSupportedTextFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".txt" or ".md" or ".log" or ".json" or ".xml" or ".cs" or ".py" or ".js" or ".html" or ".css";
        }
    }

    public class BreadcrumbSegment
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public partial class SidebarGroup : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;
        
        public ObservableCollection<FileItem> Items { get; } = new();
    }
}
