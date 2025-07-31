using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshSalesDataHandler : IRequestHandler<RefreshSalesDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshSalesDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshSalesDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshSalesData(cancellationToken);
    }
}