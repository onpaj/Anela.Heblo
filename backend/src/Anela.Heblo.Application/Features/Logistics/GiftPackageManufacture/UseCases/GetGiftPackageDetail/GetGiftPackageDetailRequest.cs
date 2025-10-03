using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetGiftPackageDetail;

public class GetGiftPackageDetailRequest : IRequest<GetGiftPackageDetailResponse>
{
    public string GiftPackageCode { get; set; } = null!;
    public decimal SalesCoefficient { get; set; } = 1.0m;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}