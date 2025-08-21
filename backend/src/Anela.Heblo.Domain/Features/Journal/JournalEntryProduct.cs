using System;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntryProduct
    {
        public int JournalEntryId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProductCode { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual JournalEntry JournalEntry { get; set; } = null!;
    }
}