using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;

public class EnqueueGiftPackageManufactureHandler : IRequestHandler<EnqueueGiftPackageManufactureRequest, EnqueueGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;

    public EnqueueGiftPackageManufactureHandler(IGiftPackageManufactureService giftPackageService)
    {
        _giftPackageService = giftPackageService;
    }

    public async Task<EnqueueGiftPackageManufactureResponse> Handle(EnqueueGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        var result = await _giftPackageService.CreateManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            cancellationToken);

        return new EnqueueGiftPackageManufactureResponse
        {
            JobId = result.Id.ToString(),
            Message = $"Manufacturing of {request.Quantity}x {request.GiftPackageCode} created. Log ID: {result.Id}. Stock operations will be processed asynchronously."
        };
    }
}