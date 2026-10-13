using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>Until C3 ships the proposal layer nothing is executed by Heblo, so every change is out-of-band.</summary>
public sealed class NoOpAdExecutionLookup : IAdExecutionLookup
{
    public Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
        AdPlatform platform, string accountExternalId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdExecutedAction>>(Array.Empty<AdExecutedAction>());
}
