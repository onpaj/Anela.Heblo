using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class SearchJournalEntryDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public DateTime EntryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }

        public List<string> AssociatedProducts { get; set; } = new();
        public List<JournalEntryTagDto> Tags { get; set; } = new();

        public string ContentPreview { get; set; } = string.Empty;
        public List<string> HighlightedTerms { get; set; } = new();
    }
}
