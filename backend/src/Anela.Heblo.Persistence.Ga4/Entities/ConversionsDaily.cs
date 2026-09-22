namespace Anela.Heblo.Persistence.Ga4.Entities;

/// <summary>
/// One row per (date, default channel group) carrying GA4's own e-commerce figures.
///
/// These count <c>purchase</c> events fired on the thank-you page. They are NOT the ERP's order
/// book and will not reconcile with it — see the caveat on <c>v_monthly_conversion</c>.
/// </summary>
public class ConversionsDaily
{
    public DateOnly Date { get; set; }

    public string ChannelGroup { get; set; } = "";

    /// <summary>GA4 metric <c>transactions</c>.</summary>
    public long Transactions { get; set; }

    /// <summary>GA4 metric <c>purchaseRevenue</c>, in the property's reporting currency (CZK).</summary>
    public decimal PurchaseRevenue { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
