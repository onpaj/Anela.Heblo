using System.ComponentModel;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

public interface IGiftPackageManufactureService
{
    [DisplayName("GiftPackageManufacture-{0}-{1}x")]
    Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        string userName,
        CancellationToken cancellationToken = default);

    Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
        string giftPackageCode,
        int quantity,
        string userName,
        CancellationToken cancellationToken = default);
}
