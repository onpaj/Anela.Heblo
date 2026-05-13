using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.API.Webhooks.Smartsupp;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
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
    private readonly ILogger<SmartsuppWebhookController> _logger;

    public SmartsuppWebhookController(
        IMediator mediator,
        IOptions<SmartsuppOptions> options,
        ILogger<SmartsuppWebhookController> logger)
    {
        _mediator = mediator;
        _options = options.Value;
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

        var headerValue = Request.Headers.TryGetValue(SignatureHeader, out var sig) ? sig.ToString() : null;

        if (!SmartsuppHmacVerifier.Verify(rawBody, headerValue, _options.WebhookSecret))
        {
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
            _logger.LogError(ex, "smartsupp webhook malformed JSON from {RemoteIp}", remoteIp);
            return Ok();
        }

        var eventName = TryGetString(envelope, "event") ?? "";
        var accountId = TryGetString(envelope, "account_id") ?? "";
        var appId = TryGetString(envelope, "app_id") ?? "";
        var timestamp = TryGetUtc(envelope, "timestamp") ?? DateTime.UtcNow;
        var data = envelope.TryGetProperty("data", out var d) ? d.Clone() : default;

        if (!string.IsNullOrEmpty(_options.WebhookAppId) &&
            !string.Equals(_options.WebhookAppId, appId, StringComparison.Ordinal))
        {
            _logger.LogWarning("smartsupp webhook app_id mismatch from {RemoteIp}", remoteIp);
            return Unauthorized();
        }

        _logger.LogInformation("smartsupp webhook event={Event} account={AccountId} app={AppId}",
            eventName, accountId, appId);

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "smartsupp webhook downstream processing failed event={Event} app={AppId} bodySize={Size}",
                eventName, appId, rawBody.Length);
        }

        return Ok();
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? TryGetUtc(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
            : null;
}
