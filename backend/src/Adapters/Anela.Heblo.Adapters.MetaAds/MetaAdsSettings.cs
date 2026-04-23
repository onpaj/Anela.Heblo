using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsSettings
{
    public static string ConfigKey => "MetaAds";

    [Required(AllowEmptyStrings = false)]
    public string AccessToken { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    public string AccountId { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    public string ApiVersion { get; set; } = "v21.0";

    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>Maximum number of campaigns to sync per run. Guards against runaway API call bursts.</summary>
    public int MaxCampaignsToSync { get; set; } = 50;

    /// <summary>Number of days back to sync daily metrics (default 30).</summary>
    public int SyncDaysBack { get; set; } = 30;
}
