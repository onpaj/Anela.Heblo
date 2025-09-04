using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

public class RefreshConsumedHistoryDataHandler : IRequestHandler<RefreshConsumedHistoryDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshConsumedHistoryDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshConsumedHistoryDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshConsumedHistoryData(cancellationToken);
    }
}