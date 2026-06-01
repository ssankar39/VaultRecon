# Todo List: Milestone 5 Implementation (Miller Columns & OneCommander Theme)

- [x] Design and implement the OneCommander-inspired Dark Glassmorphism Theme in `App.xaml` & `MainWindow.xaml`
    - [x] Create dark color resources, gradients, scrollbar styles, and button control templates in `App.xaml`
    - [x] Apply rounded corners, subtle shadows, and cyber-violet/cyan highlights across the application
- [x] Define the data structures for Miller Columns navigation
    - [x] Create `DirectoryColumnViewModel` class to track columns, directories, list contents, and selections
    - [x] Integrate Miller Columns properties and selected item state in `SearchViewModel.cs`
- [x] Implement the Miller Columns interactive browser in `MainWindow.xaml`
    - [x] Add a horizontal scrollable view containing multiple vertical Lists representing columns
    - [x] Implement path traversal logic (clicking folder triggers loading next column, clears deep columns)
- [x] Build the File Preview Pane
    - [x] Create details card panel on the right side of the main workspace
    - [x] Load and display file metadata (path, size, modified, type)
    - [x] Implement plain-text preview logic (reading and displaying first 100 lines for text files)
    - [x] Add "Open File", "Open Folder", and "Copy Path" action buttons to the preview panel
- [x] Refactor navigation and search layout
    - [x] Add a top-level visual tab selector (Search Results view vs. File Explorer view)
    - [x] Style the Semantic Search result items with modern cards and color-coded match badge highlights
- [x] Perform manual verification of the UI and verify overall functionality
- [x] Make search button style static blue (`#0078D4`) instead of gradients in `App.xaml`
- [x] Implement toggleable sort filter headers bar for Name, Date modified, Type, and Size ascending/descending
- [x] Verify sorting behaves perfectly with toggles in both ascending and descending modes
- [x] Prevent directory selections from displaying/loading the file preview/metadata menu panel
- [x] Create a custom modern ContextMenu styled exactly like Windows 11 context menu (Cut, Copy, Rename, Share, Delete action bar, standard icons, Dark Mode layout)
- [x] Configure ContextMenu bindings (`OpenFile`, `RunAsAdmin`, `OpenFolder`, `CopyPath`, `ShowProperties`, `EditInNotepad`, `DeleteFile` commands) on both Search results and Explorer columns
- [x] Add double-click input mouse gesture double-click bindings (`OpenFileCommand`) inside ItemTemplates to launch files natively
- [x] Remove placeholder content preview TextBox entirely for binary files (hiding text preview titles, dividers, and boxes dynamically when selecting non-text binary file types like PDFs, ZIPs, or shortcut links)

# Todo List: Semantic Search Bug Fix
- [x] Update `BackgroundIndexService.cs` to index `.pdf`, `.docx`, `.doc`, `.xls`, `.xlsx`, `.ppt`, `.pptx`, `.png`, `.jpg`, `.jpeg` files by their filename without extension.
- [x] Make `CalculateFilenameRelevance` public in `EverythingService.cs`.
- [x] Refactor `PerformSearch` in `SearchViewModel.cs` to execute both LanceDB and Everything keyword searches, merging and blending results using a hybrid formula.
- [x] Clear LanceDB index and verify delta scan indexes documents correctly.
- [x] Verify semantic queries like "resume", "education", "experience" retrieve relevant non-text document files successfully.

# Todo List: Local Document Processing with Docling & MarkItDown
- [x] Create `FS/scripts/parse_document.py` supporting layout-aware Markdown extraction via IBM Docling and MarkItDown fallback.
- [x] Add `ParseBinaryDocumentAsync` C# process execution wrapper in `BackgroundIndexService.cs` with robust script path resolution.
- [x] Implement dynamic safety limits (`MaxBinaryFileSizeBytes = 10MB` vs `MaxFileSizeBytes = 256KB` for text) to allow indexing larger binary files.
- [x] Update background indexing logic to use `ParseBinaryDocumentAsync` for non-text formats and index document contents semantically, truncating text to 2000 characters.
- [x] Clear LanceDB index and verify document content is indexed and semantically searchable.

# Todo List: File Path Context & OneCommander Layout Sync
- [x] Incorporate the full file path and file name into the embedding payload text in `BackgroundIndexService.cs` (`BuildRecordAsync`).
- [x] Incorporate the full file path and file name into the embedding payload text in `SearchViewModel.cs` (`EnhanceWithSemanticScoresAsync`).
- [x] Clear LanceDB index at `%APPDATA%\BetterFileSys\index\`.
- [x] Rebuild and run the application to perform a fresh delta scan indexing both document content and file paths.
- [x] Implement a high-fidelity Left Sidebar panel in `MainWindow.xaml` featuring Drives, Network, profile navigation buttons, History, and a utility mini-toolbar.
- [x] Implement parent-folder Navigation commands in `SearchViewModel.cs` allowing users to click Sidebar directories (Documents, Pictures, Desktop, Downloads) and see them open instantly.
- [x] Render grid details inside Miller Columns folders (showing file/folder name, custom tags `[DIR]`, exact file sizes, formatted modified dates, and colored Relative Age pills matching OneCommander's hours/days/months/years logic).
- [x] Design breadcrumbs (`ActiveFolderPath`) and active folder details header (`ActiveFolderName`, `ActiveFolderItemCount`, `ActiveFolderTotalSize`) dynamically computed on directory selection.
- [x] Redesign global Preview Pane Inspector with high-fidelity colored icon cards and modern properties lists.

---

## Review & Verification

### 1. File Path Inclusion in Semantic Vectors
We prepended the `File Path: ...` and `File Name: ...` context to the content block prior to vector embedding in both `BackgroundIndexService.cs` and `SearchViewModel.cs`.
- **Cosmetic Benefit**: Location terms are now fully searchable. Users can type `"downloads invoice"`, `"documents tax"`, or `"pictures sunset"` and retrieve the correct files immediately even if they do not know where they are.
- **Precision**: Differentiates duplicate filenames across directories semantically by their folder context.

### 2. High-Fidelity OneCommander Theme & Explorer Columns
The entire GUI layout has been transformed to match the OneCommander screenshot:
- **Left Sidebar**: Renders visual system shortcuts, disk progress, user home paths, history, and bottom settings.
- **Miller Columns**: Upgraded from simple lists to dynamic grid details showing tags, sizes, and relative age pills (yellow for hours, green for days, teal for months, grey for years).
- **Explorer Header**: Renders crisp breadcrumbs paths, active directory name headers, total item counts, and size aggregations.
- **Action Toolbar**: Interactive toolbar showing back/forward/up arrows and list style preferences.
- **Preview Inspector**: Renders glassmorphic colored cards with descriptive details list keys.

# Todo List: Consistent Button Hover Style
- [x] Define the consistent gray hover color resource `HoverGrayBrush` in `App.xaml`
- [x] Refactor `ModernButton` style in `App.xaml` to hover with the consistent gray background and border
- [x] Refactor `AccentButton` style in `App.xaml` to hover with the consistent gray background
- [x] Refactor `SidebarButtonStyle` in `MainWindow.xaml` to hover with the consistent gray background
- [x] Refactor `SortHeaderButtonStyle` in `MainWindow.xaml` to hover with the consistent gray background
- [x] Refactor `TabHeaderButton` in `MainWindow.xaml` to hover with a subtle consistent gray background
- [x] Create a reusable `SmallToolbarButton` style in `MainWindow.xaml` with consistent gray hover
- [x] Apply `SmallToolbarButton` style to all small buttons in `MainWindow.xaml`:
    - [x] Navigation toolbar buttons (←, →, ↑, ⌂)
    - [x] View style toolbar buttons (≡ List, ⊞ Grid, 📊 Columns)
    - [x] Sorting & Refresh buttons (⇅ Sort, 🔄 Refresh)
    - [x] Sidebar mini-toolbar buttons (ℹ️, ⚙️, 🔍, 🔄)
    - [x] Context Menu action bar buttons (Cut, Copy, Rename, Share, Delete)
- [x] Apply style/hovering to the "New group" sidebar button
- [x] Build and launch the app, manually verifying all button hover interactions

# Todo List: Interactive Sidebar & Explorer Column Controls
- [x] Implement C: Drive Disk Capacity Diagnostics (`UpdateDriveSpaceDetails`) in `SearchViewModel.cs`
- [x] Create generalised `NavigateToFolderCommand` supporting sequential path hierarchy column building
- [x] Implement Navigation Back/Forward stacks and `CanNavigateBack`/`CanNavigateForward` triggers
- [x] Implement dynamic navigation history collection (`HistoryItems`) on folder selection changes
- [x] Add about dialog, settings diagnostics, and delta scan commands to VM
- [x] Bind Main Disk button to NavigateToFolder with C:\ and dynamic space parameters in XAML
- [x] Bind Navigation Arrows (←, →, ↑, ⌂) to their respective commands and enablement states in XAML
- [x] Bind Dynamic History list to Sidebar ItemsControl in XAML
- [x] Bind bottom Sidebar mini-toolbar buttons to their respective commands in XAML
- [x] Perform manual verification of the entire interactive explorer pipeline

# Todo List: Dynamic Shell Icons & Emoji Removal
- [x] Add `LargeIcon` property to `FileItem.cs`
- [x] Add `LargeIcon` property to `SearchResult.cs`
- [x] Add `Icon` property to `DirectoryColumnViewModel.cs`
- [x] Implement `InitializeSidebarIcons` and register it in `SearchViewModel.cs` constructor
- [x] Update `MainWindow.xaml` to bind Drives, Sidebar directories, and History to their dynamic system icons
- [x] Update `MainWindow.xaml` search results card to display native system icons
- [x] Update `MainWindow.xaml` Explorer column headers and items to display dynamic icons
- [x] Update `MainWindow.xaml` Preview pane (Null state & Active preview card) with Segoe MDL2 icon and LargeIcon dynamic image
- [x] Refactor ContextMenu and bottom mini-toolbar in `MainWindow.xaml` to use Segoe MDL2 Assets instead of emojis
- [x] Build and verify compilation and visual fidelity

# Todo List: Explorer Search Overlay & Tab Removal
- [x] Add `isSearchOverlayOpen` property, `ToggleSearchOverlayCommand`, and `CloseSearchOverlayCommand` to `SearchViewModel.cs`
- [x] Remove navigation tabs StackPanel from `MainWindow.xaml` header
- [x] Add `🔍 Search` button in `MainWindow.xaml` File Explorer toolbar
- [x] Integrate Search Overlay Grid containing the search bar and cards into Row 3 of Explorer panel in `MainWindow.xaml`
- [x] Bind Miller Columns ScrollViewer and Search Overlay Grid visibility states to `IsSearchOverlayOpen`
- [x] Build and verify compilation and visual layout

# Todo List: Dynamic Breadcrumbs Address Bar & Customizable Sidebar (Milestone 5+)
- [x] Implement clickable breadcrumbs address bar matching Windows Explorer layout
- [x] Implement editable TextBox path toggle on click/focus with Return/Esc bindings
- [x] Implement customizable sidebar shortcut groups to replace static quick access
- [x] Implement "Pin to Quick Access" context menu option on folder items to bookmark them
- [x] Implement dynamic "New group" sidebar button
- [x] Implement header controls (+ and ...) to add shortcuts and manage sidebar groups
- [x] Remove write assignments to read-only FileItem.Icon property
- [x] Rebuild and verify compilation success with 0 errors

# Todo List: Group Management, Single-Panel Navigation & Windows-Style Sort/View Toolbars
- [x] Add `OptionButton_Click` event handler in `MainWindow.xaml.cs` to trigger ContextMenus on left-click
- [x] Fix WPF ContextMenu scope disconnect for Sidebar groups ("Rename Group", "Delete Group") using `PlacementTarget.Tag` and `PlacementTarget.DataContext`
- [x] Fix WPF ContextMenu scope disconnect for group shortcuts ("Remove from Sidebar")
- [x] Remove `Width="360"` from Column Border and set Column list ItemsControl panel to `<Grid/>` for single-panel stretching
- [x] Disable outer ScrollViewer horizontal scrollbar to match single-panel Windows Explorer mode
- [x] Update ListBox double-click mouse binding to trigger `OpenFileFromItemCommand` with `CommandParameter="{Binding}"` for folder entering and native file launch
- [x] Replace static `⇅ Sort` button with clickable Windows 11-style sorting dropdown ContextMenu
- [x] Replace static layout buttons with clickable `View` dropdown ContextMenu supporting details, grid, and list views
- [x] Add `ExplorerDetailsTemplate`, `ExplorerGridTemplate`, and `ExplorerListTemplate` in `Window.Resources`
- [x] Wire up dynamic template and layout switching inside Column ListBox based on `ViewStyle` using DataTriggers
- [x] Build and verify successful compilation and flawless execution
# Todo List: Group Management, Active Directory Pane & Toolbar Fixes (Milestone 5+ / User Feedback Round 2)
- [x] Add `x:Name="MainWindowInstance"` to the `<Window>` in `MainWindow.xaml` to establish a stable named namescope.
- [x] Replace `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}"` with `Tag="{Binding DataContext, ElementName=MainWindowInstance}"` on options button (line 565), shortcut button (line 598), View button (line 839), and Sort button (line 932) to resolve initial null evaluation.
- [x] Verify that right-click context menu bindings in `Windows11Menu` and sidebar groups resolve and execute perfectly.
- [x] Verify that sorting and view switching dropdown menus (Details, Grid, List) now function perfectly when left-clicked and selected.
- [x] Verify that sidebar groups renaming via input popups and shortcut removal function perfectly.
- [x] Ensure single-column stretched active folder layout matches the behavior of Windows Explorer.
- [x] Auto-close search overlay (`IsSearchOverlayOpen = false`) upon any navigation command in `SearchViewModel.cs` (inside `NavigateToFolderAsync` and `LoadInitialColumnsAsync`), so the user returns to the File Explorer when selecting sidebar drives, folders, history, breadcrumbs, parent folders, or double-clicking search results.

