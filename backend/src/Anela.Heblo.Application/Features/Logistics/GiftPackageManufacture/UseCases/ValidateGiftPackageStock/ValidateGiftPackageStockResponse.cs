using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.ValidateGiftPackageStock;

public class ValidateGiftPackageStockResponse : BaseResponse
{
    public GiftPackageStockValidationDto Validation { get; set; } = null!;
}