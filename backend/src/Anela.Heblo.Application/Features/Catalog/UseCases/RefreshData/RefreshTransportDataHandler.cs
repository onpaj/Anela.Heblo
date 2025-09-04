using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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