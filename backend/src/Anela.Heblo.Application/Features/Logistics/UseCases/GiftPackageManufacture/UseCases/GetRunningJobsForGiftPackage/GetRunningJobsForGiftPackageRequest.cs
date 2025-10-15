using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetRunningJobsForGiftPackage;

public class GetRunningJobsForGiftPackageRequest : IRequest<GetRunningJobsForGiftPackageResponse>
{
    public string GiftPackageCode { get; set; } = null!;
}