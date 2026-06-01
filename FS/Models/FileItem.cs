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

        public string RelativeAge
        {
            get
            {
                var age = DateTime.UtcNow - Modified.ToUniversalTime();
                if (age.TotalHours < 24)
                    return $"{(int)Math.Max(1, age.TotalHours)} h";
                if (age.TotalDays < 30)
                    return $"{(int)Math.Max(1, age.TotalDays)} d";
                if (age.TotalDays < 365)
                    return $"{(int)Math.Max(1, age.TotalDays / 30)} M";
                return $"{(int)Math.Max(1, age.TotalDays / 365)} y";
            }
        }

        public string RelativeAgeColor
        {
            get
            {
                var age = DateTime.UtcNow - Modified.ToUniversalTime();
                if (age.TotalHours < 24)
                    return "#D97706"; // Amber/yellow
                if (age.TotalDays < 30)
                    return "#059669"; // Green
                if (age.TotalDays < 365)
                    return "#0891B2"; // Cyan/Teal
                return "#4B5563"; // Grey
            }
        }

        public string ExtensionTag
        {
            get
            {
                if (IsDirectory) return "[DIR]";
                if (string.IsNullOrEmpty(FileType)) return "file";
                return FileType.TrimStart('.').ToLower();
            }
        }

        public string FormattedSize
        {
            get
            {
                if (IsDirectory) return "";
                string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
                double size = Size;
                int suffixIndex = 0;
                while (size >= 1024 && suffixIndex < suffixes.Length - 1)
                {
                    size /= 1024;
                    suffixIndex++;
                }
                return $"{size:F1} {suffixes[suffixIndex]}";
            }
        }

        public override string ToString() => Name;
    }
}

