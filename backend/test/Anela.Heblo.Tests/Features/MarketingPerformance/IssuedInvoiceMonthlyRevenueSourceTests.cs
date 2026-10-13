using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Invoices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class IssuedInvoiceMonthlyRevenueSourceTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public IssuedInvoiceMonthlyRevenueSourceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private static IssuedInvoice Invoice(string id, DateTime taxDate, decimal price, string currency = "CZK", bool? vatPayer = false) =>
        new()
        {
            Id = id,
            TaxDate = taxDate,
            InvoiceDate = taxDate,
            DueDate = taxDate.AddDays(14),
            Price = price,
            Currency = currency,
            VatPayer = vatPayer,
            CreationTime = taxDate,
        };

    [Fact]
    public async Task GetAsync_SplitsRetailAndWholesale_ByTaxDate_CzkOnly()
    {
        _context.IssuedInvoices.AddRange(
            Invoice("A1", new DateTime(2026, 8, 1), 1000m),
            Invoice("A2", new DateTime(2026, 8, 31, 23, 59, 0), 500m),
            Invoice("B1", new DateTime(2026, 8, 15), 8000m, vatPayer: true),
            Invoice("N1", new DateTime(2026, 8, 10), 300m, vatPayer: null),       // null VatPayer counts as retail
            Invoice("E1", new DateTime(2026, 8, 12), 40m, currency: "EUR"),
            Invoice("X1", new DateTime(2026, 9, 1), 999m),                        // next month
            Invoice("X0", new DateTime(2026, 7, 31), 999m));                      // previous month
        await _context.SaveChangesAsync();
        var source = new IssuedInvoiceMonthlyRevenueSource(_context);

        var snapshot = await source.GetAsync(new YearMonth(2026, 8), CancellationToken.None);

        snapshot.RetailOrderCount.Should().Be(3);
        snapshot.RetailRevenueWithVat.Should().Be(1800m);
        snapshot.WholesaleOrderCount.Should().Be(1);
        snapshot.WholesaleRevenueWithVat.Should().Be(8000m);
        snapshot.SkippedEurInvoiceCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_EmptyMonth_ReturnsZeros()
    {
        var source = new IssuedInvoiceMonthlyRevenueSource(_context);
        var snapshot = await source.GetAsync(new YearMonth(2026, 1), CancellationToken.None);
        snapshot.Should().BeEquivalentTo(new MonthlyRevenueSnapshot());
    }
}
