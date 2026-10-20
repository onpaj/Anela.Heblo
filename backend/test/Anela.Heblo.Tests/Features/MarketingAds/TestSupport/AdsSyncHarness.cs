using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>Builds the sync components by hand over one InMemory AdsDbContext.</summary>
internal sealed partial class AdsSyncHarness
{
    public AdsSyncHarness(AdsSyncOptions? options = null, params IInterceptor[] interceptors)
    {
        DbName = AdsTestDb.NewName();
        Db = AdsTestDb.Open(DbName, interceptors);
        Time = new FakeTimeProvider(AdsTestData.Now);
        Options = Microsoft.Extensions.Options.Options.Create(options ?? new AdsSyncOptions());
    }

    public string DbName { get; }
    public AdsDbContext Db { get; }
    public FakeTimeProvider Time { get; }
    public IOptions<AdsSyncOptions> Options { get; }

    /// <summary>A fresh context over the same store — assert through this one.</summary>
    public AdsDbContext Verify() => AdsTestDb.Open(DbName);
}
