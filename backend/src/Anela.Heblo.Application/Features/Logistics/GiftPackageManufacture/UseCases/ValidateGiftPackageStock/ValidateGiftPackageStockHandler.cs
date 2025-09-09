using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.ValidateGiftPackageStock;

public class ValidateGiftPackageStockHandler : IRequestHandler<ValidateGiftPackageStockRequest, ValidateGiftPackageStockResponse>
{
    private readonly IGiftPackageManufactureService _giftPackageService;

    public ValidateGiftPackageStockHandler(IGiftPackageManufactureService giftPackageService)
    {
        _giftPackageService = giftPackageService;
    }

    public async Task<ValidateGiftPackageStockResponse> Handle(ValidateGiftPackageStockRequest request, CancellationToken cancellationToken)
    {
        var validation = await _giftPackageService.ValidateStockAsync(
            request.GiftPackageCode, 
            request.Quantity, 
            cancellationToken);

        return new ValidateGiftPackageStockResponse
        {
            Validation = validation
        };
    }
}