using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdStreamRunner Runner => new(Db, State, Time, NullLogger<AdStreamRunner>.Instance);
    public AdEntitySyncStep EntityStep => new(Entities, Writer, State, Runner, Time);

    public AdsDailySyncService Daily(params IAdPlatformReadSource[] sources) =>
        new(sources, Accounts, EntityStep, Facts, SearchTerms, Runner, Time, Options, NullLogger<AdsDailySyncService>.Instance);
}
