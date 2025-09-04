using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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