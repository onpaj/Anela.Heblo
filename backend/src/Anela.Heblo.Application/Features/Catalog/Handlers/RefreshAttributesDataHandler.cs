using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog;

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