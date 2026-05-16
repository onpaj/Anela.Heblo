using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;

public class UpdateLotHandler : IRequestHandler<UpdateLotRequest, UpdateLotResponse>
{
    private readonly ILogger<UpdateLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly ICurrentUserService _currentUserService;

    public UpdateLotHandler(ILogger<UpdateLotHandler> logger, ILotRepository lotRepository, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _currentUserService = currentUserService;
    }

    public async Task<UpdateLotResponse> Handle(UpdateLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found for update", request.Id);
            return new UpdateLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        lot.Update(request.Expiration, request.ReceivedDate, request.Notes, currentUser.Name ?? "System");
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {Id} updated", lot.Id);
        return new UpdateLotResponse { Lot = CreateLotHandler.MapToDto(lot) };
    }
}
