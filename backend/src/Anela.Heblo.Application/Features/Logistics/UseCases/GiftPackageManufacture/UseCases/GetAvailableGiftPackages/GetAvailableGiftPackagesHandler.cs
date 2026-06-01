using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;

public class GetAvailableGiftPackagesHandler : IRequestHandler<GetAvailableGiftPackagesRequest, GetAvailableGiftPackagesResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;

    public GetAvailableGiftPackagesHandler(IGiftPackageManufactureService giftPackageService)
    {
        _giftPackageService = giftPackageService;
    }

    public async Task<GetAvailableGiftPackagesResponse> Handle(GetAvailableGiftPackagesRequest request, CancellationToken cancellationToken)
    {
        var giftPackages = await _giftPackageService.GetAvailableGiftPackagesAsync(
            request.SalesCoefficient,
            request.FromDate,
            request.ToDate,
            cancellationToken);

        return new GetAvailableGiftPackagesResponse
        {
            GiftPackages = giftPackages
        };
    }
}