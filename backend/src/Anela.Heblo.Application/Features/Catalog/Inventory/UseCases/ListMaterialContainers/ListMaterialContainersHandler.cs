using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;

public class ListMaterialContainersHandler : IRequestHandler<ListMaterialContainersRequest, ListMaterialContainersResponse>
{
    private readonly ILogger<ListMaterialContainersHandler> _logger;
    private readonly IMaterialContainerRepository _materialContainerRepository;

    public ListMaterialContainersHandler(ILogger<ListMaterialContainersHandler> logger, IMaterialContainerRepository materialContainerRepository)
    {
        _logger = logger;
        _materialContainerRepository = materialContainerRepository;
    }

    public async Task<ListMaterialContainersResponse> Handle(ListMaterialContainersRequest request, CancellationToken cancellationToken)
    {
        var result = await _materialContainerRepository.GetPaginatedAsync(
            request.MaterialCode,
            request.LotCode,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListMaterialContainersResponse
        {
            Containers = result.Items.Select(CreateMaterialContainersHandler.MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
