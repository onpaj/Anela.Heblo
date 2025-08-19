using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

public class RefreshManufactureHistoryDataHandler : IRequestHandler<RefreshManufactureHistoryDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshManufactureHistoryDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshManufactureHistoryDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshManufactureHistoryData(cancellationToken);
    }
}