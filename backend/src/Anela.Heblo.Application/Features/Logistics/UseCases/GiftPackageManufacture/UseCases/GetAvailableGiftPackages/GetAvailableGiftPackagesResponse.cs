using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;

public class GetAvailableGiftPackagesResponse : BaseResponse
{
    public List<GiftPackageDto> GiftPackages { get; set; } = new();
}