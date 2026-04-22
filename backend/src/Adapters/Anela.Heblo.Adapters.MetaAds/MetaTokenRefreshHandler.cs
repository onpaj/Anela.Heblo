using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

/// <summary>
/// DelegatingHandler that appends the Meta access_token query parameter to every request.
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
        if (request.RequestUri is not null)
        {
            var uriBuilder = new UriBuilder(request.RequestUri);
            var query = uriBuilder.Query;
            var separator = string.IsNullOrEmpty(query) ? "?" : "&";
            uriBuilder.Query = (query.TrimStart('?') + separator + "access_token=" + Uri.EscapeDataString(_settings.AccessToken)).TrimStart('&');
            request.RequestUri = uriBuilder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
