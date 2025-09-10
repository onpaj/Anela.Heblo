using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetGiftPackageDetail;

public class GetGiftPackageDetailRequest : IRequest<GetGiftPackageDetailResponse>
{
    public string GiftPackageCode { get; set; } = null!;
}