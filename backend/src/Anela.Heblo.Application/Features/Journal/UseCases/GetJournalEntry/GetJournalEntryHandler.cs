using Anela.Heblo.Application.Features.Journal.Contracts;
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
                Entry = new JournalEntryDto
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
                        .Select(pa => pa.ProductCodePrefix)
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
                }
            };
        }
    }
}