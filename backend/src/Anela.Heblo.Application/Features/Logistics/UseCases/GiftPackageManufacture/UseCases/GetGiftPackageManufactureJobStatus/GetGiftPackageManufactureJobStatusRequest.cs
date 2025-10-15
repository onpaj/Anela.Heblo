using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.UseCases.GetGiftPackageManufactureJobStatus;

public class GetGiftPackageManufactureJobStatusRequest : IRequest<GetGiftPackageManufactureJobStatusResponse>
{
    public string JobId { get; set; } = null!;
}