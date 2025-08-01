using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

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