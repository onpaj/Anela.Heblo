using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

public class RefreshManufactureCostDataHandler : IRequestHandler<RefreshManufactureCostDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshManufactureCostDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshManufactureCostDataRequest request, CancellationToken cancellationToken)
    {
        await ((CatalogRepository)_catalogRepository).RefreshManufactureCostData(cancellationToken);
    }
}