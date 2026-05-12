using System;

namespace Anela.Heblo.Domain.Features.Journal
{
    public class JournalIndicator
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public int TotalEntries => DirectEntries;
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
}