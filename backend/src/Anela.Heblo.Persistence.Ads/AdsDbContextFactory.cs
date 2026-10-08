using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Ads;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. The runtime connection string comes from
/// AdsDatabase:ConnectionString; this fallback only has to be parseable.
/// </summary>
public class AdsDbContextFactory : IDesignTimeDbContextFactory<AdsDbContext>
{
    public AdsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AdsDatabase__ConnectionString")
            ?? "Host=localhost;Database=Heblo_V3;Username=postgres";

        var options = new DbContextOptionsBuilder<AdsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName))
            .Options;
        return new AdsDbContext(options);
    }
}
