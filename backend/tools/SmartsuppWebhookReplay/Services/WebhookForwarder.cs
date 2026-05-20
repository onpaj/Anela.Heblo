using System.Diagnostics;
using System.Security.Cryptography;
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
        var sentAt = DateTime.UtcNow;

        var bodyBytes = Encoding.UTF8.GetBytes(entry.RawBody);
        var request = new HttpRequestMessage(HttpMethod.Post, _options.TargetUrl)
        {
            Content = new ByteArrayContent(bodyBytes),
        };
        request.Content.Headers.ContentType = new("application/json");

        var signature = !string.IsNullOrEmpty(_options.WebhookSecret)
            ? ComputeHmac(bodyBytes, _options.WebhookSecret)
            : entry.SignatureHeader;

        if (!string.IsNullOrEmpty(signature))
            request.Headers.TryAddWithoutValidation("X-Smartsupp-Hmac", signature);

        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(request, ct);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(ct);
        return new ForwardResult
        {
            HttpStatus = (int)response.StatusCode,
            ResponseBody = body,
            DurationMs = (int)sw.ElapsedMilliseconds,
            SentAt = sentAt,
        };
    }

    private static string ComputeHmac(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }
}
