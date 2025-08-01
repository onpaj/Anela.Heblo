using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshErpPricesDataHandler : IRequestHandler<RefreshErpPricesDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshErpPricesDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshErpPricesDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshErpPricesData(cancellationToken);
    }
}