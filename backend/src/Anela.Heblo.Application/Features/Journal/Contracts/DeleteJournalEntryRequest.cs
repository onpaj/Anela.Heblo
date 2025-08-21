using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class DeleteJournalEntryRequest : IRequest<Unit>
    {
        public int Id { get; set; }
    }
}