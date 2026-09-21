using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Marketing;

/// <summary>
/// Revenue step: aggregates the already-synced Shoptet issued invoices for one month.
/// CZK only, by TaxDate. Price is the with-VAT total (PriceC is never populated).
/// Wholesale = customer has a VAT ID (VatPayer == true) — the same rule Flexi sales query 37 uses.
/// </summary>
public class IssuedInvoiceMonthlyRevenueSource : IMonthlyRevenueSource
{
    private const string Czk = "CZK";
    private const string Eur = "EUR";
    private readonly ApplicationDbContext _context;

    public IssuedInvoiceMonthlyRevenueSource(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var start = month.Start;
        var end = month.EndExclusive;

        var groups = await _context.IssuedInvoices.AsNoTracking()
            .Where(i => i.Currency == Czk && i.TaxDate >= start && i.TaxDate < end)
            .GroupBy(i => i.VatPayer == true)
            .Select(g => new { IsWholesale = g.Key, Count = g.Count(), Sum = g.Sum(i => i.Price) })
            .ToListAsync(cancellationToken);

        var eurCount = await _context.IssuedInvoices.AsNoTracking()
            .CountAsync(i => i.Currency == Eur && i.TaxDate >= start && i.TaxDate < end, cancellationToken);

        var retail = groups.SingleOrDefault(g => !g.IsWholesale);
        var wholesale = groups.SingleOrDefault(g => g.IsWholesale);

        return new MonthlyRevenueSnapshot
        {
            RetailOrderCount = retail?.Count ?? 0,
            RetailRevenueWithVat = retail?.Sum ?? 0m,
            WholesaleOrderCount = wholesale?.Count ?? 0,
            WholesaleRevenueWithVat = wholesale?.Sum ?? 0m,
            SkippedEurInvoiceCount = eurCount,
        };
    }
}
