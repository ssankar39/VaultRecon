using System;

namespace BetterFileSys.Models
{
    /// <summary>
    /// Represents a file or directory item
    /// </summary>
    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string FileType { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
