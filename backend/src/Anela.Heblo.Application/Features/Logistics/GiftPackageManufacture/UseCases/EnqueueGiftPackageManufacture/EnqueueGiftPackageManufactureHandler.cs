using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.EnqueueGiftPackageManufacture;

public class EnqueueGiftPackageManufactureHandler : IRequestHandler<EnqueueGiftPackageManufactureRequest, EnqueueGiftPackageManufactureResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;
    private readonly ICurrentUserService _currentUserService;

    public EnqueueGiftPackageManufactureHandler(
        IGiftPackageManufactureService giftPackageService,
        ICurrentUserService currentUserService)
    {
        _giftPackageService = giftPackageService;
        _currentUserService = currentUserService;
    }

    public async Task<EnqueueGiftPackageManufactureResponse> Handle(EnqueueGiftPackageManufactureRequest request, CancellationToken cancellationToken)
    {
        // Get current user name if not provided in request
        var userName = string.IsNullOrEmpty(request.RequestedByUserName) 
            ? _currentUserService.GetCurrentUser().Name ?? "System"
            : request.RequestedByUserName;

        var jobId = await _giftPackageService.EnqueueManufactureAsync(
            request.GiftPackageCode,
            request.Quantity,
            request.AllowStockOverride,
            userName,
            cancellationToken);

        return new EnqueueGiftPackageManufactureResponse
        {
            JobId = jobId,
            Message = $"Manufacturing of {request.Quantity}x {request.GiftPackageCode} has been queued. Job ID: {jobId}"
        };
    }
}