using BetterFileSys.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BetterFileSys.ViewModels
{
    /// <summary>
    /// ViewModel representing a single column in the Miller Columns file explorer
    /// </summary>
    public partial class DirectoryColumnViewModel : ObservableObject
    {
        [ObservableProperty]
        private string path = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private ObservableCollection<FileItem> items = new();

        public System.Windows.Media.ImageSource? Icon => Path == "QuickAccess"
            ? BetterFileSys.Services.ShellIconHelper.GetIconForPath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
            : BetterFileSys.Services.ShellIconHelper.GetIconForPath(Path);

        private FileItem? _selectedItem;
        public FileItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    OnSelectionChanged?.Invoke(this, _selectedItem);
                }
            }
        }

        /// <summary>
        /// Callback triggered when selection changes in this column
        /// </summary>
        public System.Action<DirectoryColumnViewModel, FileItem?>? OnSelectionChanged { get; set; }
    }
}
