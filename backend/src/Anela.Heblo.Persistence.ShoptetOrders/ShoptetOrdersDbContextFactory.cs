using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.ShoptetOrders;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add`. The runtime connection string comes
/// from ShoptetOrdersSync:ConnectionString; this fallback only has to be parseable.
/// </summary>
public class ShoptetOrdersDbContextFactory : IDesignTimeDbContextFactory<ShoptetOrdersDbContext>
{
    public ShoptetOrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ShoptetOrdersSync__ConnectionString")
            ?? "Host=localhost;Database=Heblo_V3;Username=postgres";

        var options = new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ShoptetOrdersDbContext.SchemaName))
            .Options;
        return new ShoptetOrdersDbContext(options);
    }
}
