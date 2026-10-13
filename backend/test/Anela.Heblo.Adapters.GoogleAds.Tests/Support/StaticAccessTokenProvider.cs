using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed class StaticAccessTokenProvider : IGoogleAdsAccessTokenProvider
{
    public const string Token = "test-access-token";

    public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult(Token);

    public List<string> Invalidated { get; } = new();

    public void Invalidate(string accessToken) => Invalidated.Add(accessToken);
}
