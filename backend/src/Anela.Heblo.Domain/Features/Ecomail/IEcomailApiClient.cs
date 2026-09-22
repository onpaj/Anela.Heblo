namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>Read-only access to the Ecomail v2 API. Nothing here sends, triggers or deletes.</summary>
public interface IEcomailApiClient
{
    Task<IReadOnlyList<EcomailCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lifetime stats. Null when the campaign no longer exists.</summary>
    Task<EcomailStatsDto?> GetCampaignStatsAsync(int campaignId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EcomailPipelineDto>> GetPipelinesAsync(CancellationToken cancellationToken = default);

    /// <summary>Cumulative lifetime stats for an automation. Null when it no longer exists.</summary>
    Task<EcomailStatsDto?> GetPipelineStatsAsync(int pipelineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unique subscribers with <paramref name="eventName"/> inside the window, read from
    /// stats-detail's `total`. This is the only date filter Ecomail actually honours.
    /// Returns 0 when Ecomail answers `{"total":null}`.
    /// </summary>
    Task<int> GetPipelineEventCountAsync(
        int pipelineId, string eventName, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default);
}

public class EcomailCampaignDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? FromEmail { get; set; }
    public string CampaignType { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime? SentAt { get; set; }
    public int? ParentId { get; set; }
    public int Recipients { get; set; }
}

public class EcomailPipelineDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ListId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Shared stats shape: /campaigns/{id}/stats and /pipelines/{id}/stats return the same fields.</summary>
public class EcomailStatsDto
{
    public int Inject { get; set; }
    public int Delivery { get; set; }
    public int Open { get; set; }
    public int TotalOpen { get; set; }
    public int Click { get; set; }
    public int TotalClick { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Spam { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }

    // Automation-only; absent on campaigns and left at 0 there.
    public int Triggered { get; set; }
    public int Ended { get; set; }
    public int Send { get; set; }
}
