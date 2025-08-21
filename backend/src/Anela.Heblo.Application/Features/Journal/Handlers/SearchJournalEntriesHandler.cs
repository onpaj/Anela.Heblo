using Anela.Heblo.Application.Features.Journal.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Handlers
{
    public class SearchJournalEntriesHandler : IRequestHandler<SearchJournalEntriesRequest, SearchJournalEntriesResponse>
    {
        private readonly IJournalRepository _journalRepository;

        public SearchJournalEntriesHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<SearchJournalEntriesResponse> Handle(
            SearchJournalEntriesRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _journalRepository.SearchEntriesAsync(request, cancellationToken);

            var entryDtos = result.Items.Select(entry => new JournalEntryDto
            {
                Id = entry.Id,
                Title = entry.Title,
                Content = entry.Content,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                ModifiedAt = entry.ModifiedAt,
                CreatedByUserId = entry.CreatedByUserId,
                ModifiedByUserId = entry.ModifiedByUserId,
                AssociatedProducts = entry.ProductAssociations
                    .Select(pa => pa.ProductCode)
                    .Concat(entry.ProductFamilyAssociations
                        .Select(pfa => pfa.ProductCodePrefix))
                    .Distinct()
                    .ToList(),
                Tags = entry.TagAssignments
                    .Select(ta => new JournalEntryTagDto
                    {
                        Id = ta.Tag.Id,
                        Name = ta.Tag.Name,
                        Color = ta.Tag.Color
                    })
                    .ToList()
            }).ToList();

            // Add content previews for search results
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                foreach (var dto in entryDtos)
                {
                    dto.ContentPreview = CreateContentPreview(dto.Content, request.SearchText);
                    dto.HighlightedTerms = ExtractHighlightTerms(request.SearchText);
                }
            }

            return new SearchJournalEntriesResponse
            {
                Entries = entryDtos,
                TotalCount = result.TotalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling((double)result.TotalCount / request.PageSize),
                HasNextPage = request.PageNumber * request.PageSize < result.TotalCount,
                HasPreviousPage = request.PageNumber > 1
            };
        }

        private static string CreateContentPreview(string content, string searchText, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(searchText))
                return content.Length <= maxLength ? content : content[..maxLength] + "...";

            var index = content.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (index == -1)
                return content.Length <= maxLength ? content : content[..maxLength] + "...";

            var start = Math.Max(0, index - maxLength / 2);
            var length = Math.Min(maxLength, content.Length - start);

            var preview = content.Substring(start, length);
            if (start > 0) preview = "..." + preview;
            if (start + length < content.Length) preview += "...";

            return preview;
        }

        private static List<string> ExtractHighlightTerms(string searchText)
        {
            return searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(term => term.Length > 2)
                            .ToList();
        }
    }
}