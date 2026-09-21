using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Tests for CostPoolService.
/// Collection attribute forces sequential execution: the service guards
/// RefreshAsync with a static SemaphoreSlim.
/// </summary>
[Collection("CostPoolServiceTests")]
public class CostPoolServiceTests
{
    private static LedgerItem Entry(DateTime date, string? department, decimal amount) => new()
    {
        Date = date,
        Department = department!,
        Amount = amount,
        DocumentNumber = "DOC",
        ClientName = "CLIENT",
        VariableSymbol = "VS",
        DebitAccountNumber = "518100",
        DebitAccountName = "Ostatni sluzby",
        CreditAccountNumber = "321100",
        CreditAccountName = "Dodavatele"
    };

    private static CostPoolService CreateService(
        IList<LedgerItem> ledgerItems,
        Mock<ICostPoolCache>? cacheMock = null,
        Mock<ILogger<CostPoolService>>? loggerMock = null) =>
        CreateService(ledgerItems, out _, cacheMock, loggerMock);

    /// <summary>
    /// Overload that exposes the ledger mock for callers that need to Verify calls
    /// against it, and optionally makes the ledger call throw instead of return.
    /// </summary>
    private static CostPoolService CreateService(
        IList<LedgerItem> ledgerItems,
        out Mock<ILedgerService> ledgerMock,
        Mock<ICostPoolCache>? cacheMock = null,
        Mock<ILogger<CostPoolService>>? loggerMock = null,
        Exception? ledgerFailure = null)
    {
        ledgerMock = new Mock<ILedgerService>();
        var ledgerSetup = ledgerMock
            .Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()));

        if (ledgerFailure is not null)
        {
            ledgerSetup.ThrowsAsync(ledgerFailure);
        }
        else
        {
            ledgerSetup.ReturnsAsync(ledgerItems);
        }

        var cache = cacheMock ?? new Mock<ICostPoolCache>();
        cache.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(CostPoolCacheData.Empty());

        return new CostPoolService(
            cache.Object,
            ledgerMock.Object,
            (loggerMock ?? new Mock<ILogger<CostPoolService>>()).Object,
            Options.Create(new DataSourceOptions()));
    }

    private static decimal AmountFor(
        IReadOnlyList<MonthlyCostPool> pools, int year, int month, CostPool pool) =>
        pools.Single(p => p.Month == new DateTime(year, month, 1) && p.Pool == pool).Amount;

    [Fact]
    public async Task GetMonthlyPoolsAsync_BucketsDepartmentsIntoTheirPools()
    {
        // Arrange
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            // VYROBA deliberately differs from SKLAD+MARKETING so an M1/M2 swap
            // in CostPoolDefinition.Resolve would not pass this test by coincidence.
            Entry(july, "VYROBA", 90m),
            Entry(july, "SKLAD", 30m),
            Entry(july, "MARKETING", 70m),
            Entry(july, "CENTRALA", 500m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M1).Should().Be(90m);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(100m);
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_FoldsMissingDepartmentIntoM3()
    {
        // Arrange
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, null, 11m),
            Entry(july, "", 22m),
            Entry(july, "   ", 33m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(66m);
        AmountFor(pools, 2026, 7, CostPool.M1).Should().Be(0m);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_PoolsSumToTheFullLedgerTotal_ForAccounts51And52()
    {
        // Arrange - the balance invariant, but only over the accounts every pool
        // counts: Entry() books on 518100. It is NOT true of the ledger as a whole -
        // see CostPoolSalesCostParityTests.PoolsSumToTheFullLedgerTotal_IncludingSpendNoMarginLevelSeesToday,
        // where BUVOL and 50x-outside-M2 entries are deliberately dropped.
        var july = new DateTime(2026, 7, 15);
        var entries = new List<LedgerItem>
        {
            Entry(july, "VYROBA", 100m),
            Entry(july, "SKLAD", 30m),
            Entry(july, "MARKETING", 70m),
            Entry(july, "CENTRALA", 500m),
            Entry(july, "ESHOP", 250m),
            Entry(july, null, 12m),
        };
        var service = CreateService(entries);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Sum(p => p.Amount).Should().Be(entries.Sum(e => e.Amount));
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_SeparatesMonths()
    {
        // Arrange
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 7, 15), "CENTRALA", 500m),
            Entry(new DateTime(2026, 8, 3), "CENTRALA", 800m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
        AmountFor(pools, 2026, 8, CostPool.M3).Should().Be(800m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_EmitsZeroRowsForEveryPoolInEveryRequestedMonth()
    {
        // Arrange - one entry in August only, three months requested
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 8, 3), "SKLAD", 800m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

        // Assert - 3 months x 3 pools, callers never distinguish "no data" from "no spend"
        pools.Should().HaveCount(9);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(0m);
        AmountFor(pools, 2026, 8, CostPool.M2).Should().Be(800m);
        AmountFor(pools, 2026, 9, CostPool.M2).Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_OrdersByMonthThenPool()
    {
        // Arrange
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 8, 3), "SKLAD", 1m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));

        // Assert
        pools.Select(p => (p.Month, p.Pool)).Should().ContainInOrder(
            (new DateTime(2026, 7, 1), CostPool.M1),
            (new DateTime(2026, 7, 1), CostPool.M2),
            (new DateTime(2026, 7, 1), CostPool.M3),
            (new DateTime(2026, 8, 1), CostPool.M1),
            (new DateTime(2026, 8, 1), CostPool.M2),
            (new DateTime(2026, 8, 1), CostPool.M3));
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_QueriesDirectCostAccountsAcrossAllDepartments()
    {
        // Arrange
        var service = CreateService(new List<LedgerItem>(), out var ledgerMock);

        // Act
        await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert - one unfiltered pull on every prefix any pool counts, which would
        // replace the three department-filtered ones once the dedup follow-up lands.
        // Wider than any single pool's own set (50x counts only in M2), so the
        // service buckets through CostPoolDefinition.Resolve rather than summing.
        ledgerMock.Verify(l => l.GetLedgerItems(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31, 23, 59, 59),
            It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "50", "51", "52" })),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        ledgerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ExpandsPartialMonthsToWholeMonths()
    {
        // Arrange - a mid-month entry must still be counted when the range starts mid-month
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 7, 2), "CENTRALA", 500m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 25));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_PropagatesLedgerFailure()
    {
        // Arrange - a wrong zero in a financial calculation is worse than a visible failure
        var service = CreateService(
            new List<LedgerItem>(),
            out _,
            ledgerFailure: new HttpRequestException("FlexiBee unreachable"));

        // Act
        var act = () => service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ThrowsWhenFromIsAfterTo()
    {
        // Arrange - the interface contract forbids a silently-empty result
        var service = CreateService(new List<LedgerItem>(), out _);

        // Act
        var act = () => service.GetMonthlyPoolsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(e => e.ParamName == "from");
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_LogsTheDepartmentsFoldedIntoM3()
    {
        // Arrange - a miscoded entry becoming overhead must be visible in the logs
        var loggerMock = new Mock<ILogger<CostPoolService>>();
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, "CENTRALA", 500m),
            Entry(july, "ESHOP", 250m),
            Entry(july, "SKLAD", 30m),
        }, loggerMock: loggerMock);

        // Act
        await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("CENTRALA") && v.ToString()!.Contains("ESHOP")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_LogsAnUnknownDepartmentThatOnlyBooksOnConsumables()
    {
        // Arrange - the gap the M3 catch-all alone does not cover: a cost centre added
        // in Flexi that books on 50x resolves to null, not M3, so it lands in no pool
        // and would also miss the M3 log line. It must still be visible somewhere.
        var loggerMock = new Mock<ILogger<CostPoolService>>();
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Consumables(july, "WEBDEV", 4_200m),
        }, loggerMock: loggerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert - counted by no pool ...
        AmountFor(pools, 2026, 7, CostPool.M1).Should().Be(0m);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(0m);
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(0m);

        // ... but not silently: the department and its amount reach the log
        VerifyInformationLogged(loggerMock, "WEBDEV", "4200");
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_LogsExcludedSeparateActivityAsDropped()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<CostPoolService>>();
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, "BUVOL", 999m),
        }, loggerMock: loggerMock);

        // Act
        await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        VerifyInformationLogged(loggerMock, "BUVOL", "999");
    }

    private static LedgerItem Consumables(DateTime date, string? department, decimal amount)
    {
        var entry = Entry(date, department, amount);
        entry.DebitAccountNumber = "501200";
        entry.DebitAccountName = "Spotreba materialu";
        return entry;
    }

    private static void VerifyInformationLogged(
        Mock<ILogger<CostPoolService>> loggerMock,
        params string[] contains) =>
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => contains.All(c => v.ToString()!.Contains(c))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
