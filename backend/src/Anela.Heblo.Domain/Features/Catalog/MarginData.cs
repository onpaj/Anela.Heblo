namespace Anela.Heblo.Domain.Features.Catalog;

/// <summary>
/// The margin cascade. Each level adds one cost layer to the one above it, so CostTotal grows
/// M0 -> M1 -> M2 -> M3 and M3 is the all-costs-in view of the product.
/// </summary>
public class MarginData
{
    public MarginLevel M0 { get; init; } = MarginLevel.Zero;      // Material cost
    public MarginLevel M1 { get; init; } = MarginLevel.Zero;      // + flat manufacturing cost (VYROBA)
    public MarginLevel M2 { get; init; } = MarginLevel.Zero;      // + storage and marketing cost (SKLAD, MARKETING)
    public MarginLevel M3 { get; init; } = MarginLevel.Zero;      // + overhead (everything else on 51/52)
}
