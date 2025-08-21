using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class GetJournalEntryRequest : IRequest<JournalEntryDto?>
    {
        public int Id { get; set; }
    }
}