using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Analytics;

public class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AnalyticsDatabase__ConnectionString")
            ?? "Host=localhost;Database=anela_analytics;Username=postgres";

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AnalyticsDbContext(options);
    }
}
