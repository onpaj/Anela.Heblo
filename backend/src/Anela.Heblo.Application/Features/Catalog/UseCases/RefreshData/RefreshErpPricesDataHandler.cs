using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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