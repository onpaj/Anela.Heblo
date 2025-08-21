using Anela.Heblo.Application.Features.Journal.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Journal.Handlers
{
    public class GetJournalTagsHandler : IRequestHandler<GetJournalTagsRequest, GetJournalTagsResponse>
    {
        private readonly IJournalTagRepository _tagRepository;

        public GetJournalTagsHandler(IJournalTagRepository tagRepository)
        {
            _tagRepository = tagRepository;
        }

        public async Task<GetJournalTagsResponse> Handle(
            GetJournalTagsRequest request,
            CancellationToken cancellationToken)
        {
            var tags = await _tagRepository.GetAllTagsAsync(cancellationToken);

            return new GetJournalTagsResponse
            {
                Tags = tags.Select(tag => new JournalEntryTagDto
                {
                    Id = tag.Id,
                    Name = tag.Name,
                    Color = tag.Color
                }).ToList()
            };
        }
    }
}