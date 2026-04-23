using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

/// <summary>
/// DelegatingHandler that attaches the Meta access token as a Bearer Authorization header on every request.
/// </summary>
public class MetaTokenRefreshHandler : DelegatingHandler
{
    private readonly MetaAdsSettings _settings;

    public MetaTokenRefreshHandler(IOptions<MetaAdsSettings> options)
    {
        _settings = options.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

        return base.SendAsync(request, cancellationToken);
    }
}
