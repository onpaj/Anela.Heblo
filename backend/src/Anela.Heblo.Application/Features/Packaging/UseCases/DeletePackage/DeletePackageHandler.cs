using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.DeletePackage;

public class DeletePackageHandler : IRequestHandler<DeletePackageRequest, DeletePackageResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IShipmentClient _shipmentClient;
    private readonly ILogger<DeletePackageHandler> _logger;

    public DeletePackageHandler(
        IPackageRepository repo,
        IShipmentClient shipmentClient,
        ILogger<DeletePackageHandler> logger)
    {
        _repo = repo;
        _shipmentClient = shipmentClient;
        _logger = logger;
    }

    public async Task<DeletePackageResponse> Handle(DeletePackageRequest request, CancellationToken cancellationToken)
    {
        var package = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (package is null)
            return new DeletePackageResponse(ErrorCodes.PackageNotFound);

        try
        {
            await _shipmentClient.CancelShipmentAsync(package.ShipmentGuid, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to cancel Shoptet shipment {ShipmentGuid} for package {PackageId}; deleting local row anyway",
                package.ShipmentGuid, package.Id);
        }

        await _repo.DeleteAsync(package, cancellationToken);
        return new DeletePackageResponse(deleted: true);
    }
}
