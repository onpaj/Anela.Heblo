using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Cups;

internal class CupsAuthHandler : DelegatingHandler
{
    private readonly IOptions<CupsOptions> _options;

    public CupsAuthHandler(IOptions<CupsOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return base.SendAsync(request, cancellationToken);
    }
}
