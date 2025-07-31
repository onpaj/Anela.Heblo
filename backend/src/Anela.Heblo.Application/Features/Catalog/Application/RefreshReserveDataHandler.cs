using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

public class RefreshReserveDataHandler : IRequestHandler<RefreshReserveDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshReserveDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshReserveDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshReserveData(cancellationToken);
    }
}