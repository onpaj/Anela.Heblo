namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal interface IGoogleAdsAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
