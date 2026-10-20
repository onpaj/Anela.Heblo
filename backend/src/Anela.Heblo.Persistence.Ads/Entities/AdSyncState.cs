namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>Watermark per (platform, account, stream). <see cref="Stream"/> is an <see cref="AdSyncStreams"/> value.</summary>
public class AdSyncState
{
    public string Platform { get; set; } = "";
    public string AccountExternalId { get; set; } = "";
    public string Stream { get; set; } = "";
    public DateTimeOffset? Watermark { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
