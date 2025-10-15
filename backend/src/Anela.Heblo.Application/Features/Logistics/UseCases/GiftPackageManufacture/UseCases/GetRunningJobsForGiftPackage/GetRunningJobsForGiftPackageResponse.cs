using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;

public class GetRunningJobsForGiftPackageResponse : BaseResponse
{
    public List<GiftPackageManufactureJobStatusDto> RunningJobs { get; set; } = new();
    public bool HasRunningJobs => RunningJobs.Any();
}