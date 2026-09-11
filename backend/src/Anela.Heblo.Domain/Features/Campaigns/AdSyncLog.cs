namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdSyncLog
{
    public Guid Id { get; set; }
    public AdPlatform Platform { get; set; }
    public AdSyncStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CampaignsSynced { get; set; }
    public int AdSetsSynced { get; set; }
    public int AdsSynced { get; set; }
    public int MetricRowsSynced { get; set; }
    public string? ErrorMessage { get; set; }

    public void Complete(int campaigns, int adSets, int ads, int metricRows)
    {
        Status = AdSyncStatus.Success;
        CompletedAt = DateTime.UtcNow;
        CampaignsSynced = campaigns;
        AdSetsSynced = adSets;
        AdsSynced = ads;
        MetricRowsSynced = metricRows;
    }

    public void Fail(string errorMessage)
    {
        Status = AdSyncStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }
}

public enum AdSyncStatus
{
    Running = 0,
    Success = 1,
    Failed = 2
}
