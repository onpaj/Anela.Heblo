using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class DeleteJournalEntryRequest : IRequest<DeleteJournalEntryResponse>
    {
        public int Id { get; set; }
    }

    public class DeleteJournalEntryResponse : BaseResponse
    {
        public int Id { get; set; }
        public string Message { get; set; } = "Journal entry deleted successfully";

        public DeleteJournalEntryResponse() : base() { }
        public DeleteJournalEntryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
    }
}