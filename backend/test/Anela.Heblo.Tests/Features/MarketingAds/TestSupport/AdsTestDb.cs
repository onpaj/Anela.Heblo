using Anela.Heblo.Persistence.Ads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Diagnostics;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>
/// Named InMemory databases. Tests assert through a SECOND context opened on the same name:
/// asserting through the context the code under test used reads the change tracker, not the store,
/// and passes even with SaveChanges removed.
/// </summary>
internal static class AdsTestDb
{
    public static string NewName() => $"ads-{Guid.NewGuid():N}";

    public static AdsDbContext Open(string name, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AdsDbContext>().UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new AdsDbContext(builder.Options);
    }
}
