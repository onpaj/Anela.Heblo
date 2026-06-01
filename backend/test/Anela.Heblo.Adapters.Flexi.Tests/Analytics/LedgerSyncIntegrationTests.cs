using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

[Trait("Category", "Integration")]
public class LedgerSyncIntegrationTests : IAsyncLifetime
{
    static LedgerSyncIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private AnalyticsDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AnalyticsDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private LedgerSyncService CreateService(ILedgerClient client)
    {
        var repo = new SyncWatermarkRepository(_dbContext);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2024-01-01",
            RequestTimeoutSeconds = 30
        });
        return new LedgerSyncService(
            client, repo, _dbContext, opts,
            NullLogger<LedgerSyncService>.Instance);
    }

    private static LedgerItemFlexiDto MakeLedgerDto(int id, DateTime accountingDate, double amount = 100.0) =>
        new()
        {
            Id = id,
            AccountingDate = accountingDate,
            LastUpdate = accountingDate.AddHours(1),
            AmountLocal = amount,
            ParSymbol = $"CODE{id}",
            DebitAccountShowAs = "501000",
            CreditAccountShowAs = "221000",
            CurrencyRef = "code:CZK",
            Description = "Integration test entry",
        };

    [Fact]
    public async Task SyncAsync_WithRealDatabase_InsertsNewRows()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                MakeLedgerDto(101, new DateTime(2024, 6, 1)),
                MakeLedgerDto(102, new DateTime(2024, 6, 2))
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);

        var entries = await _dbContext.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.FlexiId == 101);
        entries.Should().Contain(e => e.FlexiId == 102);

        var state = await _dbContext.SyncStates.FindAsync("ledger_entry");
        state.Should().NotBeNull();
        state!.Watermark.Should().NotBeNull();
        state.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SyncAsync_WithRealDatabase_UpdatesExistingRows()
    {
        // Arrange — pre-seed 1 row with amount 100
        var existing = new LedgerEntry
        {
            FlexiId = 201,
            Code = "CODE201",
            EntryDate = new DateOnly(2024, 7, 1),
            AccountDebit = "501000",
            AccountCredit = "221000",
            Amount = 100m,
            Currency = "code:CZK",
            Description = "Seeded entry",
            RawPayload = "{}",
            SyncedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.LedgerEntries.Add(existing);
        await _dbContext.SaveChangesAsync();

        var client = new Mock<ILedgerClient>();
        client.SetupSequence(c => c.GetChangedSinceAsync(
                It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                MakeLedgerDto(201, new DateTime(2024, 7, 1), amount: 999.0)
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object);

        // Act
        var result = await svc.SyncAsync();

        // Assert — no duplicate row, amount updated
        result.IsSuccess.Should().BeTrue();

        var entries = await _dbContext.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].FlexiId.Should().Be(201);
        entries[0].Amount.Should().Be(999m);
    }
}
