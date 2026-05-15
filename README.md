# Better FileSys

A modern Windows 11 file system management desktop application built with C# and WPF.

## Overview

Better FileSys is an advanced file manager for Windows 11 designed to enhance the native file system experience with powerful features for efficient file management, organization, and navigation.

## Features

- **Intuitive Interface**: Clean, modern WPF-based user interface
- **Fast Navigation**: Quick access to common folders and drives
- **File Operations**: Bulk delete, create, move, and copy operations
- **Search & Filter**: Advanced search capabilities with filters
- **Drag & Drop**: Intuitive drag-and-drop file operations
- **Recent Files**: Quick access to recently used files
- **Metadata View**: Display and edit file properties
- **Multi-tab Support**: Manage multiple folders simultaneously

## Technology Stack

- **Framework**: .NET 6.0+
- **UI**: WPF (Windows Presentation Foundation)
- **Language**: C# 10+
- **Dependencies**:
  - CommunityToolkit.Mvvm - MVVM implementation
  - System.IO.Abstractions - File system abstraction
  - Microsoft.Xaml.Behaviors.Wpf - XAML behaviors

## Project Structure

```
BetterFileSys/
├── Models/              # Data models (FileItem, etc.)
├── ViewModels/          # MVVM ViewModels
├── Views/               # XAML Windows and UserControls
├── Services/            # Business logic and file operations
├── App.xaml             # Application root
├── MainWindow.xaml      # Main window UI
└── BetterFileSys.csproj # Project file
```

## Getting Started

### Prerequisites

- .NET 6.0 SDK or later
- Windows 10 or Windows 11
- Visual Studio 2022 or Visual Studio Code

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/username/better-filesys.git
   cd "Better FileSys"
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

4. Run the application:
   ```bash
   dotnet run
   ```

## Development

### Building

```bash
dotnet build
```

### Running in Debug Mode

```bash
dotnet run
```

### Publishing

Create a standalone executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Architecture

The application follows the **MVVM (Model-View-ViewModel)** pattern:

- **Models**: Core data structures representing files and folders
- **ViewModels**: Logic for handling UI interactions and file operations
- **Views**: XAML UI definitions
- **Services**: Business logic for file system operations

## Future Enhancements

- [ ] Cloud storage integration (OneDrive, Google Drive)
- [ ] File synchronization
- [ ] Advanced preview capabilities
- [ ] Batch renaming tools
- [ ] File compression/extraction
- [ ] Theme customization
- [ ] Multi-language support

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see LICENSE file for details.

## Contact

For questions or suggestions, please open an issue on GitHub.

---

**Version**: 1.0.0  
**Last Updated**: May 14, 2026
