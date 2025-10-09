using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageDetail;

public class GetGiftPackageDetailResponse : BaseResponse
{
    public GiftPackageDto? GiftPackage { get; set; }
}