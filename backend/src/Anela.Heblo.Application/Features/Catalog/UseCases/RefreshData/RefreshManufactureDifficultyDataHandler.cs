using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

public class RefreshManufactureDifficultyDataHandler : IRequestHandler<RefreshManufactureDifficultyDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshManufactureDifficultyDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshManufactureDifficultyDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshManufactureDifficultySettingsData(null, cancellationToken);
    }
}