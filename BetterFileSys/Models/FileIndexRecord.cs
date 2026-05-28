using System;

namespace BetterFileSys.Models
{
    public class FileIndexRecord
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public long ModifiedTicksUtc { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
