using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Anela.Heblo.Tests.Common;

public static class TestDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Suppresses <see cref="CoreEventId.ManyServiceProvidersCreatedWarning"/>, which EF Core raises as an
    /// error once twenty internal service providers exist in a process.
    ///
    /// The pgvector integration tests must pass an <c>NpgsqlDataSource</c> (rather than a connection string)
    /// so the vector type handler is registered, and the data source instance forms part of EF's
    /// service-provider cache key. Because xUnit creates a fresh test-class instance per test method, each
    /// method builds its own data source and therefore its own provider, and whichever classes run past the
    /// twentieth would otherwise fail. The churn is confined to the test run, so the warning is suppressed
    /// here rather than in production configuration.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> AllowManyServiceProviders<TContext>(
        this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
        => builder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
}
