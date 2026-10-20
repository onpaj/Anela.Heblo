using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    public AdSyncStateStore State => new(Db, Time);
    public AdAccountUpserter Accounts => new(Db, Time, Options, NullLogger<AdAccountUpserter>.Instance);
    public AdEntityUpserter Entities => new(Db, Time, Options);
}
