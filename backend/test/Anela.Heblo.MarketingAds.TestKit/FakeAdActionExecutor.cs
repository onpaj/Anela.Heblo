using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// In-memory IAdActionExecutor for the core's execution pipeline (C3) and the executor contract
/// self-tests. Holds ad statuses and negative keywords per account; never talks to a platform.
/// Follows spec 12.2: platform-side rejections come back as Failed; only <see cref="FailWith"/>
/// (a simulated transport/auth failure) and a wrong-platform action (a programming error) throw.
/// It does not compare OldValue itself — the core does.
/// </summary>
public sealed class FakeAdActionExecutor : IAdActionExecutor
{
    private readonly Dictionary<AdKey, string> _adStatuses = new();
    private readonly HashSet<TargetKey> _negativeKeywordTargets = new();
    private readonly Dictionary<string, NegativeKeyword> _negativeKeywords = new(StringComparer.Ordinal);
    private readonly List<AdAction> _executedActions = [];
    private readonly List<AdAction> _revertedActions = [];
    private int _nextResourceNumber = 1;
    private string? _nextExecuteRejection;
    private Exception? _failure;

    public FakeAdActionExecutor(AdPlatform platform = AdPlatform.GoogleAds, params AdActionType[] supportedActions)
    {
        Platform = platform;
        SupportedActions = (supportedActions.Length == 0 ? Enum.GetValues<AdActionType>() : supportedActions).ToHashSet();
    }

    public AdPlatform Platform { get; }
    public IReadOnlySet<AdActionType> SupportedActions { get; }
    public IReadOnlyList<AdAction> ExecutedActions => _executedActions;
    public IReadOnlyList<AdAction> RevertedActions => _revertedActions;

    public FakeAdActionExecutor SeedAd(string accountExternalId, string adExternalId, string status = AdActionValues.Enabled)
    {
        _adStatuses[new AdKey(accountExternalId, adExternalId)] = status;
        return this;
    }

    public FakeAdActionExecutor SeedNegativeKeywordTarget(string accountExternalId, AdEntityLevel level, string targetExternalId)
    {
        _negativeKeywordTargets.Add(new TargetKey(accountExternalId, level, targetExternalId));
        return this;
    }

    public FakeAdActionExecutor RejectNextExecute(string error)
    {
        _nextExecuteRejection = error;
        return this;
    }

    public FakeAdActionExecutor FailWith(Exception failure)
    {
        _failure = failure;
        return this;
    }

    public Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        Guard(action, ct);
        var state = action.Type switch
        {
            AdActionType.PauseAd => ReadAd(action),
            AdActionType.AddNegativeKeyword => ReadNegativeKeyword(action),
            _ => new AdTargetState(false, null, null),
        };
        return Task.FromResult(state);
    }

    public Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        Guard(action, ct);
        if (!SupportedActions.Contains(action.Type))
            return Task.FromResult(Failed($"Action {action.Type} is not supported by this executor."));

        if (_nextExecuteRejection is { } rejection)
        {
            _nextExecuteRejection = null;
            return Task.FromResult(Failed(rejection));
        }

        var result = action.Type == AdActionType.PauseAd ? PauseAd(action) : AddNegativeKeyword(action);
        if (result.Outcome == AdExecutionOutcome.Succeeded)
            _executedActions.Add(action);
        return Task.FromResult(result);
    }

    public Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        Guard(action, ct);
        if (!SupportedActions.Contains(action.Type))
            return Task.FromResult(Failed($"Action {action.Type} is not supported by this executor."));
        if (original.Outcome != AdExecutionOutcome.Succeeded)
            return Task.FromResult(Failed("Only a succeeded execution can be reverted."));

        var result = action.Type == AdActionType.PauseAd ? RestoreAd(action, original) : RemoveNegativeKeyword(original);
        if (result.Outcome == AdExecutionOutcome.Succeeded)
            _revertedActions.Add(action);
        return Task.FromResult(result);
    }

    private void Guard(AdAction action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_failure is not null)
            throw _failure;
        if (action.Platform != Platform)
            throw new ArgumentException($"A {action.Platform} action was sent to the {Platform} executor.", nameof(action));
    }

    private AdTargetState ReadAd(AdAction action) =>
        _adStatuses.TryGetValue(AdKeyOf(action), out var status)
            ? new AdTargetState(true, status, JsonSerializer.Serialize(new { adId = action.TargetExternalId, status }))
            : new AdTargetState(false, null, null);

    private AdTargetState ReadNegativeKeyword(AdAction action)
    {
        if (!_negativeKeywordTargets.Contains(TargetKeyOf(action)))
            return new AdTargetState(false, null, null);

        var current = FindNegativeKeyword(action) is null ? AdActionValues.Absent : AdActionValues.Present;
        return new AdTargetState(true, current, null);
    }

    private AdExecutionResult PauseAd(AdAction action)
    {
        var key = AdKeyOf(action);
        if (!_adStatuses.TryGetValue(key, out var before))
            return Failed($"Ad {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");

        _adStatuses[key] = AdActionValues.Paused;
        return Succeeded(before, AdActionValues.Paused, action.TargetExternalId);
    }

    private AdExecutionResult RestoreAd(AdAction action, AdExecutionResult original)
    {
        var key = AdKeyOf(action);
        if (!_adStatuses.TryGetValue(key, out var current))
            return Failed($"Ad {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");

        var restored = original.BeforeValue ?? AdActionValues.Enabled;
        _adStatuses[key] = restored;
        return Succeeded(current, restored, action.TargetExternalId);
    }

    private AdExecutionResult AddNegativeKeyword(AdAction action)
    {
        if (!_negativeKeywordTargets.Contains(TargetKeyOf(action)))
            return Failed($"{action.TargetLevel} {action.TargetExternalId} does not exist in account {action.AccountExternalId}.");
        if (!TryReadKeyword(action, out var text, out var matchType))
            return Failed("The payload must carry non-empty 'text' and 'matchType'.");
        if (FindNegativeKeyword(action) is not null)
            return Failed($"Negative keyword '{text}' ({matchType}) already exists.");

        var resourceId = $"fake-negative-{_nextResourceNumber++}";
        _negativeKeywords[resourceId] = new NegativeKeyword(TargetKeyOf(action), text, matchType);
        return Succeeded(AdActionValues.Absent, AdActionValues.Present, resourceId);
    }

    private AdExecutionResult RemoveNegativeKeyword(AdExecutionResult original)
    {
        if (original.PlatformResourceId is null || !_negativeKeywords.Remove(original.PlatformResourceId))
            return Failed($"Negative keyword {original.PlatformResourceId} does not exist.");

        return Succeeded(AdActionValues.Present, AdActionValues.Absent, original.PlatformResourceId);
    }

    private string? FindNegativeKeyword(AdAction action)
    {
        if (!TryReadKeyword(action, out var text, out var matchType))
            return null;

        var wanted = new NegativeKeyword(TargetKeyOf(action), text, matchType);
        return _negativeKeywords.FirstOrDefault(entry => entry.Value == wanted).Key;
    }

    private static bool TryReadKeyword(AdAction action, out string text, out string matchType)
    {
        action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var rawText);
        action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var rawMatchType);
        text = rawText?.Trim() ?? "";
        matchType = rawMatchType?.Trim() ?? "";
        return text.Length > 0 && matchType.Length > 0;
    }

    private static AdKey AdKeyOf(AdAction action) => new(action.AccountExternalId, action.TargetExternalId);

    private static TargetKey TargetKeyOf(AdAction action) =>
        new(action.AccountExternalId, action.TargetLevel, action.TargetExternalId);

    private static AdExecutionResult Succeeded(string before, string after, string resourceId) =>
        new(AdExecutionOutcome.Succeeded, before, after, resourceId,
            JsonSerializer.Serialize(new { resourceId, before, after }), null);

    private static AdExecutionResult Failed(string error) =>
        new(AdExecutionOutcome.Failed, null, null, null, null, error);

    private sealed record AdKey(string Account, string AdId);

    private sealed record TargetKey(string Account, AdEntityLevel Level, string TargetId);

    private sealed record NegativeKeyword(TargetKey Target, string Text, string MatchType);
}
