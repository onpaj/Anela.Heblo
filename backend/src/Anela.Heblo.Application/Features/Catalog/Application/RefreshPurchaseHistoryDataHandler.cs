using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshPurchaseHistoryDataHandler : IRequestHandler<RefreshPurchaseHistoryDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshPurchaseHistoryDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshPurchaseHistoryDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshPurchaseHistoryData(cancellationToken);
    }
}