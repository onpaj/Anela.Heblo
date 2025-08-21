using System.Collections.Generic;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public class GetJournalTagsRequest : IRequest<GetJournalTagsResponse>
    {
    }

    public class GetJournalTagsResponse
    {
        public List<JournalEntryTagDto> Tags { get; set; } = new();
    }
}