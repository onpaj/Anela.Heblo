using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.CreateGiftPackageManufacture;

public class CreateGiftPackageManufactureResponse : BaseResponse
{
    public GiftPackageManufactureDto Manufacture { get; set; } = null!;
}