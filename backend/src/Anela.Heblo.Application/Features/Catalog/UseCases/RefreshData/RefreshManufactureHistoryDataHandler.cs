using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

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