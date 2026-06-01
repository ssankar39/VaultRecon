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
- [ ] Define the consistent gray hover color resource `HoverGrayBrush` in `App.xaml`
- [ ] Refactor `ModernButton` style in `App.xaml` to hover with the consistent gray background and border
- [ ] Refactor `AccentButton` style in `App.xaml` to hover with the consistent gray background
- [ ] Refactor `SidebarButtonStyle` in `MainWindow.xaml` to hover with the consistent gray background
- [ ] Refactor `SortHeaderButtonStyle` in `MainWindow.xaml` to hover with the consistent gray background
- [ ] Refactor `TabHeaderButton` in `MainWindow.xaml` to hover with a subtle consistent gray background
- [ ] Create a reusable `SmallToolbarButton` style in `MainWindow.xaml` with consistent gray hover
- [ ] Apply `SmallToolbarButton` style to all small buttons in `MainWindow.xaml`:
    - Navigation toolbar buttons (←, →, ↑, ⌂)
    - View style toolbar buttons (≡ List, ⊞ Grid, 📊 Columns)
    - Sorting & Refresh buttons (⇅ Sort, 🔄 Refresh)
    - Sidebar mini-toolbar buttons (ℹ️, ⚙️, 🔍, 🔄)
    - Context Menu action bar buttons (Cut, Copy, Rename, Share, Delete)
- [ ] Apply style/hovering to the "New group" sidebar button
- [ ] Build and launch the app, manually verifying all button hover interactions
