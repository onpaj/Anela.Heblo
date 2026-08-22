namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdDailyMetric
{
    public Guid Id { get; set; }
    public Guid AdId { get; set; }
    public DateTime Date { get; set; }

    // Core metrics
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Spend { get; set; }
    public decimal Revenue { get; set; }
    public long Conversions { get; set; }

    // Computed metrics
    public decimal Ctr => Impressions > 0 ? (decimal)Clicks / Impressions * 100m : 0m;
    public decimal Cpc => Clicks > 0 ? Spend / Clicks : 0m;
    public decimal Roas => Spend > 0 ? Revenue / Spend : 0m;

    public DateTime CreatedAt { get; set; }

    public Ad Ad { get; set; } = null!;
}
