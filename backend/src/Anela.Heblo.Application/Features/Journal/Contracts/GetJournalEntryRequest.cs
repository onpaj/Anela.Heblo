using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class GetJournalEntryRequest : IRequest<GetJournalEntryResponse>
    {
        public int Id { get; set; }
    }

    public class GetJournalEntryResponse : BaseResponse
    {
        public JournalEntryDto? Entry { get; set; }

        public GetJournalEntryResponse() : base() { }
        public GetJournalEntryResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
    }
}