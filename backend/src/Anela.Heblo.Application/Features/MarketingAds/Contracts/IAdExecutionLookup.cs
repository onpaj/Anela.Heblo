namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Read port the change sync uses to tell Heblo's own writes from out-of-band ones.
/// C2 registers a no-op (everything is out-of-band); C3 registers the real implementation over
/// its execution log. Returns successfully executed actions whose execution time is in [from, to].
/// </summary>
/// <remarks>
/// The matcher treats a change whose ActorKind is User or PlatformAutomation as out-of-band regardless of
/// what was executed. Platform read sources must therefore classify Heblo's own platform user as
/// <see cref="AdChangeActorKind.Heblo"/>, or Heblo's writes are never recognised.
/// </remarks>
public interface IAdExecutionLookup
{
    Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform,
        string accountExternalId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
