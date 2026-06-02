using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;

public class DiscardMaterialContainerHandler : IRequestHandler<DiscardMaterialContainerRequest, DiscardMaterialContainerResponse>
{
    private readonly ILogger<DiscardMaterialContainerHandler> _logger;
    private readonly IMaterialContainerRepository _containerRepository;
    private readonly ICurrentUserService _currentUserService;

    public DiscardMaterialContainerHandler(
        ILogger<DiscardMaterialContainerHandler> logger,
        IMaterialContainerRepository containerRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _containerRepository = containerRepository;
        _currentUserService = currentUserService;
    }

    public async Task<DiscardMaterialContainerResponse> Handle(
        DiscardMaterialContainerRequest request, CancellationToken cancellationToken)
    {
        var container = await _containerRepository.GetByIdAsync(request.Id, cancellationToken);
        if (container == null)
        {
            return new DiscardMaterialContainerResponse(
                ErrorCodes.MaterialContainerNotFound,
                new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var updatedBy = currentUser.Name ?? "System";
        container.Discard(updatedBy);

        await _containerRepository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("MaterialContainer {Id} discarded by {User}", request.Id, updatedBy);
        return new DiscardMaterialContainerResponse();
    }
}
