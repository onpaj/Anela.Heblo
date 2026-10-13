namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// One ad account on one platform. <see cref="Platform"/> holds the AdPlatform enum name
/// ("GoogleAds", "MetaAds", "Sklik"). <see cref="IsManaged"/> is true only for Anela's own accounts;
/// the limits engine (C3) refuses actions on unmanaged ones. Unique (platform, external_id).
/// </summary>
public class AdAccount
{
    public long Id { get; set; }
    public string Platform { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public bool IsManaged { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
