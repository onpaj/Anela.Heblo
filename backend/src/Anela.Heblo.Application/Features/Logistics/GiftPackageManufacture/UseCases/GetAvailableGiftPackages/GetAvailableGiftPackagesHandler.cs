using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;

public class GetAvailableGiftPackagesHandler : IRequestHandler<GetAvailableGiftPackagesRequest, GetAvailableGiftPackagesResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;

    public GetAvailableGiftPackagesHandler(IGiftPackageManufactureService giftPackageService)
    {
        _giftPackageService = giftPackageService;
    }

    public async Task<GetAvailableGiftPackagesResponse> Handle(GetAvailableGiftPackagesRequest request, CancellationToken cancellationToken)
    {
        var giftPackages = await _giftPackageService.GetAvailableGiftPackagesAsync(request.SalesCoefficient, cancellationToken);

        return new GetAvailableGiftPackagesResponse
        {
            GiftPackages = giftPackages
        };
    }
}