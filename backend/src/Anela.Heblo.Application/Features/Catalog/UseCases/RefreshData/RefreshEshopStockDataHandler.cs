using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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