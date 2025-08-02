using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;

namespace Anela.Heblo.Application.Features.Catalog;

public class RefreshTransportDataHandler : IRequestHandler<RefreshTransportDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshTransportDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshTransportDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshTransportData(cancellationToken);
    }
}