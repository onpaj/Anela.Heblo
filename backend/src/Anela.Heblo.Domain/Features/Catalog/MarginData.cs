namespace Anela.Heblo.Domain.Features.Catalog;

public class MarginData
{
    public MarginLevel M0 { get; init; } = MarginLevel.Zero;
    public MarginLevel M1_A { get; init; } = MarginLevel.Zero;  // Average M1_A across months
    public MarginLevel M1_B { get; init; } = MarginLevel.Zero;  // Average M1_B (only months with production)
    public MarginLevel M2 { get; init; } = MarginLevel.Zero;
    public MarginLevel M3 { get; init; } = MarginLevel.Zero;
}