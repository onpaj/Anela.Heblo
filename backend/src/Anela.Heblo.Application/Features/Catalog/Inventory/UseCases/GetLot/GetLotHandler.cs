using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;

public class GetLotHandler : IRequestHandler<GetLotRequest, GetLotResponse>
{
    private readonly ILogger<GetLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly IMaterialContainerRepository _materialContainerRepository;

    public GetLotHandler(ILogger<GetLotHandler> logger, ILotRepository lotRepository, IMaterialContainerRepository materialContainerRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _materialContainerRepository = materialContainerRepository;
    }

    public async Task<GetLotResponse> Handle(GetLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdWithEansAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found", request.Id);
            return new GetLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var containerPage = await _materialContainerRepository.GetPaginatedAsync(
            lot.MaterialCode, lot.LotCode, page: 1, pageSize: 100, cancellationToken);
        var containerDtos = containerPage.Items.Select(CreateMaterialContainersHandler.MapToDto).ToList();

        return new GetLotResponse { Lot = CreateLotHandler.MapToDto(lot), Containers = containerDtos };
    }
}
