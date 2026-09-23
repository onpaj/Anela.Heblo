using Anela.Heblo.Persistence.ShoptetOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public sealed class ShoptetOrderFactRefresher : IShoptetOrderFactRefresher
{
    private readonly ShoptetOrdersDbContext _dbContext;
    private readonly ILogger<ShoptetOrderFactRefresher> _logger;

    public ShoptetOrderFactRefresher(
        ShoptetOrdersDbContext dbContext,
        ILogger<ShoptetOrderFactRefresher> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        // The materialised view is created by Sql/shoptet_raw_views.sql, not by a migration, so it
        // can legitimately be absent on an environment where only the migration has been applied.
        // to_regclass returns NULL rather than throwing for an unknown relation.
        var exists = await _dbContext.Database
            .SqlQuery<bool>($"SELECT to_regclass('shoptet_raw.order_fact') IS NOT NULL AS \"Value\"")
            .SingleAsync(ct);

        if (!exists)
        {
            _logger.LogWarning(
                "ShoptetOrdersSync.OrderFactMissing — shoptet_raw.order_fact does not exist. "
                + "Run Sql/shoptet_raw_views.sql; the read views will be stale until then.");
            return false;
        }

        // CONCURRENTLY so Metabase keeps reading the previous contents instead of blocking for the
        // length of the refresh. It requires the unique index the views script creates on (code).
        await _dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY shoptet_raw.order_fact", ct);

        _logger.LogInformation("ShoptetOrdersSync.OrderFactRefreshed");
        return true;
    }
}
