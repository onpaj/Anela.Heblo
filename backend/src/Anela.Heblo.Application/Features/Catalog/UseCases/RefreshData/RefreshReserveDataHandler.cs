using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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