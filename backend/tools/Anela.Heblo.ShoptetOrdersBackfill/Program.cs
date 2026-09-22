using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Persistence.ShoptetOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Operator entry point for the shoptet_raw sync — the same orchestrator the nightly Hangfire job
// runs, so this is "run the sync now" rather than a second implementation of it. While the history
// backfill is unfinished it spends the whole run there; once it completes, each run is the
// incremental catch-up.
//
// The backfill is ~97k Shoptet detail calls at ~4 req/s — several hours of wall clock — and the
// production Postgres is a shared 1-vCore burstable instance, so it is run deliberately and
// off-hours rather than left to the nightly job to chew through it a few hours at a time.
//
//   dotnet run --project backend/tools/Anela.Heblo.ShoptetOrdersBackfill
//
// Configuration comes from the Anela.Heblo.API user-secrets store plus environment variables:
//   ShoptetOrdersSync__ConnectionString   target database (Heblo_V3 / Heblo_TST)
//   Shoptet__ApiToken                     Shoptet premium private API token
//   ShoptetOrdersSync__BackfillMaxMinutesPerRun, __RequestsPerSecond, __BackfillFrom  (optional)
//
// Re-running is safe: every phase is keyed on order code and resumes from the persisted cursor.

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration[$"{ShoptetOrdersSyncOptions.ConfigurationKey}:ConnectionString"];
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        $"{ShoptetOrdersSyncOptions.ConfigurationKey}:ConnectionString is not configured.");

var apiToken = builder.Configuration[$"{ShoptetApiSettings.ConfigurationKey}:ApiToken"];
if (string.IsNullOrWhiteSpace(apiToken))
    throw new InvalidOperationException($"{ShoptetApiSettings.ConfigurationKey}:ApiToken is not configured.");

builder.Services.AddOptions<ShoptetApiSettings>()
    .Bind(builder.Configuration.GetSection(ShoptetApiSettings.ConfigurationKey));

// The resilience plumbing AddShoptetOrdersPersistenceServices expects from the API host.
builder.Services.AddSingleton<DbResilienceMetrics>();
builder.Services.AddSingleton<NpgsqlConnectionInterceptor>();

builder.Services.AddShoptetOrdersAnalytics(builder.Configuration);

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ShoptetOrdersBackfill");
using var scope = host.Services.CreateScope();

var options = scope.ServiceProvider.GetRequiredService<IOptions<ShoptetOrdersSyncOptions>>().Value;
logger.LogInformation(
    "Sync starting. BackfillFrom={From} WindowDays={Window} RequestsPerSecond={Rps} BudgetMinutes={Budget}",
    options.BackfillFrom, options.BackfillWindowDays, options.RequestsPerSecond, options.BackfillMaxMinutesPerRun);

var sync = scope.ServiceProvider.GetRequiredService<IShoptetOrdersSyncService>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogWarning("Cancellation requested — the cursor is already persisted, re-run to resume.");
    cts.Cancel();
};

var report = await sync.SyncAsync(cts.Token);

var db = scope.ServiceProvider.GetRequiredService<ShoptetOrdersDbContext>();
var orderCount = await db.Orders.CountAsync(CancellationToken.None);
var itemCount = await db.OrderItems.CountAsync(CancellationToken.None);

logger.LogInformation(
    "Sync finished. Fetched={Fetched} Upserted={Upserted} Success={Success} BackfillCompleted={Complete} "
    + "OrdersInDb={Orders} ItemsInDb={Items}",
    report.TotalFetched, report.TotalUpserted, report.IsFullSuccess, report.BackfillCompleted,
    orderCount, itemCount);

return report.IsFullSuccess ? 0 : 1;

// Needed by AddUserSecrets<Program>() in a top-level-statements program.
public partial class Program;
