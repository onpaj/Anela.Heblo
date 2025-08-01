using MediatR;
using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.Application;

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