using System;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public class PhotobankIndexRoot
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(1000)]
        public string SharePointPath { get; set; } = null!;

        [MaxLength(200)]
        public string? DisplayName { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        [MaxLength(100)]
        public string? CreatedByUserId { get; set; }

        [MaxLength(500)]
        public string? DriveId { get; set; }

        [MaxLength(500)]
        public string? RootItemId { get; set; }

        [MaxLength(2000)]
        public string? DeltaLink { get; set; }

        public DateTime? LastIndexedAt { get; set; }
    }
}
