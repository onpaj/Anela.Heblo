using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class PhotoDto
    {
        public int Id { get; set; }
        public string SharePointFileId { get; set; } = null!;
        public string? DriveId { get; set; }
        public string Name { get; set; } = null!;
        public string FolderPath { get; set; } = null!;
        public string? SharePointWebUrl { get; set; }
        public long? FileSizeBytes { get; set; }
        public DateTime LastModifiedAt { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }
}
