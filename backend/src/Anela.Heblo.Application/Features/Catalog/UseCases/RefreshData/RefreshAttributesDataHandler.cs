using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;

public class RefreshAttributesDataHandler : IRequestHandler<RefreshAttributesDataRequest>
{
    private readonly ICatalogRepository _catalogRepository;

    public RefreshAttributesDataHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public async Task Handle(RefreshAttributesDataRequest request, CancellationToken cancellationToken)
    {
        await _catalogRepository.RefreshAttributesData(cancellationToken);
    }
}