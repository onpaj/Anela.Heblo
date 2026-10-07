namespace Anela.Heblo.Adapters.GoogleAds.Api;

internal interface IGoogleAdsAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);

    /// <summary>
    /// Drops <paramref name="accessToken"/> from the cache after Google rejected it (401), so the
    /// next call exchanges the refresh token again. A newer cached token is left alone.
    /// </summary>
    void Invalidate(string accessToken);
}
