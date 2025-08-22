using Anela.Heblo.Application.Features.Journal.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Handlers
{
    public class GetJournalEntryHandler : IRequestHandler<GetJournalEntryRequest, JournalEntryDto?>
    {
        private readonly IJournalRepository _journalRepository;

        public GetJournalEntryHandler(IJournalRepository journalRepository)
        {
            _journalRepository = journalRepository;
        }

        public async Task<JournalEntryDto?> Handle(
            GetJournalEntryRequest request,
            CancellationToken cancellationToken)
        {
            var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null)
            {
                return null;
            }

            return new JournalEntryDto
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
            };
        }
    }
}