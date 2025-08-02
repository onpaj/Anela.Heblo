using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

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