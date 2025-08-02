using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

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