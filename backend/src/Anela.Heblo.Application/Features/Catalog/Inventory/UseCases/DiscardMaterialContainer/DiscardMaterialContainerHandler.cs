using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;

public class DiscardMaterialContainerHandler : IRequestHandler<DiscardMaterialContainerRequest, DiscardMaterialContainerResponse>
{
    private readonly ILogger<DiscardMaterialContainerHandler> _logger;
    private readonly IMaterialContainerRepository _materialContainerRepository;

    public DiscardMaterialContainerHandler(ILogger<DiscardMaterialContainerHandler> logger, IMaterialContainerRepository materialContainerRepository)
    {
        _logger = logger;
        _materialContainerRepository = materialContainerRepository;
    }

    public async Task<DiscardMaterialContainerResponse> Handle(DiscardMaterialContainerRequest request, CancellationToken cancellationToken)
    {
        var container = await _materialContainerRepository.GetByIdAsync(request.Id, cancellationToken);
        if (container == null)
        {
            _logger.LogWarning("MaterialContainer {Id} not found for delete", request.Id);
            return new DiscardMaterialContainerResponse(ErrorCodes.MaterialContainerNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        await _materialContainerRepository.DeleteAsync(container, cancellationToken);
        await _materialContainerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("MaterialContainer {Id} deleted", request.Id);
        return new DiscardMaterialContainerResponse();
    }
}
