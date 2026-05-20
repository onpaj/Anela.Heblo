using System.Diagnostics;
using System.Text;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Options;
using SmartsuppWebhookReplay.Models;

namespace SmartsuppWebhookReplay.Services;

public sealed class WebhookForwarder
{
    private readonly IHttpClientFactory _http;
    private readonly ReplayOptions _options;

    public WebhookForwarder(IHttpClientFactory http, IOptions<ReplayOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<ForwardResult> ForwardAsync(SmartsuppWebhookAuditEntry entry, CancellationToken ct)
    {
        var client = _http.CreateClient(nameof(WebhookForwarder));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        var request = new HttpRequestMessage(HttpMethod.Post, _options.TargetUrl)
        {
            Content = new StringContent(entry.RawBody, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(entry.SignatureHeader))
            request.Headers.TryAddWithoutValidation("X-Smartsupp-Hmac", entry.SignatureHeader);

        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(request, ct);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(ct);
        return new ForwardResult
        {
            HttpStatus = (int)response.StatusCode,
            ResponseBody = body,
            DurationMs = (int)sw.ElapsedMilliseconds,
            SentAt = DateTime.UtcNow,
        };
    }
}
