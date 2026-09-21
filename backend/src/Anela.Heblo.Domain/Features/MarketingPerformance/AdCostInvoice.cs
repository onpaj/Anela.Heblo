namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>A received invoice relevant for ad spend, already filtered to the configured suppliers.</summary>
public class AdCostInvoice
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public string SupplierVatId { get; init; } = string.Empty;
    public DateTime AccountingDate { get; init; }
    public decimal AmountWithoutVat { get; init; }
    public bool IsCancelled { get; init; }
}
