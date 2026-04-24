using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public class Photo
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(500)]
        public string SharePointFileId { get; set; } = null!; // unique

        [Required]
        [MaxLength(2000)]
        public string FolderPath { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string FileName { get; set; } = null!;

        [MaxLength(2000)]
        public string? SharePointWebUrl { get; set; }

        [MaxLength(50)]
        public string? MimeType { get; set; }

        public long? FileSizeBytes { get; set; }

        public DateTime? TakenAt { get; set; }

        public DateTime IndexedAt { get; set; }

        public DateTime ModifiedAt { get; set; }

        public virtual ICollection<PhotoTag> Tags { get; set; } = new List<PhotoTag>();
    }
}
