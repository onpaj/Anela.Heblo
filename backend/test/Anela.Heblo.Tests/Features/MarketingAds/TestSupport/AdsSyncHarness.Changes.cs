using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

internal sealed partial class AdsSyncHarness
{
    /// <summary>Replace to simulate C3's execution log.</summary>
    public IAdExecutionLookup Lookup { get; set; } = new NoOpAdExecutionLookup();

    public AdChangeEventWriter Writer => new(Db, Lookup, new AdChangeOriginMatcher(), Time, Options);
}
