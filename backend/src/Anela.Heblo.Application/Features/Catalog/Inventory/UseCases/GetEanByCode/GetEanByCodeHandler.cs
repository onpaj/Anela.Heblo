using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;

public class GetEanByCodeHandler : IRequestHandler<GetEanByCodeRequest, GetEanByCodeResponse>
{
    private readonly ILogger<GetEanByCodeHandler> _logger;
    private readonly IEanRepository _eanRepository;
    private readonly ILotRepository _lotRepository;

    public GetEanByCodeHandler(
        ILogger<GetEanByCodeHandler> logger,
        IEanRepository eanRepository,
        ILotRepository lotRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
        _lotRepository = lotRepository;
    }

    public async Task<GetEanByCodeResponse> Handle(GetEanByCodeRequest request, CancellationToken cancellationToken)
    {
        var ean = await _eanRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (ean == null)
        {
            _logger.LogWarning("EAN code {Code} not found", request.Code);
            return new GetEanByCodeResponse(ErrorCodes.EanNotFound,
                new Dictionary<string, string> { { "Code", request.Code } });
        }

        var lot = await _lotRepository.GetByIdAsync(ean.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogError("Orphaned EAN {Id} — lot {LotId} missing", ean.Id, ean.LotId);
            return new GetEanByCodeResponse(ErrorCodes.LotNotFound,
                new Dictionary<string, string> { { "LotId", ean.LotId.ToString() } });
        }

        return new GetEanByCodeResponse
        {
            Ean = CreateEansHandler.MapToDto(ean),
            Lot = CreateLotHandler.MapToDto(lot)
        };
    }
}
