using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureHandler : IRequestHandler<CreateGiftPackageManufactureRequest, CreateGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;

    public CreateGiftPackageManufactureHandler(
        IGiftPackageManufactureService giftPackageService,
        ICurrentUserService currentUserService)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
    }

    public async Task<CreateGiftPackageManufactureResponse> Handle(CreateGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var manufacture = await _giftPackageService.CreateManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            user.Name ?? "System",
            cancellationToken);

        return new CreateGiftPackageManufactureResponse
        {
            Manufacture = manufacture
        };
    }
}
