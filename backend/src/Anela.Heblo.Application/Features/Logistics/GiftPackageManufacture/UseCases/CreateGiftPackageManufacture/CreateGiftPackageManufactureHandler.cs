using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureHandler : IRequestHandler<CreateGiftPackageManufactureRequest, CreateGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;

    public CreateGiftPackageManufactureHandler(IGiftPackageManufactureService giftPackageService, ICurrentUserService currentUserService)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
    }

    public async Task<CreateGiftPackageManufactureResponse> Handle(CreateGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        var manufacture = await _giftPackageService.CreateManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            _currentUserService.GetCurrentUser().Name ?? "System",
            cancellationToken);

        return new CreateGiftPackageManufactureResponse
        {
            Manufacture = manufacture
        };
    }
}