using System;

namespace BetterFileSys.Models
{
    /// <summary>
    /// Represents a search result with ranking and metadata
    /// </summary>
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileType { get; set; } = string.Empty;
        public DateTime Modified { get; set; }
        public double RelevanceScore { get; set; } // 0.0 to 1.0
        public SearchType SearchType { get; set; }

        public string FormattedSize => FormatBytes(FileSize);

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
                if (string.IsNullOrEmpty(FileType)) return "file";
                return FileType.TrimStart('.').ToLower();
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public enum SearchType
    {
        Keyword,
        Semantic,
        Hybrid
    }
}

