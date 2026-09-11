namespace Anela.Heblo.Domain.Features.Campaigns;

public class Ad
{
    public Guid Id { get; set; }
    public Guid AdSetId { get; set; }
    public string PlatformAdId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AdAdSet AdSet { get; set; } = null!;
    public ICollection<AdDailyMetric> DailyMetrics { get; set; } = new List<AdDailyMetric>();
}
