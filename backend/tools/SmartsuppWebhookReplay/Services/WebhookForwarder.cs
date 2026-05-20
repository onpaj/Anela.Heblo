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
    private readonly string? _signingSecret;

    public WebhookForwarder(IHttpClientFactory http, IOptions<ReplayOptions> options, IConfiguration config)
    {
        _http = http;
        _options = options.Value;
        // Prefer Replay:WebhookSecret; fall back to the main app's Smartsupp:WebhookSecret
        // so no extra secrets config is needed when both apps share the same secrets.json.
        _signingSecret = options.Value.WebhookSecret
            ?? config["Smartsupp:WebhookSecret"];
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

        var signature = !string.IsNullOrEmpty(_signingSecret)
            ? ComputeHmac(bodyBytes, _signingSecret)
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
