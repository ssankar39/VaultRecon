namespace BetterFileSys.Models
{
    /// <summary>
    /// Model representing a system hard drive partition
    /// </summary>
    public class DriveItemViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string FreeSpaceText { get; set; } = string.Empty;
        public double PercentUsed { get; set; }
        public System.Windows.Media.ImageSource? Icon => BetterFileSys.Services.ShellIconHelper.GetIconForPath(Path);
    }
}
