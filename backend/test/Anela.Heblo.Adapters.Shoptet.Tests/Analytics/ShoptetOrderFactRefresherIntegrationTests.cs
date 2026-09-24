using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Persistence.ShoptetOrders;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

/// <summary>
/// Runs the real migration and the real views script against Postgres, because what can go wrong
/// here — a materialised view that cannot be refreshed CONCURRENTLY, a script that fails on re-run —
/// is invisible to the InMemory provider.
/// </summary>
[Trait("Category", "Integration")]
public class ShoptetOrderFactRefresherIntegrationTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private static readonly DateTimeOffset SyncedAt = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly string ViewsScriptPath =
        Path.Combine(AppContext.BaseDirectory, "Analytics", "Sql", "shoptet_raw_views.sql");

    static ShoptetOrderFactRefresherIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ShoptetOrdersDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<ShoptetOrdersDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ShoptetOrdersDbContext.SchemaName))
            .Options;

        _dbContext = new ShoptetOrdersDbContext(options);
        await _dbContext.Database.MigrateAsync();
        await _dbContext.Database.ExecuteSqlRawAsync("CREATE ROLE metabase_ro");
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ShoptetOrderFactRefresher CreateRefresher() =>
        new(_dbContext, NullLogger<ShoptetOrderFactRefresher>.Instance);

    private async Task RunViewsScriptAsync() =>
        await _dbContext.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(ViewsScriptPath));

    [Fact]
    public async Task RefreshAsync_returns_false_when_the_views_script_has_not_been_run()
    {
        // Act
        var refreshed = await CreateRefresher().RefreshAsync();

        // Assert
        refreshed.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_picks_up_orders_ingested_after_the_views_script_ran()
    {
        // Arrange — run the script twice: operators re-run it on every change, and the second run
        // takes the drop-the-existing-materialised-view branch.
        await RunViewsScriptAsync();
        await RunViewsScriptAsync();

        var json = ShoptetOrderTestData.SimpleOrderJson;
        var order = ShoptetOrderMapper.Map(ShoptetOrderTestData.Parse(json), json, Prague, SyncedAt);
        await new ShoptetOrderStore(_dbContext).UpsertAsync([order]);

        // Act
        var refreshed = await CreateRefresher().RefreshAsync();

        // Assert
        refreshed.Should().BeTrue();
        var codes = await _dbContext.Database
            .SqlQuery<string>($"SELECT code AS \"Value\" FROM shoptet_raw.order_fact")
            .ToListAsync();
        codes.Should().ContainSingle().Which.Should().Be(order.Code);
    }
}
