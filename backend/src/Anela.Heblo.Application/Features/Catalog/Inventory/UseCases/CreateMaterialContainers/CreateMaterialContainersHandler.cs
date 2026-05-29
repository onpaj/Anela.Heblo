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
    private readonly IMaterialContainerRepository _containerRepository;
    private readonly IMaterialContainerCodeGenerator _codeGenerator;
    private readonly ICurrentUserService _currentUserService;

    public CreateMaterialContainersHandler(
        ILogger<CreateMaterialContainersHandler> logger,
        IMaterialContainerRepository containerRepository,
        IMaterialContainerCodeGenerator codeGenerator,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _containerRepository = containerRepository;
        _codeGenerator = codeGenerator;
        _currentUserService = currentUserService;
    }

    public async Task<CreateMaterialContainersResponse> Handle(
        CreateMaterialContainersRequest request, CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var codes = await _codeGenerator.GenerateAsync(request.Items.Count, cancellationToken);
        var containers = request.Items
            .Select((item, i) => new MaterialContainer(
                codes[i], item.MaterialCode, item.LotCode, item.Amount, item.Unit, createdBy))
            .ToList();

        await _containerRepository.AddRangeAsync(containers, cancellationToken);
        await _containerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {Count} MaterialContainers", containers.Count);

        return new CreateMaterialContainersResponse
        {
            Containers = containers.Select(MapToDto).ToList()
        };
    }

    internal static MaterialContainerDto MapToDto(MaterialContainer c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        MaterialCode = c.MaterialCode,
        LotCode = c.LotCode,
        Amount = c.Amount,
        Unit = c.Unit,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy
    };
}
