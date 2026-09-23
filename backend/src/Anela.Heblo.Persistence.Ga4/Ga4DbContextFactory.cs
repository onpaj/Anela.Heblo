using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Ga4;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>. The ga4_agg schema lives inside the main
/// Heblo database, so this reads the same connection string the app does.
/// </summary>
public class Ga4DbContextFactory : IDesignTimeDbContextFactory<Ga4DbContext>
{
    public Ga4DbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("Ga4Database__ConnectionString")
            ?? "Host=localhost;Database=heblo;Username=postgres";

        var options = new DbContextOptionsBuilder<Ga4DbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Ga4DbContext.SchemaName))
            .Options;
        return new Ga4DbContext(options);
    }
}
