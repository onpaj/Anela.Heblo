using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Application.Features.Journal.Pagination;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntries
{
    public class GetJournalEntriesHandler : IRequestHandler<GetJournalEntriesRequest, GetJournalEntriesResponse>
    {
        private readonly IJournalRepository _journalRepository;

        public GetJournalEntriesHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<GetJournalEntriesResponse> Handle(
            GetJournalEntriesRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _journalRepository.GetEntriesAsync(
                request.PageNumber,
                request.PageSize,
                request.SortBy,
                request.SortDirection,
                cancellationToken);

            var entryDtos = result.Items.Select(JournalEntryMapper.ToDto).ToList();

            var (totalPages, hasNextPage, hasPreviousPage) =
                JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize);

            return new GetJournalEntriesResponse
            {
                Entries = entryDtos,
                TotalCount = result.TotalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = totalPages,
                HasNextPage = hasNextPage,
                HasPreviousPage = hasPreviousPage
            };
        }
    }
}
