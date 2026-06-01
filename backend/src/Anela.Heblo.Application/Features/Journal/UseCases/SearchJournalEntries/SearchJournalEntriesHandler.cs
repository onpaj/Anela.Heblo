using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.SearchJournalEntries
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
            var result = await _journalRepository.SearchEntriesAsync(
                searchText: request.SearchText,
                dateFrom: request.DateFrom,
                dateTo: request.DateTo,
                productCodePrefix: request.ProductCodePrefix,
                tagIds: request.TagIds,
                createdByUserId: request.CreatedByUserId,
                pageNumber: request.PageNumber,
                pageSize: request.PageSize,
                sortBy: request.SortBy,
                sortDirection: request.SortDirection,
                cancellationToken: cancellationToken);

            var searchText = request.SearchText ?? string.Empty;
            var hasSearchText = !string.IsNullOrEmpty(searchText);

            var entryDtos = result.Items
                .Select(entry =>
                {
                    var dto = JournalEntryMapper.ToSearchDto(entry);
                    dto.ContentPreview = CreateContentPreview(entry.Content, searchText);
                    if (hasSearchText)
                    {
                        dto.HighlightedTerms = ExtractHighlightTerms(searchText);
                    }
                    return dto;
                })
                .ToList();

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
            if (string.IsNullOrEmpty(content)) return string.Empty;

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
