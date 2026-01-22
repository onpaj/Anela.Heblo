using System.ComponentModel;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

public interface IGiftPackageManufactureService
{
    Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

    Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

    [DisplayName("GiftPackageManufacture-{0}-{1}x")]
    Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        CancellationToken cancellationToken = default);

    Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
        string giftPackageCode,
        int quantity,
        CancellationToken cancellationToken = default);
}