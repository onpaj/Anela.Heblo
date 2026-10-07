namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Write side of one ad platform (spec 4.2 / 12.2). Executors do NOT compare old values — the core
/// does (spec 6.2). ExecuteAsync returns Failed on a platform-side rejection and throws only for
/// transport/auth failures.
/// </summary>
public interface IAdActionExecutor
{
    AdPlatform Platform { get; }
    IReadOnlySet<AdActionType> SupportedActions { get; }
    Task<AdTargetState> ReadCurrentAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> ExecuteAsync(AdAction action, CancellationToken ct);
    Task<AdExecutionResult> RevertAsync(AdAction action, AdExecutionResult original, CancellationToken ct);
}
