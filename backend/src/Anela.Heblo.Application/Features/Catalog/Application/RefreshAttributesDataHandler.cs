using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.features.catalog.contracts;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Application;

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