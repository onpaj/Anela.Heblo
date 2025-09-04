using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

public class RefreshLotsDataHandler : IRequestHandler<RefreshLotsDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshLotsDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshLotsDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshLotsData(cancellationToken);
    }
}