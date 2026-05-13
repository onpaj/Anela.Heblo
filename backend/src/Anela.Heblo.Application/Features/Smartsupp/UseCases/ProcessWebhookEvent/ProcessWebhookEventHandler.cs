using System.Diagnostics;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;

public class ProcessWebhookEventHandler : IRequestHandler<ProcessWebhookEventRequest, ProcessWebhookEventResponse>
{
    private readonly IReadOnlyDictionary<string, ISmartsuppWebhookReaction> _reactionsByName;
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppWebhookMetrics _metrics;
    private readonly ILogger<ProcessWebhookEventHandler> _logger;

    public ProcessWebhookEventHandler(
        IEnumerable<ISmartsuppWebhookReaction> reactions,
        ISmartsuppRepository repository,
        ISmartsuppWebhookMetrics metrics,
        ILogger<ProcessWebhookEventHandler> logger)
    {
        _reactionsByName = reactions.ToDictionary(r => r.EventName, StringComparer.Ordinal);
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ProcessWebhookEventResponse> Handle(
        ProcessWebhookEventRequest request,
        CancellationToken cancellationToken)
    {
        var ctx = WebhookEventContext.From(request);
        var sw = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["smartsupp.event"] = ctx.EventName,
            ["smartsupp.account_id"] = ctx.AccountId,
            ["smartsupp.app_id"] = ctx.AppId,
        });

        if (!_reactionsByName.TryGetValue(ctx.EventName, out var reaction))
        {
            var outcome = ClassifyUnhandled(ctx.EventName);
            _metrics.RecordReceived(ctx.EventName, outcome, sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("smartsupp webhook {Outcome} event {Event}", outcome, ctx.EventName);
            return new ProcessWebhookEventResponse { Handled = false, Reason = outcome };
        }

        try
        {
            await reaction.HandleAsync(ctx, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _metrics.RecordReceived(ctx.EventName, "handled", sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("smartsupp webhook handled {Event} in {ElapsedMs}ms",
                ctx.EventName, (int)sw.Elapsed.TotalMilliseconds);
            return new ProcessWebhookEventResponse { Handled = true };
        }
        catch (Exception ex)
        {
            _metrics.RecordReceived(ctx.EventName, "error", sw.Elapsed.TotalMilliseconds);
            _logger.LogError(ex, "smartsupp webhook reaction failed for {Event} ({Reaction})",
                ctx.EventName, reaction.GetType().Name);
            throw;
        }
    }

    private static string ClassifyUnhandled(string eventName)
    {
        if (eventName.StartsWith("visitor.", StringComparison.Ordinal)) return "observed";
        if (eventName.StartsWith("app.", StringComparison.Ordinal)) return "ignored";
        return "unknown";
    }
}
