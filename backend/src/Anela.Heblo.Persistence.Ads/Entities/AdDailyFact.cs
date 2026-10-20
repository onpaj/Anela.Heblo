namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Metrics for one entity on one day, PK (entity_id, date). Stored at every level the platform
/// reports — queries must aggregate a single level. Cost is net of VAT, in account currency.
/// </summary>
public class AdDailyFact
{
    public long EntityId { get; set; }
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset SyncedAt { get; set; }
}
