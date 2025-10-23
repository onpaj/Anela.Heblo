using System;
using System.Collections.Generic;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class JournalEntryDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string Content { get; set; } = null!;
        public DateTime EntryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string CreatedByUserId { get; set; } = null!;
        public string? CreatedByUsername { get; set; }
        public string? ModifiedByUserId { get; set; }
        public string? ModifiedByUsername { get; set; }

        public List<string> AssociatedProducts { get; set; } = new();
        public List<JournalEntryTagDto> Tags { get; set; } = new();

        // For search results
        public string? ContentPreview { get; set; }
        public List<string> HighlightedTerms { get; set; } = new();
    }

    public class JournalEntryTagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
    }
}