using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.Mapping;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.UseCases.GetJournalEntry
{
    public class GetJournalEntryHandler : IRequestHandler<GetJournalEntryRequest, GetJournalEntryResponse>
    {
        private readonly IJournalRepository _journalRepository;

        public GetJournalEntryHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<GetJournalEntryResponse> Handle(
            GetJournalEntryRequest request,
            CancellationToken cancellationToken)
        {
            var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null)
            {
                return new GetJournalEntryResponse(ErrorCodes.JournalEntryNotFound, new Dictionary<string, string>
                {
                    { "entryId", request.Id.ToString() }
                });
            }

            return new GetJournalEntryResponse
            {
                Entry = JournalEntryMapper.ToDto(entry)
            };
        }
    }
}