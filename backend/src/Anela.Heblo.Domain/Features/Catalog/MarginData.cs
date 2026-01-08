namespace Anela.Heblo.Domain.Features.Catalog;

public class MarginData
{
    public MarginLevel M0 { get; init; } = MarginLevel.Zero;      // Material cost
    public MarginLevel M1_A { get; init; } = MarginLevel.Zero;    // Flat manufacturing cost
    public MarginLevel M1_B { get; init; } = MarginLevel.Zero;    // Direct manufacturing cost
    public MarginLevel M2 { get; init; } = MarginLevel.Zero;      // Storage + Marketing cost

    // Backward compatibility - map old names to new structure
    [Obsolete("Use M1_A (flat manufacturing cost) instead. This property maps to M1_A for backward compatibility.")]
    public MarginLevel M1 => M1_A;
}