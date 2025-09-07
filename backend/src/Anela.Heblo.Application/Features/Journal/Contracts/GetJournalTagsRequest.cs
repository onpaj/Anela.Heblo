using System.Collections.Generic;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class GetJournalTagsRequest : IRequest<GetJournalTagsResponse>
    {
    }

    public class GetJournalTagsResponse : BaseResponse
    {
        public List<JournalEntryTagDto> Tags { get; set; } = new();
    }
}