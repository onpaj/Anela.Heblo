using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingRequest : IRequest<RecalculatePricingResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }

    /// <summary>Sparse: only products the user has touched. Never carries baselines.</summary>
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    /// <summary>The gesture just made. Null means "replay the overrides as given".</summary>
    public PricingEditDto? Edit { get; set; }
}
