using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshEshopPricesDataHandler : IRequestHandler<RefreshEshopPricesDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshEshopPricesDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshEshopPricesDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshEshopPricesData(cancellationToken);
    }
}