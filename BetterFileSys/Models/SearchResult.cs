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
