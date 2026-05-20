using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.API.Webhooks.Smartsupp;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence.Smartsupp;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/webhooks/smartsupp")]
[AllowAnonymous]
public class SmartsuppWebhookController : ControllerBase
{
    private const string SignatureHeader = "X-Smartsupp-Hmac";
    private const int MaxBodyBytes = 1_048_576;

    private readonly IMediator _mediator;
    private readonly SmartsuppOptions _options;
    private readonly ISmartsuppWebhookMetrics _metrics;
    private readonly ISmartsuppWebhookAuditWriter _audit;
    private readonly ILogger<SmartsuppWebhookController> _logger;

    public SmartsuppWebhookController(
        IMediator mediator,
        IOptions<SmartsuppOptions> options,
        ISmartsuppWebhookMetrics metrics,
        ISmartsuppWebhookAuditWriter audit,
        ILogger<SmartsuppWebhookController> logger)
    {
        _mediator = mediator;
        _options = options.Value;
        _metrics = metrics;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        Request.EnableBuffering();
        Request.Body.Position = 0;

        byte[] rawBody;
        await using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms, cancellationToken);
            rawBody = ms.ToArray();
        }
        Request.Body.Position = 0;

        _metrics.RecordPayloadBytes(rawBody.Length);

        var headerValue = Request.Headers.TryGetValue(SignatureHeader, out var sig) ? sig.ToString() : null;
        var rawBodyText = Encoding.UTF8.GetString(rawBody);
        var headersJson = SerializeHeaders(Request);

        var entry = new SmartsuppWebhookAuditEntry
        {
            ReceivedAt = DateTime.UtcNow,
            RemoteIp = remoteIp,
            SignatureHeader = headerValue,
            HeadersJson = headersJson,
            RawBody = rawBodyText,
            BodySizeBytes = rawBody.Length,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.NotProcessed,
        };

        if (!SmartsuppHmacVerifier.Verify(rawBody, headerValue, _options.WebhookSecret))
        {
            entry.SignatureStatus = headerValue is null
                ? SmartsuppWebhookSignatureStatus.Missing
                : SmartsuppWebhookSignatureStatus.Mismatch;
            await _audit.CreateAsync(entry, cancellationToken);
            _metrics.RecordSignatureFailure(headerValue is null ? "missing" : "mismatch");
            _logger.LogWarning("smartsupp webhook signature mismatch from {RemoteIp}", remoteIp);
            return Unauthorized();
        }

        JsonElement envelope;
        try
        {
            envelope = JsonDocument.Parse(rawBody).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            entry.SignatureStatus = SmartsuppWebhookSignatureStatus.Valid;
            entry.ProcessingStatus = SmartsuppWebhookProcessingStatus.MalformedJson;
            entry.ProcessingError = ex.Message;
            await _audit.CreateAsync(entry, cancellationToken);
            _logger.LogError(ex, "smartsupp webhook malformed JSON from {RemoteIp}", remoteIp);
            return Ok();
        }

        var eventName = TryGetString(envelope, "event") ?? "";
        var accountId = TryGetString(envelope, "account_id") ?? "";
        var appId = TryGetString(envelope, "app_id") ?? "";
        var timestamp = TryGetUtc(envelope, "timestamp") ?? DateTime.UtcNow;
        var data = envelope.TryGetProperty("data", out var d) ? d.Clone() : default;

        entry.EventName = eventName;
        entry.AccountId = accountId;
        entry.AppId = appId;
        entry.EventTimestamp = timestamp;

        if (!string.IsNullOrEmpty(_options.WebhookAppId) &&
            !string.Equals(_options.WebhookAppId, appId, StringComparison.Ordinal))
        {
            entry.SignatureStatus = SmartsuppWebhookSignatureStatus.AppIdMismatch;
            await _audit.CreateAsync(entry, cancellationToken);
            _metrics.RecordSignatureFailure("app_id_mismatch");
            _logger.LogWarning("smartsupp webhook app_id mismatch from {RemoteIp}", remoteIp);
            return Unauthorized();
        }

        entry.SignatureStatus = SmartsuppWebhookSignatureStatus.Valid;
        var auditId = await _audit.CreateAsync(entry, cancellationToken);

        _logger.LogInformation("smartsupp webhook event={Event} account={AccountId} app={AppId} bodySize={BodySize}",
            eventName, accountId, appId, rawBody.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            await _mediator.Send(new ProcessWebhookEventRequest
            {
                EventName = eventName,
                Timestamp = timestamp,
                AccountId = accountId,
                AppId = appId,
                Data = data,
            }, cancellationToken);
            sw.Stop();
            await _audit.UpdateOutcomeAsync(auditId,
                SmartsuppWebhookProcessingStatus.Success,
                error: null,
                durationMs: (int)sw.ElapsedMilliseconds,
                cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _audit.UpdateOutcomeAsync(auditId,
                SmartsuppWebhookProcessingStatus.HandlerException,
                error: ex.ToString(),
                durationMs: (int)sw.ElapsedMilliseconds,
                cancellationToken);
            _logger.LogError(ex,
                "smartsupp webhook downstream processing failed event={Event} app={AppId}",
                eventName, appId);
        }

        return Ok();
    }

    private static string SerializeHeaders(HttpRequest request)
    {
        var dict = request.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value!));
        return JsonSerializer.Serialize(dict);
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? TryGetUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;
}
