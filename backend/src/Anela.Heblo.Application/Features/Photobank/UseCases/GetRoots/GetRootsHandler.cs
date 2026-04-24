using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots
{
    public class GetRootsHandler : IRequestHandler<GetRootsRequest, GetRootsResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetRootsHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetRootsResponse> Handle(GetRootsRequest request, CancellationToken cancellationToken)
        {
            var roots = await _repository.GetRootsAsync(cancellationToken);

            return new GetRootsResponse
            {
                Roots = roots.Select(r => new IndexRootDto
                {
                    Id = r.Id,
                    SharePointPath = r.SharePointPath,
                    DisplayName = r.DisplayName,
                    DriveId = r.DriveId,
                    RootItemId = r.RootItemId,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    LastIndexedAt = r.LastIndexedAt,
                }).ToList(),
            };
        }
    }
}
