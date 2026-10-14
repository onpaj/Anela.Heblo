using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>
/// Google Ads executor for the v1 allowlist. Does not compare old values (the core does). Platform
/// rejections become Failed results; only transport/auth failures throw.
/// </summary>
internal sealed class GoogleAdsActionExecutor : IAdActionExecutor
{
    private static readonly IReadOnlySet<AdActionType> Supported =
        new HashSet<AdActionType> { AdActionType.AddNegativeKeyword, AdActionType.PauseAd };

    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsActionExecutor> _logger;
    private readonly GoogleAdsPauseAdHandler _pauseAd;

    public GoogleAdsActionExecutor(
        IGoogleAdsApiClient api, IOptionsMonitor<GoogleAdsSettings> settings, ILogger<GoogleAdsActionExecutor> logger)
    {
        _settings = settings;
        _logger = logger;
        _pauseAd = new GoogleAdsPauseAdHandler(api);
    }

    public AdPlatform Platform => AdPlatform.GoogleAds;

    public IReadOnlySet<AdActionType> SupportedActions => Supported;

    public async Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct)
    {
        var customerId = CustomerId();
        if (GoogleAdsActionGuard.Validate(action, customerId) is not null)
            return GoogleAdsExecutionResults.Missing;

        return action.Type switch
        {
            AdActionType.PauseAd => await _pauseAd.ReadCurrentAsync(customerId, action, ct),
            _ => GoogleAdsExecutionResults.Missing,
        };
    }

    public async Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct)
    {
        var customerId = CustomerId();
        var validateOnly = _settings.CurrentValue.ValidateOnly;
        var result = GoogleAdsActionGuard.Validate(action, customerId) is { } invalid
            ? GoogleAdsExecutionResults.Failed(invalid)
            : action.Type switch
            {
                AdActionType.PauseAd => await _pauseAd.ExecuteAsync(customerId, action, validateOnly, ct),
                _ => Unsupported(action),
            };
        Log("Execute", action, result, validateOnly);
        return result;
    }

    public async Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct)
    {
        var customerId = CustomerId();
        var validateOnly = _settings.CurrentValue.ValidateOnly;
        var result = GoogleAdsActionGuard.Validate(action, customerId) is { } invalid
            ? GoogleAdsExecutionResults.Failed(invalid)
            : action.Type switch
            {
                AdActionType.PauseAd => await _pauseAd.RevertAsync(customerId, action, original, validateOnly, ct),
                _ => Unsupported(action),
            };
        Log("Revert", action, result, validateOnly);
        return result;
    }

    private static AdExecutionResult Unsupported(AdAction action) =>
        GoogleAdsExecutionResults.Failed($"Action type {action.Type} is not supported for Google Ads.");

    private string CustomerId() => GoogleAdsIds.NormalizeCustomerId(_settings.CurrentValue.CustomerId);

    private void Log(string operation, AdAction action, AdExecutionResult result, bool validateOnly) =>
        _logger.LogInformation(
            "GoogleAds: {Operation} {ActionType} on {Level} {Target} -> {Outcome} (validateOnly={ValidateOnly}) {Error}",
            operation, action.Type, action.TargetLevel, action.TargetExternalId, result.Outcome, validateOnly, result.Error);
}
