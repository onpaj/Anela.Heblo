using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    /// <summary>Replace to simulate C3's execution log.</summary>
    public IAdExecutionLookup Lookup { get; set; } = new EmptyExecutionLookup();

    private sealed class EmptyExecutionLookup : IAdExecutionLookup
    {
        public Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(
            AdPlatform platform, string accountExternalId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdExecutedAction>>(Array.Empty<AdExecutedAction>());
    }

    public AdChangeEventWriter Writer => new(Db, Lookup, new AdChangeOriginMatcher(), Time, Options);
}
