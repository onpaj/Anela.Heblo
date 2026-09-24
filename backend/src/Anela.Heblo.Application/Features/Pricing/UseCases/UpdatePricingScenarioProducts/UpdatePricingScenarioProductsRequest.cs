using Anela.Heblo.Application.Features.Pricing.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.UpdatePricingScenarioProducts;

/// <summary>
/// Partial update of a saved scenario: touches only the named products and, when given, the
/// name/description. The scenario's filter is kept. Removals run before edits, so removing and
/// re-editing a product in one request starts that product from the live baseline.
/// </summary>
public class UpdatePricingScenarioProductsRequest : IRequest<UpdatePricingScenarioProductsResponse>
{
    public Guid ScenarioId { get; set; }

    /// <summary>Field-level edits applied in order; a failing edit rejects the whole request.</summary>
    public List<PricingEditDto> Edits { get; set; } = new();

    /// <summary>Products whose override is dropped, so they ride the live baseline again.</summary>
    public List<string> RemoveProductCodes { get; set; } = new();

    /// <summary>Null keeps the current name.</summary>
    public string? Name { get; set; }

    /// <summary>Null keeps the current description; an empty string clears it.</summary>
    public string? Description { get; set; }
}
