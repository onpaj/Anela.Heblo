using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingFilterDto
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }
}
