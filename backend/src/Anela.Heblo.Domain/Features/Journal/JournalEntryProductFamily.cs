using System;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntryProductFamily
    {
        public int JournalEntryId { get; set; }

        [Required]
        [MaxLength(20)]
        public string ProductCodePrefix { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual JournalEntry JournalEntry { get; set; } = null!;
    }
}