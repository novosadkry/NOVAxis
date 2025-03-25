using System;
using System.ComponentModel.DataAnnotations;

namespace NOVAxis.Database.Entities
{
    public class DownloadInfo
    {
        public Guid Uuid { get; set; }
        public ulong UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        [MaxLength(255)] public string Path { get; set; }
        [MaxLength(255)] public string SourceUrl { get; set; }
    }
}
