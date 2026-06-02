using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;

public class GetLastUsedLotForMaterialHandler
    : IRequestHandler<GetLastUsedLotForMaterialRequest, GetLastUsedLotForMaterialResponse>
{
    private readonly ILogger<GetLastUsedLotForMaterialHandler> _logger;
    private readonly IMaterialContainerRepository _repo;

    public GetLastUsedLotForMaterialHandler(
        ILogger<GetLastUsedLotForMaterialHandler> logger,
        IMaterialContainerRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    public async Task<GetLastUsedLotForMaterialResponse> Handle(
        GetLastUsedLotForMaterialRequest request, CancellationToken cancellationToken)
    {
        var lotCode = await _repo.GetLastUsedLotCodeForMaterialAsync(request.MaterialCode, cancellationToken);
        return new GetLastUsedLotForMaterialResponse { LotCode = lotCode };
    }
}
