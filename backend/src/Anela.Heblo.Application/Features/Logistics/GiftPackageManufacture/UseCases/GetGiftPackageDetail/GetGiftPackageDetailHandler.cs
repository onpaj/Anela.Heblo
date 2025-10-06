using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetGiftPackageDetail;

public class GetGiftPackageDetailHandler : IRequestHandler<GetGiftPackageDetailRequest, GetGiftPackageDetailResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;

    public GetGiftPackageDetailHandler(IGiftPackageManufactureService giftPackageService)
    {
        _giftPackageService = giftPackageService;
    }

    public async Task<GetGiftPackageDetailResponse> Handle(GetGiftPackageDetailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var giftPackage = await _giftPackageService.GetGiftPackageDetailAsync(
                request.GiftPackageCode,
                request.SalesCoefficient,
                request.FromDate,
                request.ToDate,
                cancellationToken);

            return new GetGiftPackageDetailResponse
            {
                GiftPackage = giftPackage,
                Success = true
            };
        }
        catch (ArgumentException ex)
        {
            return new GetGiftPackageDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError
            };
        }
        catch (Exception ex)
        {
            return new GetGiftPackageDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError
            };
        }
    }
}