using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

public class RefreshEshopStockDataHandler : IRequestHandler<RefreshEshopStockDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshEshopStockDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshEshopStockDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshEshopStockData(cancellationToken);
    }
}