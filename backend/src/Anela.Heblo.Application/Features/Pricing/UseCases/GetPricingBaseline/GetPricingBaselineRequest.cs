using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineRequest : IRequest<GetPricingBaselineResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }
}
