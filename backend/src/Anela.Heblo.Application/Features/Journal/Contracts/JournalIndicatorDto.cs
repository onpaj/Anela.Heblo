using System;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class JournalIndicatorDto
    {
        public string ProductCode { get; set; } = null!;
        public int DirectEntries { get; set; }
        public DateTime? LastEntryDate { get; set; }
        public bool HasRecentEntries { get; set; } // Within last 30 days
    }
}