using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshErpStockDataHandler : IRequestHandler<RefreshErpStockDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshErpStockDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshErpStockDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshErpStockData(cancellationToken);
    }
}