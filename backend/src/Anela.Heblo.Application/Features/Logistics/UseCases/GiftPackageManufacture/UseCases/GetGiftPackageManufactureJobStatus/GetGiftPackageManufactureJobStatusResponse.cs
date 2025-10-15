using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;

public class GetGiftPackageManufactureJobStatusResponse : BaseResponse
{
    public GiftPackageManufactureJobStatusDto JobStatus { get; set; } = null!;
}