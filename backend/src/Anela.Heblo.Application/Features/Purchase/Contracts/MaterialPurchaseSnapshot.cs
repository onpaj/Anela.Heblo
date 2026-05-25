namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public sealed class MaterialPurchaseSnapshot
{
    public required DateTime Date { get; init; }
    public required string SupplierName { get; init; }
    public required decimal Amount { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal TotalPrice { get; init; }
}
