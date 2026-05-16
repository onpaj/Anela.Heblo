using System.Diagnostics;
using System.Net.Sockets;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// DelegatingHandler attached to every Heblo outbound HttpClient. On the happy path it is a
/// near-zero overhead pass-through. On failure it classifies the exception, emits a structured
/// ILogger entry, and tracks the exception via ITelemetryService with PascalCase properties
/// that align with the rest of the Heblo telemetry surface.
///
/// In a background context (Hangfire job, hosted service) IHttpContextAccessor.HttpContext is
/// null. The handler then falls back to the caller's CancellationToken to decide whether a
/// cancellation came from the caller (ClientAborted) or from a per-call timeout (Timeout).
/// </summary>
public sealed class OutboundCallObservabilityHandler : DelegatingHandler
{
    private readonly ILogger<OutboundCallObservabilityHandler> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<OutboundResilienceOptions> _options;

    public OutboundCallObservabilityHandler(
        ILogger<OutboundCallObservabilityHandler> logger,
        ITelemetryService telemetry,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<OutboundResilienceOptions> options)
    {
        _logger = logger;
        _telemetry = telemetry;
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.LoggingEnabled)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var reason = Classify(ex, cancellationToken, out var inboundCancellationRequested);
            LogFailure(ex, request, elapsed, reason, inboundCancellationRequested);
            throw;
        }
    }

    private OutboundCallReason Classify(Exception ex, CancellationToken callerToken, out bool inboundCancellationRequested)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        // In a background context (Hangfire job, hosted service) HttpContext is null;
        // fall back to the caller token as the inbound signal.
        var inboundToken = httpContext is not null ? httpContext.RequestAborted : callerToken;
        inboundCancellationRequested = inboundToken.IsCancellationRequested;

        if (ex is OperationCanceledException)
        {
            return inboundCancellationRequested
                ? OutboundCallReason.ClientAborted
                : OutboundCallReason.Timeout;
        }

        if (ex is SocketException or IOException or HttpRequestException)
        {
            return OutboundCallReason.Network;
        }

        return OutboundCallReason.Unknown;
    }

    private void LogFailure(
        Exception ex,
        HttpRequestMessage request,
        TimeSpan elapsed,
        OutboundCallReason reason,
        bool inboundCancellationRequested)
    {
        var targetHost = request.RequestUri?.Host ?? "unknown";
        var targetPath = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method.Method;
        var operationId = Activity.Current?.RootId ?? string.Empty;
        var elapsedMs = (long)elapsed.TotalMilliseconds;

        var level = reason == OutboundCallReason.ClientAborted ? LogLevel.Warning : LogLevel.Error;

        _logger.Log(
            level,
            ex,
            "Outbound call failed: {HttpMethod} {TargetHost}{TargetPath} after {ElapsedMs}ms (Reason: {Reason})",
            method, targetHost, targetPath, elapsedMs, reason);

        _telemetry.TrackException(ex, new Dictionary<string, string>
        {
            [OutboundCallLogProperties.TargetHost] = targetHost,
            [OutboundCallLogProperties.TargetPath] = targetPath,
            [OutboundCallLogProperties.HttpMethod] = method,
            [OutboundCallLogProperties.ElapsedMs] = elapsedMs.ToString(),
            [OutboundCallLogProperties.Reason] = reason.ToString(),
            [OutboundCallLogProperties.CancellationRequested] = inboundCancellationRequested ? "true" : "false",
            [OutboundCallLogProperties.OperationId] = operationId,
        });
    }
}
