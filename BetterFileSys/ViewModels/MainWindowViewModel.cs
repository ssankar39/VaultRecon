using BetterFileSys.Models;
using BetterFileSys.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace BetterFileSys.ViewModels
{
    /// <summary>
    /// ViewModel for the main application window
    /// </summary>
    public class MainWindowViewModel
    {
        private readonly FileSystemService _fileSystemService;
        private string _currentPath = "";

        public ObservableCollection<FileItem> FileItems { get; } = new();

        public MainWindowViewModel()
        {
            _fileSystemService = new FileSystemService();
            _currentPath = Path.GetPathRoot(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))) ?? "C:\\";
        }

        public async Task LoadDirectoryAsync(string path)
        {
            _currentPath = path;
            var items = await _fileSystemService.GetDirectoryContentsAsync(path);

            FileItems.Clear();
            foreach (var item in items)
            {
                FileItems.Add(item);
            }
        }

        public async Task RefreshAsync()
        {
            await LoadDirectoryAsync(_currentPath);
        }

        public string CurrentPath => _currentPath;
    }
}
