using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetTags
{
    public class GetTagsHandler : IRequestHandler<GetTagsRequest, GetTagsResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetTagsHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetTagsResponse> Handle(GetTagsRequest request, CancellationToken cancellationToken)
        {
            var tags = await _repository.GetTagsWithCountsAsync(cancellationToken);

            return new GetTagsResponse
            {
                Tags = tags.Select(t => new TagWithCountDto
                {
                    Id = t.Tag.Id,
                    Name = t.Tag.Name,
                    Count = t.Count,
                }).ToList(),
            };
        }
    }
}
