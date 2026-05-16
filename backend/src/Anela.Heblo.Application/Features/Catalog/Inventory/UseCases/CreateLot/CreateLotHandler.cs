using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotHandler : IRequestHandler<CreateLotRequest, CreateLotResponse>
{
    private readonly ILogger<CreateLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;

    public CreateLotHandler(
        ILogger<CreateLotHandler> logger,
        ILotRepository lotRepository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
    }

    public async Task<CreateLotResponse> Handle(CreateLotRequest request, CancellationToken cancellationToken)
    {
        var material = await _catalogRepository.GetByIdAsync(request.MaterialCode, cancellationToken);
        if (material == null)
        {
            _logger.LogWarning("Material {MaterialCode} not found", request.MaterialCode);
            return new CreateLotResponse(ErrorCodes.InventoryMaterialNotFound,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode } });
        }

        if (material.Type != ProductType.Material)
        {
            _logger.LogWarning("Material {MaterialCode} is not of type Material (is {Type})", request.MaterialCode, material.Type);
            return new CreateLotResponse(ErrorCodes.InventoryMaterialInvalidType,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode }, { "ProductType", material.Type.ToString() } });
        }

        if (await _lotRepository.ExistsAsync(request.MaterialCode, request.LotCode, cancellationToken))
        {
            _logger.LogWarning("Lot ({MaterialCode}, {LotCode}) already exists", request.MaterialCode, request.LotCode);
            return new CreateLotResponse(ErrorCodes.LotAlreadyExists,
                new Dictionary<string, string> { { "MaterialCode", request.MaterialCode }, { "LotCode", request.LotCode } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var lot = new Lot(request.MaterialCode, request.LotCode, request.Expiration, request.ReceivedDate, request.Notes, createdBy);
        await _lotRepository.AddAsync(lot, cancellationToken);
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {LotId} ({MaterialCode}/{LotCode}) created", lot.Id, lot.MaterialCode, lot.LotCode);

        return new CreateLotResponse { Lot = MapToDto(lot) };
    }

    internal static LotDto MapToDto(Lot lot) => new()
    {
        Id = lot.Id,
        MaterialCode = lot.MaterialCode,
        LotCode = lot.LotCode,
        Expiration = lot.Expiration,
        ReceivedDate = lot.ReceivedDate,
        Notes = lot.Notes,
        CreatedAt = lot.CreatedAt,
        CreatedBy = lot.CreatedBy,
        UpdatedAt = lot.UpdatedAt,
        UpdatedBy = lot.UpdatedBy
    };
}
