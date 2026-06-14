using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankStatementStatisticsSourceAdapterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BankStatementStatisticsSourceAdapter _adapter;

    public BankStatementStatisticsSourceAdapterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"BankStats_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _adapter = new BankStatementStatisticsSourceAdapter(_context);
    }

    public void Dispose() => _context.Dispose();

    private static BankStatementImport MakeStatement(
        string transferId, DateTime statementDate, DateTime? importDate = null, int itemCount = 0)
    {
        var statement = new BankStatementImport(transferId, statementDate);
        statement.Account = "TEST-ACCT";
        statement.Currency = CurrencyCode.CZK;
        statement.ItemCount = itemCount;
        statement.ImportResult = "OK";

        if (importDate is not null)
        {
            typeof(BankStatementImport)
                .GetProperty(nameof(BankStatementImport.ImportDate))!
                .SetValue(statement, importDate);
        }

        return statement;
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_StatementDateBranch_ReturnsCountsAndSummedItemCount()
    {
        // Arrange
        var day1 = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("S-1", day1, itemCount: 3),
            MakeStatement("S-2", day1, itemCount: 7),
            MakeStatement("S-3", day2, itemCount: 5));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        var d1 = result.Single(r => r.Date.Date == new DateTime(2026, 6, 1));
        d1.Date.Kind.Should().Be(DateTimeKind.Utc);
        d1.ImportCount.Should().Be(2);
        d1.TotalItemCount.Should().Be(10);
        var d2 = result.Single(r => r.Date.Date == new DateTime(2026, 6, 2));
        d2.ImportCount.Should().Be(1);
        d2.TotalItemCount.Should().Be(5);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_ImportDateBranch_ReturnsCountsAndSummedItemCount()
    {
        // Arrange
        var statementDay = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var importDay = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("I-1", statementDay, importDate: importDay, itemCount: 4),
            MakeStatement("I-2", statementDay, importDate: importDay.AddHours(3), itemCount: 6),
            MakeStatement("I-3", statementDay, importDate: importDay.AddDays(1), itemCount: 2));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.ImportDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).ImportCount.Should().Be(2);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).TotalItemCount.Should().Be(10);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).TotalItemCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_EmptyRange_ReturnsZeroRowsForEveryDay()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().Equal(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 2),
            new DateTime(2026, 6, 3));
        result.Should().OnlyContain(r => r.ImportCount == 0);
        result.Should().OnlyContain(r => r.TotalItemCount == 0);
        result.Should().OnlyContain(r => r.Date.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_InclusiveBoundaries_IncludesStatementsOnStartAndEndDate()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 12, 23, 59, 59, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("B-START", startDate, itemCount: 1),
            MakeStatement("B-MID", new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc), itemCount: 2),
            MakeStatement("B-END", endDate, itemCount: 3));
        await _context.SaveChangesAsync();

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 10)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 11)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 12)).ImportCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_GapFill_EmitsZeroRowsForMissingDays()
    {
        // Arrange
        _context.BankStatements.Add(
            MakeStatement("G-1", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc), itemCount: 4));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().BeInAscendingOrder();
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).ImportCount.Should().Be(0);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).TotalItemCount.Should().Be(4);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 3)).ImportCount.Should().Be(0);
    }
}
