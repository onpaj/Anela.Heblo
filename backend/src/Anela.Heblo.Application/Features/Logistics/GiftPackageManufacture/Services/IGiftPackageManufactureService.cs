using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;

public interface IGiftPackageManufactureService
{
    Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(CancellationToken cancellationToken = default);
    
    Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, CancellationToken cancellationToken = default);
    
    Task<GiftPackageStockValidationDto> ValidateStockAsync(string giftPackageCode, int quantity, CancellationToken cancellationToken = default);
    
    Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode, 
        int quantity, 
        bool allowStockOverride, 
        Guid userId, 
        CancellationToken cancellationToken = default);
}