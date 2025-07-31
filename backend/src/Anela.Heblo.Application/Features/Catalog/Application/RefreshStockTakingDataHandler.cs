using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshStockTakingDataHandler : IRequestHandler<RefreshStockTakingDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshStockTakingDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshStockTakingDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshStockTakingData(cancellationToken);
    }
}