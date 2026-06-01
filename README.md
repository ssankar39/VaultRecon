# Vault Recon

An advanced local search engine and multi-column file manager for Windows 11 built with C# and WPF, featuring hybrid semantic vector search, IBM Docling document parsing, and a high-fidelity glassmorphic dark user interface.

---

## Core Features

* **Modern Dark UI**: Beautiful glassmorphic interface featuring a left sidebar with disk capacity indicators, Quick Access shortcuts, navigation history, and unified cool-gray hover visual feedback on all buttons.
* **Miller Columns Explorer**: Browse nested folders side-by-side using horizontal columns. Displays folder size, relative age, and lets you open files natively by double-clicking.
* **Windows 11-Style Context Menu**: Right-click context menu containing standard commands (Open, Administrator run, notepad editing) and a quick action bar (Cut, Copy, Rename, Share, Delete).
* **Smart Hybrid Search**: Combines traditional file name matching with local AI vector search. You can search by meaning and include location terms (e.g., searching *"downloads tax"* specifically queries tax documents in the Downloads directory).
* **Deep Document Ingestion**: Runs local parsing scripts to index the actual text content of PDFs, Word documents, and Excel sheets, making them semantically searchable.
* **Efficient Background Indexing**: Watches for file modifications and indexes your PC in the background while automatically bypassing massive system and developer folders (`node_modules`, `.venv`, etc.) to remain fast and responsive.
* **File Details & Preview Inspector**: Right side panel displaying colored file cards, metadata stats, and a dynamic 100-line live content preview box for source/text files.

---

## Technology Stack

- **Target Framework**: .NET 8.0-windows
- **UI Platform**: WPF (C# / XAML)
- **Vector Database**: LanceDB (Embedded local database)
- **Machine Learning**: Microsoft.ML.OnnxRuntime & `all-MiniLM-L6-v2.onnx` local sentence embeddings
- **MVVM Library**: CommunityToolkit.Mvvm
- **Document Parsing**: IBM Docling + MarkItDown (Python 3 environment execution wrapper)
- **Keyword Search**: PLINQ Parallel Search (highly optimized)

---

## Project Structure

```text
VaultRecon/
├── FS/
│   ├── Models/              # Data structures (FileItem.cs, SearchResult.cs)
│   ├── ViewModels/          # SearchViewModel.cs, DirectoryColumnViewModel.cs
│   ├── Services/            # BackgroundIndexService.cs, EverythingService.cs, EmbeddingService.cs
│   ├── scripts/             # parse_document.py (IBM Docling text parser)
│   ├── App.xaml             # App-wide color resources and base templates
│   ├── MainWindow.xaml      # Core glassmorphic theme and multi-page UI definitions
│   └── BetterFileSys.csproj # .NET 8.0 project file
├── tasks/                   # AI persistence memory and checklist
└── README.md                # Project documentation
```

---

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Windows 10 or Windows 11
- Python 3.10+ (for IBM Docling deep document ingestion)

### Run the Application

1. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

2. **Build the project**:
   ```bash
   dotnet build
   ```

3. **Run the application**:
   ```bash
   dotnet run --project FS/BetterFileSys.csproj
   ```
