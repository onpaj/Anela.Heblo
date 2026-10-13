using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Analytics;

public class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AnalyticsDatabase__ConnectionString")
            ?? "Host=localhost;Database=Heblo_V3;Username=postgres";

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                AnalyticsDbContext.MigrationsHistoryTableName,
                AnalyticsDbContext.Schema))
            .Options;
        return new AnalyticsDbContext(options);
    }
}
