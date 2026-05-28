using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;

public class DeleteLotHandler : IRequestHandler<DeleteLotRequest, DeleteLotResponse>
{
    private readonly ILogger<DeleteLotHandler> _logger;
    private readonly ILotRepository _lotRepository;
    private readonly IMaterialContainerRepository _materialContainerRepository;

    public DeleteLotHandler(ILogger<DeleteLotHandler> logger, ILotRepository lotRepository, IMaterialContainerRepository materialContainerRepository)
    {
        _logger = logger;
        _lotRepository = lotRepository;
        _materialContainerRepository = materialContainerRepository;
    }

    public async Task<DeleteLotResponse> Handle(DeleteLotRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.Id, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {Id} not found for delete", request.Id);
            return new DeleteLotResponse(ErrorCodes.LotNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        if (await _materialContainerRepository.AnyByLotIdAsync(request.Id, cancellationToken))
        {
            _logger.LogWarning("Cannot delete lot {Id} — it still has MaterialContainers", request.Id);
            return new DeleteLotResponse(ErrorCodes.LotHasEans, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        await _lotRepository.DeleteAsync(lot, cancellationToken);
        await _lotRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Lot {Id} deleted", request.Id);
        return new DeleteLotResponse();
    }
}
