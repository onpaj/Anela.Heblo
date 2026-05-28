using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;

public class GetMaterialContainerByCodeHandler : IRequestHandler<GetMaterialContainerByCodeRequest, GetMaterialContainerByCodeResponse>
{
    private readonly ILogger<GetMaterialContainerByCodeHandler> _logger;
    private readonly IMaterialContainerRepository _materialContainerRepository;
    private readonly ILotRepository _lotRepository;

    public GetMaterialContainerByCodeHandler(
        ILogger<GetMaterialContainerByCodeHandler> logger,
        IMaterialContainerRepository materialContainerRepository,
        ILotRepository lotRepository)
    {
        _logger = logger;
        _materialContainerRepository = materialContainerRepository;
        _lotRepository = lotRepository;
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

        var lot = await _lotRepository.GetByIdAsync(container.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogError("Orphaned MaterialContainer {Id} — lot {LotId} missing", container.Id, container.LotId);
            return new GetMaterialContainerByCodeResponse(ErrorCodes.LotNotFound,
                new Dictionary<string, string> { { "LotId", container.LotId.ToString() } });
        }

        return new GetMaterialContainerByCodeResponse
        {
            Container = CreateMaterialContainersHandler.MapToDto(container),
            Lot = CreateLotHandler.MapToDto(lot)
        };
    }
}
