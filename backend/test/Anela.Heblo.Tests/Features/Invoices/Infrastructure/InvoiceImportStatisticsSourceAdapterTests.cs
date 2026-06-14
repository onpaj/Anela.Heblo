using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public sealed class InvoiceImportStatisticsSourceAdapterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly InvoiceImportStatisticsSourceAdapter _adapter;

    public InvoiceImportStatisticsSourceAdapterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"InvoiceStats_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _adapter = new InvoiceImportStatisticsSourceAdapter(_context);
    }

    public void Dispose() => _context.Dispose();

    private static IssuedInvoice MakeInvoice(string id, DateTime invoiceDate, DateTime? lastSyncTime = null)
    {
        var invoice = new IssuedInvoice
        {
            Id = id,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(14),
            TaxDate = invoiceDate,
        };

        if (lastSyncTime is not null)
        {
            typeof(IssuedInvoice)
                .GetProperty(nameof(IssuedInvoice.LastSyncTime))!
                .SetValue(invoice, lastSyncTime);
        }

        return invoice;
    }

    [Fact]
    public async Task GetDailyCountsAsync_InvoiceDateBranch_ReturnsCountsGroupedByDay()
    {
        // Arrange
        var day1 = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

        _context.IssuedInvoices.AddRange(
            MakeInvoice("INV-1", day1),
            MakeInvoice("INV-2", day1.AddHours(3)),
            MakeInvoice("INV-3", day2));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Utc));
        result[0].Date.Kind.Should().Be(DateTimeKind.Utc);
        result[0].Count.Should().Be(2);
        result[0].IsBelowThreshold.Should().BeFalse();
        result[1].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 2), DateTimeKind.Utc));
        result[1].Count.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyCountsAsync_SyncTimeBranch_IgnoresInvoicesWithNullSyncTime()
    {
        // Arrange
        var syncedDay = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
        _context.IssuedInvoices.AddRange(
            MakeInvoice("INV-A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay),
            MakeInvoice("INV-B", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay.AddHours(2)),
            MakeInvoice("INV-NULL", new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: null));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 5, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyCountsAsync_EmptyRange_ReturnsZeroCountsForEveryDay()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().Equal(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 2),
            new DateTime(2026, 6, 3));
        result.Should().OnlyContain(r => r.Count == 0);
        result.Should().OnlyContain(r => r.Date.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetDailyCountsAsync_InclusiveBoundaries_IncludesInvoicesOnStartAndEndDate()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 12, 23, 59, 59, DateTimeKind.Utc);

        _context.IssuedInvoices.AddRange(
            MakeInvoice("INV-START", startDate),
            MakeInvoice("INV-MID", new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc)),
            MakeInvoice("INV-END", endDate));
        await _context.SaveChangesAsync();

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 10)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 11)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 12)).Count.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyCountsAsync_GapFill_EmitsZeroRowsForMissingDays()
    {
        // Arrange
        _context.IssuedInvoices.Add(
            MakeInvoice("INV-1", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc)));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().BeInAscendingOrder();
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).Count.Should().Be(0);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 3)).Count.Should().Be(0);
    }
}
