using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingHandler : IRequestHandler<SetCarrierCoolingRequest, SetCarrierCoolingResponse>
{
    private readonly ICarrierCoolingRepository _repository;
    private readonly IShippingMethodCatalog _catalog;
    private readonly ICurrentUserService _currentUserService;

    public SetCarrierCoolingHandler(
        ICarrierCoolingRepository repository,
        IShippingMethodCatalog catalog,
        ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetCarrierCoolingResponse> Handle(
        SetCarrierCoolingRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
        {
            return new SetCarrierCoolingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Unauthorized,
            };
        }

        var isValidCombo = _catalog.GetAvailableDeliveryOptions()
            .Any(o => o.Carrier == request.Carrier && o.Handling == request.DeliveryHandling);

        if (!isValidCombo)
        {
            return new SetCarrierCoolingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "message", $"Combination of Carrier '{request.Carrier}' and DeliveryHandling '{request.DeliveryHandling}' is not available." }
                }
            };
        }

        var setting = new CarrierCoolingSetting(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            currentUser.Id,
            request.CoolingText);

        await _repository.UpsertAsync(setting, cancellationToken);

        return new SetCarrierCoolingResponse();
    }
}
