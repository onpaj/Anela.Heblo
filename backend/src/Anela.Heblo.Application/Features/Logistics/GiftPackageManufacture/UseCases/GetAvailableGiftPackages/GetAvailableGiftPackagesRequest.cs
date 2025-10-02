using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.UseCases.GetAvailableGiftPackages;

public class GetAvailableGiftPackagesRequest : IRequest<GetAvailableGiftPackagesResponse>
{
    public decimal SalesCoefficient { get; set; } = 1.0m;
}