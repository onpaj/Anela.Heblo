using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>One calendar month of the marketing performance snapshot. Sums only — ratios are derived at read time.</summary>
public class MarketingPerformanceMonth : IEntity<int>
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>CZK issued invoices without a customer VAT ID, by tax date.</summary>
    public int RetailOrderCount { get; set; }
    public decimal RetailRevenueWithVat { get; set; }

    /// <summary>CZK issued invoices with a customer VAT ID (wholesale — same rule as Flexi sales query 37).</summary>
    public int WholesaleOrderCount { get; set; }
    public decimal WholesaleRevenueWithVat { get; set; }

    /// <summary>EUR invoices in the month; excluded from revenue, kept visible.</summary>
    public int SkippedEurInvoiceCount { get; set; }

    /// <summary>True once the month left the recompute window; only an explicit recompute touches it.</summary>
    public bool IsLocked { get; set; }

    public DateTime? RevenueComputedAt { get; set; }
    public DateTime? CostsComputedAt { get; set; }
    public string? LastError { get; set; }

    public List<MarketingPerformanceChannelCost> ChannelCosts { get; set; } = new();

    public YearMonth Key => new(Year, Month);
}
