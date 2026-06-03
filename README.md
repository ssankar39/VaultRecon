# Vault Recon

An advanced local search engine and multi-column file manager for Windows 11 built with C# and WPF, featuring hybrid semantic vector search, IBM Docling document parsing, and a high-fidelity glassmorphic dark user interface.

---

## Core Features

* **Modern Dark UI**: Beautiful glassmorphic interface with unified cool-gray hover feedback, custom Windows 11-style dropdowns/context menus, and Segoe MDL2 icon assets.
* **Integrated Search Overlay**: Toggleable semantic search overlay that lets you query the system without losing your place in the directory browser, automatically returning to the file explorer when you navigate.
* **Stretched Active Column Explorer**: Stretched folder listing representing the active column with support for dynamic View Styles (Details, Grid, List) that swap layout templates at runtime.
* **Dynamic Address Bar**: Interactive breadcrumb segments representing parent folders that toggle to a fully editable path TextBox upon click or pencil-icon focus.
* **Customizable Left Sidebar**: User-manageable shortcut groups enabling pinning folders via context menus, adding/renaming/deleting groups, and displaying dynamic space capacity diagnostics for all active system partitions.
* **Smart Hybrid Search**: Combines traditional PLINQ filename matching (30% weight) with local AI vector search (70% weight) to retrieve files by meaning, including directory context (e.g., searching *"downloads tax"* matches tax files in Downloads).
* **Deep Document Ingestion**: Runs background Python scripts via IBM Docling and MarkItDown to extract layout-aware markdown content from PDFs, Word documents, and Excel sheets, making them semantically searchable.
* **Background Indexing Service**: Scans and indexes directories in the background, listening to real-time changes using FileSystemWatcher, while ignoring heavy developer and environment folders (`node_modules`, `.venv`, `bin`, `obj`, etc.).
* **File Details & Preview Inspector**: Right-hand inspection panel displaying dynamic file details, metadata statistics, and a live 100-line content preview box for text and source files (automatically hidden for binary assets).

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
│   ├── Models/              # FileItem.cs, SearchResult.cs, FileIndexRecord.cs, DriveItemViewModel.cs
│   ├── ViewModels/          # SearchViewModel.cs, DirectoryColumnViewModel.cs, MainWindowViewModel.cs
│   ├── Services/            # BackgroundIndexService.cs, EverythingService.cs, EmbeddingService.cs, LanceDbIndexService.cs, FileSystemService.cs, BertTokenizer.cs, ShellIconHelper.cs
│   ├── scripts/             # parse_document.py (IBM Docling parser), convert_icon.py, sign_release.ps1
│   ├── App.xaml             # App-wide color resources and base templates
│   ├── MainWindow.xaml      # Core glassmorphic theme and layout definitions
│   └── VaultRecon.csproj    # .NET 8.0 project file
├── tasks/                   # AI persistence memory and todo checklists
└── README.md                # Project documentation
```

---

## Getting Started

### Prerequisites
- **.NET 8.0 SDK**
- **Windows 10 or Windows 11**
- **Python 3.10+** (required for IBM Docling deep document ingestion)

### 1. Python Parser Setup (Optional but recommended)
Install the required packages for document extraction:
```bash
pip install docling markitdown
```

### 2. Local AI Model Bootstrapping
You do not need to download the model manually. At startup, Vault Recon automatically downloads and caches the required sentence embedding model (`all-MiniLM-L6-v2.onnx`, ~90MB) and vocabulary file (`vocab.txt`) from HuggingFace to your local directory:
`%APPDATA%\VaultRecon\models\`

### 3. Run the Application

From the root of the project directory:

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project FS/VaultRecon.csproj
```

---

## Packaging & Distribution

VaultRecon comes with an automated distribution pipeline to package the application as a self-contained, code-signed executable and standard installer:

### 1. Build Self-Contained Release Payload
Compile the WPF application along with all native runtime DLLs (for LanceDB and ONNX) and the Python document-parser scripts into a standalone output directory:
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```
The compiled executable `VaultRecon.exe` will be located in `FS/bin/Release/net8.0-windows/win-x64/publish/`.

### 2. Sign the Executable (Authenticode)
Establish local binary trust and verify signature integrity by signing the executable using the built-in signing tool:
```bash
powershell -ExecutionPolicy Bypass -File FS/scripts/sign_release.ps1
```
This native PowerShell script checks for a local code-signing certificate, registers it in your current user's Trusted Root store if missing, and signs the compiled `VaultRecon.exe` using SHA-256.

### 3. Generate Inno Setup Installer
To create the final redistributable setup wizard (`VaultReconSetup.exe`):
1. Install **Inno Setup**.
2. Run the command-line compiler:
   ```bash
   ISCC FS/installer.iss
   ```
3. The setup package will be built under the `installer_output/` folder. The installer is configured to run at `lowest` privileges (installing to the user's Local AppData folder) to guarantee that standard users do not require Administrator UAC prompts to install the application.

