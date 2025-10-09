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
        var jobId = await _giftPackageService.EnqueueManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            cancellationToken);

        return new EnqueueGiftPackageManufactureResponse
        {
            JobId = jobId,
            Message = $"Manufacturing of {request.Quantity}x {request.GiftPackageCode} has been queued. Job ID: {jobId}"
        };
    }
}