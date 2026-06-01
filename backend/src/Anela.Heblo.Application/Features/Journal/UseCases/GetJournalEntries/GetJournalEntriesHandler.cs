using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
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

            return new GetJournalEntriesResponse
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
    }
}
