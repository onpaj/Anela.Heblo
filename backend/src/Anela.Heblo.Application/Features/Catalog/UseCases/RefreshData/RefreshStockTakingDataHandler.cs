using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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