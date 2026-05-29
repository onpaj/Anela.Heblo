using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;

public class GetMaterialContainerByCodeHandler : IRequestHandler<GetMaterialContainerByCodeRequest, GetMaterialContainerByCodeResponse>
{
    private readonly ILogger<GetMaterialContainerByCodeHandler> _logger;
    private readonly IMaterialContainerRepository _materialContainerRepository;

    public GetMaterialContainerByCodeHandler(
        ILogger<GetMaterialContainerByCodeHandler> logger,
        IMaterialContainerRepository materialContainerRepository)
    {
        _logger = logger;
        _materialContainerRepository = materialContainerRepository;
    }

    public async Task<GetMaterialContainerByCodeResponse> Handle(GetMaterialContainerByCodeRequest request, CancellationToken cancellationToken)
    {
        var container = await _materialContainerRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (container == null)
        {
            _logger.LogWarning("MaterialContainer code {Code} not found", request.Code);
            return new GetMaterialContainerByCodeResponse(ErrorCodes.MaterialContainerNotFound,
                new Dictionary<string, string> { { "Code", request.Code } });
        }

        return new GetMaterialContainerByCodeResponse
        {
            Container = CreateMaterialContainersHandler.MapToDto(container)
        };
    }
}
