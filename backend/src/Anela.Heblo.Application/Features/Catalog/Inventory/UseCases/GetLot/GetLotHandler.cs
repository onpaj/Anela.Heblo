using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;

public class GetLotHandler : IRequestHandler<GetLotRequest, GetLotResponse>
{
    private readonly ILogger<GetLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly IEanRepository _eanRepository;

    public GetLotHandler(ILogger<GetLotHandler> logger, ILotRepository lotRepository, IEanRepository eanRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _eanRepository = eanRepository;
    }

    public async Task<GetLotResponse> Handle(GetLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdWithEansAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found", request.Id);
            return new GetLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var eanPage = await _eanRepository.GetPaginatedAsync(request.Id, null, 1, 100, cancellationToken);
        var eanDtos = eanPage.Items.Select(e => new EanDto
        {
            Id = e.Id,
            Code = e.Code,
            LotId = e.LotId,
            Amount = e.Amount,
            Unit = e.Unit,
            CreatedAt = e.CreatedAt,
            CreatedBy = e.CreatedBy
        }).ToList();

        return new GetLotResponse { Lot = CreateLotHandler.MapToDto(lot), Eans = eanDtos };
    }
}
