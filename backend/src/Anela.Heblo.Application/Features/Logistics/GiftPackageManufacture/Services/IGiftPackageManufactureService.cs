using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;

public interface IGiftPackageManufactureService
{
    Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, CancellationToken cancellationToken = default);

    Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, CancellationToken cancellationToken = default);

    Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        CancellationToken cancellationToken = default);

    Task<string> EnqueueManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride = false,
        CancellationToken cancellationToken = default);
}