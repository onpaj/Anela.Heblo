using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;

public class SavePricingScenarioRequest : IRequest<SavePricingScenarioResponse>
{
    /// <summary>Null means create a new scenario; otherwise update the scenario with this id.</summary>
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }

    public List<PricingOverrideDto> Overrides { get; set; } = new();
}
