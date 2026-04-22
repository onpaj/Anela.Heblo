namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsSettings
{
    public static string ConfigKey => "MetaAds";

    public string AccessToken { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string ApiVersion { get; set; } = "v21.0";
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>Number of days back to sync daily metrics (default 30).</summary>
    public int SyncDaysBack { get; set; } = 30;
}
