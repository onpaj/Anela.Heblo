using System;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalEntryTagAssignment
    {
        public int JournalEntryId { get; set; }
        public int TagId { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual JournalEntry JournalEntry { get; set; } = null!;
        public virtual JournalEntryTag Tag { get; set; } = null!;
    }
}