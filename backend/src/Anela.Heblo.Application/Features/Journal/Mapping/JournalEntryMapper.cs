using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;

namespace Anela.Heblo.Application.Features.Journal.Mapping
{
    internal static class JournalEntryMapper
    {
        public static JournalEntryDto ToDto(JournalEntry entry)
        {
            return new JournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                Content = entry.Content,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                CreatedByUsername = entry.CreatedByUsername,
                ModifiedByUserId = entry.ModifiedByUserId,
                ModifiedByUsername = entry.ModifiedByUsername,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Where(ta => ta.Tag != null)
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
            };
        }

        public static SearchJournalEntryDto ToSearchDto(JournalEntry entry)
        {
            return new SearchJournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                CreatedByUsername = entry.CreatedByUsername,
                ModifiedByUserId = entry.ModifiedByUserId,
                ModifiedByUsername = entry.ModifiedByUsername,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCodePrefix)
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Where(ta => ta.Tag != null)
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
                // ContentPreview defaults to string.Empty; HighlightedTerms defaults to new List<string>().
                // The search handler overwrites these with real values before returning.
            };
        }
    }
}
