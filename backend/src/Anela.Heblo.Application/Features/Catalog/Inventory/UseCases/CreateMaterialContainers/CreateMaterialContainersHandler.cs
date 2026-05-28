using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersHandler : IRequestHandler<CreateMaterialContainersRequest, CreateMaterialContainersResponse>
{
    private readonly ILogger<CreateMaterialContainersHandler> _logger;
    private readonly IMaterialContainerRepository _materialContainerRepository;
    private readonly ILotRepository _lotRepository;
    private readonly IMaterialContainerCodeGenerator _materialContainerCodeGenerator;
    private readonly ICurrentUserService _currentUserService;

    public CreateMaterialContainersHandler(
        ILogger<CreateMaterialContainersHandler> logger,
        IMaterialContainerRepository materialContainerRepository,
        ILotRepository lotRepository,
        IMaterialContainerCodeGenerator materialContainerCodeGenerator,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _materialContainerRepository = materialContainerRepository;
        _lotRepository = lotRepository;
        _materialContainerCodeGenerator = materialContainerCodeGenerator;
        _currentUserService = currentUserService;
    }

    public async Task<CreateMaterialContainersResponse> Handle(CreateMaterialContainersRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {LotId} not found for MaterialContainer creation", request.LotId);
            return new CreateMaterialContainersResponse(ErrorCodes.LotNotFound,
                new Dictionary<string, string> { { "LotId", request.LotId.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var codes = await _materialContainerCodeGenerator.GenerateAsync(request.Items.Count, cancellationToken);
        var containers = request.Items
            .Select((item, i) => new MaterialContainer(codes[i], request.LotId, item.Amount, item.Unit, createdBy))
            .ToList();

        await _materialContainerRepository.AddRangeAsync(containers, cancellationToken);
        await _materialContainerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {Count} MaterialContainers for Lot {LotId}", containers.Count, request.LotId);

        return new CreateMaterialContainersResponse
        {
            Containers = containers.Select(MapToDto).ToList()
        };
    }

    internal static MaterialContainerDto MapToDto(MaterialContainer container) => new()
    {
        Id = container.Id,
        Code = container.Code,
        LotId = container.LotId,
        Amount = container.Amount,
        Unit = container.Unit,
        CreatedAt = container.CreatedAt,
        CreatedBy = container.CreatedBy
    };
}
