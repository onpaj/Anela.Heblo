using MediatR;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

public class RecalculatePurchasePriceRequest : IRequest<RecalculatePurchasePriceResponse>
{
    /// <summary>
    /// Optional specific product code to recalculate. If null, RecalculateAll must be true.
    /// </summary>
    public string? ProductCode { get; set; }

    /// <summary>
    /// Whether to recalculate all products that have BoM (Bill of Materials). 
    /// If true, ProductCode is ignored.
    /// </summary>
    public bool RecalculateAll { get; set; }

    /// <summary>
    /// Whether to force reload of catalog data before recalculation.
    /// </summary>
    public bool ForceReload { get; set; } = false;
}