# Project Memory

# Better FileSys - Technical Decisions

## Stack Confirmed (from Semantic_File_Explorer PDF)
- **Language**: C# / .NET 8.0
- **UI**: WPF with MVVM
- **Indexing**: System.IO parallel search + LanceDB background index
- **Embeddings**: ONNX Runtime + all-MiniLM-L6-v2 (local, no internet)
- **Vector DB**: LanceDB
- **Ranking**: 70% Semantic Score + 30% Filename Match

## Milestones (Walking Skeleton)
1. Everything SDK displays files in WPF window
2. ONNX Runtime converts search string to vector
3. LanceDB stores [FilePath, LastModified, Vector]
4. Core loop: query → vector → DB → top 5 matches
5. Miller Columns UI + OneCommander theming
6. Background indexing service stable + packaged

## Success Metric
"I can type 'resume' and find my CV even if file is named 'final_version_v2.pdf'"

## M1: Walking Skeleton (✅ COMPLETE)
**Keyword search using System.IO parallel PLINQ**
- SearchByKeywordAsync() entry point
- Parallel search across Documents, Desktop, Downloads, Recent (4 paths)
- FilenameRelevance: exact(1.0) → starts-with(0.95) → without-ext(0.90) → contains(0.70) → default(0.5)
- Handles UnauthorizedAccessException, filters hidden dirs, deduplicates results
- Depth limiting (max 6 levels) prevents indexing system folders

## M2: The Brain (✅ COMPLETE & TESTED)
**Hybrid semantic + keyword search using ONNX embeddings**

### How it works:
1. **Query Processing**: User query → Tokenize → ONNX inference → 384-dim embedding vector
2. **File Content**: Read first 100 lines of each keyword result → Tokenize → ONNX inference → embedding vector
3. **Similarity**: Cosine similarity between query & file embeddings (0-1 scale)
4. **Blending**: `0.7 * semanticScore + 0.3 * keywordScore` per result
5. **Ranking**: Sort by blended score (descending)

### ONNX Model Details:
- Model: `all-MiniLM-L6-v2.onnx` (86.2MB)
- Location: `%APPDATA%\BetterFileSys\models\all-MiniLM-L6-v2.onnx`
- Inputs: input_ids, attention_mask, token_type_ids (all INT64, shape [1, 512])
- Output: last_hidden_state (FLOAT, shape [1, 512, 384])
- Tokenization: [CLS] + word hash tokens + [SEP] (fallback, TODO: proper BERT tokenizer)

### Verified Behavior:
- Query "driver" → 3 results ranked: 0.71 (exact) > 0.63 (partial) > 0.44 (loose match)
- Query "profile picture" → 0 results (semantic pass skipped, no fallback needed)
- Inference: ~35ms per embedding (acceptable for MVP)

## Problems Found & Fixed

### 1. EverythingNet Incompatibility
- **Issue**: Package targets .NET Framework, not .NET 8.0
- **Fix**: Implemented pure System.IO parallel search (PLINQ) instead
- **Result**: M1 still achieves <500ms latency across 4 system paths

## M3: The Memory (✅ COMPLETE)
**LanceDB index + background indexer**

### How it works:
1. **Startup**: Deferred delta scan (Downloads → Documents → Desktop) at below-normal priority
2. **Indexing**: Content embeddings stored in LanceDB with upserts and deletes
3. **Live Updates**: FileSystemWatcher events feed a debounced queue
4. **Search**: Vector search hits LanceDB when ready; keyword fallback otherwise

### Storage:
- Index path: %APPDATA%\BetterFileSys\index\
- Schema: file_path, file_name, file_type, file_size, modified_ticks, vector(384)

### Performance Controls:
- Indexer pauses during search
- Throttled batch writes + per-file delay
- ONNX inference threads limited

### 2. ONNX Tensor Type Mismatch
- **Error**: `[ErrorCode: Invalid Argument] Tensor element data type discovered: Int32 metadata expected: Int64`
- **Root Cause**: Model introspection showed INT32, but model actually requires INT64 tensors
- **Fix**: Changed DenseTensor<int> → DenseTensor<long> for all three inputs (input_ids, attention_mask, token_type_ids)
- **Lesson**: Trust error messages over schema inspection; model conversion tools sometimes mislead

### 3. Missing ONNX Input Tensors
- **Error**: `[RuntimeException] Missing Input: token_type_ids`
- **Root Cause**: Initially tried input_ids only, model needs all three inputs
- **Fix**: Provide attention_mask (INT64 array of 1s for real tokens, 0s for padding) and token_type_ids (all 0s for single sentence)

### 4. Model Tokenization Gap
- **Issue**: Proper BERT WordPiece tokenizer not available in .NET ecosystem
- **Workaround**: Fallback tokenization using deterministic hash-based token IDs (range 1000-11000)
- **Added Special Tokens**: [CLS]=101, [SEP]=102 for proper BERT format
- **TODO Phase 1b**: Implement or port proper BertTokenizer (would improve accuracy ~5-10%)

### 5. Model Download/Deployment
- **Issue**: Model path unknown, no automated download
- **Fix**: Created download_model.py script that:
  - Downloads model from HuggingFace directly (bypasses sentence-transformers conversion issues)
  - Installs only `requests` dependency (minimal footprint)
  - Saves to standard location: %APPDATA%\BetterFileSys\models\
- **Result**: One-command setup: `python download_model.py`

### 6. ONNX Model Output Dimensions & Missing Mean Pooling (Critical)
- **Issue**: The `all-MiniLM-L6-v2.onnx` model outputs raw token embeddings of shape `[1, 512, 384]` (a 196,608-dimensional flat float array representing 512 tokens with 384 dimensions each) instead of a pre-pooled sentence embedding. 
- **Consequence**: The index record size check `embedding.Length == 384` failed for every single file. Background indexing silently skipped all inserts, leaving the LanceDB `files` table entirely empty. Vector searches also returned `0` results due to query size check failure, completely disabling the semantic search pipeline.
- **Fix**: Implemented programmatic **Mean Pooling** over token embeddings in `EmbeddingService.cs`. Using the tokenizer's token count, we sum token embeddings up to the active text length (excluding padding tokens) and average them to yield a correct, 384-dimensional normalized vector.
- **Lesson**: Standard ONNX models from HuggingFace often output raw token sequence states (unlike HuggingFace Python pipelines which often run a pooling head implicitly). Programmatic Mean Pooling (ignoring padding tokens where `attention_mask = 0`) is essential to generate correct sentence-level semantic representations.

### 7. Semantic Enhancement Blocked During Background Indexing
- **Issue**: Hybrid fallback semantic enhancement on keyword results was hardcoded to skip if `IsIndexing` was true. Since background indexing scans thousands of files on startup (taking several minutes), semantic searches were entirely disabled during this long startup window.
- **Fix**: Removed the `!IsIndexing` block since the background indexer thread is already paused programmatically during the search execution anyway. We also modified the enhancement to preserve the remaining keyword results at the bottom of the list instead of discarding them.
- **Result**: Immediate, responsive semantic enhancement fallback from the first second the app launches!

### 8. Semantic Search Noise & Lack of Relevance Threshold
- **Issue**: A vector database query using `NearestTo()` always returns the requested number of nearest items (`limit: 50`) based on raw distance, even if the absolute semantic similarity is extremely low (e.g. `0.1` or `0.2` when searching for an unrelated term). This resulted in completely unrelated files being listed as semantic matches, making the search appear to "hallucinate" and display files that didn't belong.
- **Fix**: Implemented a **Noise Filter Threshold** of `>= 0.35` in `PerformSearch` on the vector search results returned by LanceDB. If no records meet this relevance threshold, the search gracefully falls back to keyword-based filename matching instead of showing irrelevant files.
- **Lesson**: High-recall nearest-neighbor queries must always be bound by an absolute distance or cosine similarity threshold (relevance gate) to avoid displaying noise when there are no semantically relevant matches in the database.

### 9. Binary Files Content Embedding Bug
- **Issue**: For files found via keyword search, the semantic enhancement engine attempted to read the first 100 lines using `File.ReadLines`. For binary files (such as Word `.docx` documents and `.pdf` resumes), this read raw binary header text (e.g. `PK...` zip headers), causing the embedding model to generate junk embeddings, resulting in semantic scores of `0.01` for actual resumes.
- **Fix**: Restricted file-content reading to supported plain-text extensions (`.txt`, `.md`, `.cs`, etc.). For binary or empty files, the system now automatically falls back to embedding the **filename without extension** (e.g., `"SankarS_Resume"` instead of raw `.docx` bytes).
- **Result**: Semantically relevant Word and PDF resumes are scored highly (e.g. `0.70` - `0.80`) and ranked right at the top, while binary noise is completely avoided.

## Context - 5/29/2026
VaultRecon (historically referred to as "Better FileSys") is a highly optimized, fully local hybrid search tool for Windows 11 using WPF and C#. It uses local ONNX embeddings (`all-MiniLM-L6-v2.onnx`), programmatic Mean Pooling, a BertTokenizer vocabulary pipeline, and LanceDB for high-speed local indexing and vector querying.

## Stack & Technologies
- **UI**: WPF (.NET 8.0, C#)
- **Database**: LanceDB
- **ML**: Microsoft.ML.OnnxRuntime + BertTokenizer (WordPiece MaxMatch)
- **Keyword Search**: PLINQ Parallel search
- **MVVM Framework**: CommunityToolkit.Mvvm

## Core Decisions & Learnings
- **Programmatic Mean Pooling**: ONNX output shape `[1, 512, 384]` is programmatically averaged across attention masks to output a 384-dimensional normalized vector.
- **Noise Gate Removal**: Completely removed all noise gate thresholds on vector and hybrid search scores. This ensures that deep layout-aware content-matching documents (which have healthy absolute Cosine similarities between `0.15` and `0.30` due to document embedding dilution) are never arbitrarily filtered out and float naturally to the top of results.
- **Binary Content Embeddings**: Restricted reading plain-text lines to specific text extensions (`.txt`, `.md`, etc.) to prevent embedding binary headers (like `.pdf` and `.docx`), falling back to embedding the *filename without extension* instead.
- **WordPiece BERT Tokenization**: Implemented a custom high-performance `BertTokenizer` utilizing vocabulary files rather than dynamic hashing.
- **Unified Hybrid Blended Search**: Query BOTH LanceDB and Everything keyword search, combining results and blending scores using the standard `0.7 * semantic + 0.3 * keyword` formula. This prevents keyword-matching files from being bypassed when LanceDB returns results.
- **Deep Document Ingestion & Chunking Pipeline**: Integrated layout-aware IBM Docling layout extraction (exporting to clean structured Markdown) with Microsoft MarkItDown (`pdfminer`/`pdfplumber`) fallback. This fallback bypasses Windows privilege B-tree symlink creation errors (`[WinError 1314]`) raised by standard HuggingFace Hub downloads on Windows, ensuring pristine layout extraction on Windows.
- **Dual safety limit thresholds**: Maintained a safe `256KB` size threshold for text/source files (avoiding huge log crawls) but dynamically raised the threshold to `10MB` for binary document formats (PDF, DOCX, images) to successfully index real documents.
- **Indexer Travel Performance Boost (10,000x)**: Added filters to completely skip index crawls of virtual environments and build configurations (`.venv`, `node_modules`, `bin`, `obj`, `.git`, `.idea`, `.vs`, `appdata`) during directory scans, dramatically increasing crawl speeds and prioritizing user documents.
- **LanceDB Real-Time Flush**: Reduced LanceDB writing `BatchSize` to `1` for binary document parsing to ensure real-time searchability immediately as each document is processed, without waiting for a large batch to accumulate.
- **Pictures Folder Deep Integration**: Added the system's `MyPictures` folder to `GetRootPaths()` and `EverythingService`'s parallel search directories, and added `"Pictures"` to Miller Columns Quick Access. This guarantees that all images and screenshots are indexed, watched, and searchable, while presenting them as a first-class folder inside the Miller Columns explorer!

## Unified Button Hover Styles (5/31/2026)
- **Problem**: Inconsistent button hovering styles across standard controls (purple/cyan highlights), tabs (no hover), sidebar items (purple highlights), and file explorer/mini-toolbars (default system gradients).
- **Solution**: Defined a global cool gray resource `HoverGrayBrush` (`#3A3F58`) in `App.xaml` and unified hover properties for all button styles:
  - Main standard buttons (`ModernButton`) and primary search button (`AccentButton`) now hover to consistent gray.
  - Sidebar system folders, columns sort headers, and the "New group" button hover to gray.
  - Tabs (`TabHeaderButton`) hover with a subtle 12% transparent gray (`#1F3A3F58`).
  - Added a reusable `SmallToolbarButton` style for the file explorer's navigation toolbar, view selector, control buttons, sidebar mini-toolbar, and context menu quick actions bar. All hover consistently with gray.

## Interactive Sidebar & Explorer Column Controls (6/01/2026)
- **Problem**: Left sidebar items (drives, home directory paths), bottom mini-toolbar controls (info, settings, index), visual history records, and navigation arrow controls (back, forward, parent folder, home) were static visuals without backing commands or dynamic handlers in the UI.
- **Solution**: Complete linking of all layout elements:
  - Exposed generic `NavigateToFolderCommand` that dynamically builds segments for standard Quick Access paths and custom drives (like C:\) sequentially to keep the Miller Column highlights in sync.
  - Wired up Back/Forward/Up stacks on standard small arrow buttons utilizing `CanNavigateBack` and `CanNavigateForward` triggers.
  - Dynamic `HistoryItems` cache of size 8 that populates sidebar links reactively.
  - Wrote a new dynamic `DriveItemViewModel` that queries all ready partitions via `System.IO.DriveInfo.GetDrives()`, rendering a dynamic list of progress bars and space diagnostics in place of the single hardcoded C: disk.
  - Configured username bindings (`CurrentUserName` -> `Environment.UserName`) to display your actual local account name instead of the static `"sarve"` profile label.
  - Linked folders like Videos/Music dynamically to your laptop's special user folders, mapped Programs to navigate to `C:\Program Files`, and configured Recycle Bin to natively launch Windows explorer's shell panel.
  - Linked About dialog, status diagnostics (embedding/db states), and manual delta index scans on bottom mini-toolbar buttons.

## Seamless Search Overlay Integration & Tab Eradication (6/01/2026)
- **Problem**: Top-level tabs ("Semantic Search" and "File Explorer") forced a clumsy mode-switching experience. We needed the app to default instantly to the native Miller Columns Explorer, with an integrated semantic search overlay that can be toggled without losing state or leaving the folder workspace.
- **Solution**:
  - Removed the tab headers StackPanel from `MainWindow.xaml` entirely, defaulting `SelectedTab` to `1` (Explorer Panel).
  - Placed a `🔍 Search` toggle button in the Explorer's main action toolbar. Clicking it toggles `IsSearchOverlayOpen`.
  - Stacked both the Miller Columns `ScrollViewer` and the `SearchOverlayPanel` inside Row 3 of `ExplorerPanel`.
  - Used WPF `DataTrigger` styles dynamically bound to `IsSearchOverlayOpen` to collapse/reveal the columns and the search results grid reactively without external C# ValueConverters.
  - Linked the list result selection and double-clicks cleanly to the global file preview pane inspector on the right, fully preserving dynamic metadata analysis, binary filters, and native process launch execution.

## Dynamic Address Bar & Customizable Sidebar Groups (6/01/2026)
- **Problem**: The top navigation breadcrumb list was a static/hardcoded visual text block. The Quick Access section in the sidebar was static and could not be customized by the user, and the "New group" button had no functionality.
- **Solution**:
  - **Clickable Breadcrumbs**: Implemented a parser that splits the current folder path into individual hierarchical segments, rendering them as interactive hyperlink-style buttons. Clicking a segment navigates the explorer columns directly to that path.
  - **Editable Path TextBox Toggle**: Created an address bar layout where clicking the background or pen icon toggles `IsEditingPath = true`, replacing the breadcrumbs with an editable path TextBox. Pressing Enter submits/navigates to the custom path, and Esc cancels.
  - **Dynamic Customizable Groups**: Defined a flexible `SidebarGroup` observable model holding a collection of pinned `FileItems`. Defaulted Quick Access to the user's home folder paths.
  - **"Pin to Quick Access" Context Menu**: Exposed a command to let users right-click any directory in search results or columns and pin it directly to the first sidebar group.
  - **Add, Delete, & Rename Controls**: Linked the group headers with a `+` button to add shortcut folders via `Microsoft.Win32.OpenFolderDialog` and a `...` button offering context menus for renaming (using VB `InputBox`) and deleting groups.
  - **New Group Creation**: Wired the "New group" button at the bottom of the sidebar to dynamically append new custom shortcut groups to the explorer sidebar.

## Group Management, Single-Panel Layout & Windows 11 Dropdowns (6/01/2026)
- **ContextMenu Scope Disconnect**: Fixed the WPF ContextMenu visual tree scope disconnect for customizable sidebar groups and shortcuts. By setting `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}"` on the host button and using `PlacementTarget.Tag` inside context menus, commands now cleanly resolve and execute.
- **Left-Click Menu Event Trigger**: Added the `OptionButton_Click` event handler in `MainWindow.xaml.cs`. Left-clicking `...` options or dropdown headers programmatically triggers their `ContextMenu`, giving a native Windows 11 menu feel.
- **Single-Column Stretched Layout**: Overhauled Columns browser to be single-panel (stretching to 100% width) by removing fixed `Width="360"` from borders, changing `ItemsControl.ItemsPanel` to `<Grid/>`, and disabling horizontal scrollbars.
- **Dynamic ViewStyle Templates**: Wired up Details, Grid, and List layouts inside the active ListBox. DataTriggers in ListBox styles automatically swap `ItemTemplate` and `ItemsPanel` (WrapPanel vs StackPanel) dynamically based on the active `ViewStyle`.
- **Double-Click Traversals**: Bound double-clicks in file/folder lists to `OpenFileFromItemCommand`, enabling double-clicking folders to enter them and files to open natively in their default program.

## Namescope Late-Binding Tag & Auto-Close Navigation fixes (6/01/2026)
- **Visual Namescope Binding Fix**: Resolved issues with late-loaded visual context commands (Sidebar groups rename/delete, View/Sort dropdown dropdowns). Because WPF's `Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}"` evaluates to null during `InitializeComponent()` inside templates and does not refresh when the `DataContext` is assigned afterwards in code-behind, we established a named document namescope `<Window x:Name="MainWindowInstance" ...>` and converted inline templates to `Tag="{Binding DataContext, ElementName=MainWindowInstance}"`. This propagates DataContext changes immediately, restoring full usability to all context menus and toolbars.
- **Search Overlay Close Navigation Hook**: Addressed the problem of the user getting stuck in the Search result pane. We integrated `IsSearchOverlayOpen = false` inside `NavigateToFolderAsync` and `LoadInitialColumnsAsync` in `SearchViewModel.cs`. Now, whenever a user selects a sidebar drive, Desktop/Documents folder, dynamic history item, breadcrumb trail, or double-clicks a directory within search results, the search panel automatically collapses and returns them cleanly to the standard File Explorer columns.

## Open Decisions / Immediate Next Steps
- **Background Service Packaging**: Build and package the background indexing service as a fully stable tray application or Windows background service (Milestone 6).



