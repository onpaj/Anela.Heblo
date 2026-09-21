using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MarketingPerformanceRepository _repo;

    public MarketingPerformanceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _context = new ApplicationDbContext(options);
        _repo = new MarketingPerformanceRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private static MarketingPerformanceMonth Month(int y, int m, bool locked = false, DateTime? computed = null) => new()
    {
        Year = y,
        Month = m,
        IsLocked = locked,
        RevenueComputedAt = computed,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 10m, InvoiceCount = 1 } },
    };

    [Fact]
    public async Task GetRangeAsync_ReturnsInclusiveAscendingWithChannelCosts()
    {
        await _repo.AddAsync(Month(2026, 3), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 1), CancellationToken.None);
        await _repo.AddAsync(Month(2025, 12), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 4), CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var rows = await _repo.GetRangeAsync(new YearMonth(2026, 1), new YearMonth(2026, 3), CancellationToken.None);

        rows.Select(r => r.Key).Should().Equal(new YearMonth(2026, 1), new YearMonth(2026, 3));
        rows[0].ChannelCosts.Should().ContainSingle(c => c.ChannelCode == "meta");
    }

    [Fact]
    public async Task LockMonthsBeforeAsync_LocksOnlyOlderUnlockedMonths()
    {
        await _repo.AddAsync(Month(2026, 6), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 7), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 8), CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var affected = await _repo.LockMonthsBeforeAsync(new YearMonth(2026, 8), CancellationToken.None);

        affected.Should().Be(2);
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 8), CancellationToken.None))!.IsLocked.Should().BeFalse();
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 7), CancellationToken.None))!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastComputedAtAsync_ReturnsMaxOfBothTimestamps()
    {
        await _repo.AddAsync(Month(2026, 7, computed: new DateTime(2026, 8, 1, 5, 0, 0)), CancellationToken.None);
        var m = Month(2026, 8);
        m.CostsComputedAt = new DateTime(2026, 9, 1, 5, 0, 0);
        await _repo.AddAsync(m, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        (await _repo.GetLastComputedAtAsync(CancellationToken.None)).Should().Be(new DateTime(2026, 9, 1, 5, 0, 0));
    }
}
