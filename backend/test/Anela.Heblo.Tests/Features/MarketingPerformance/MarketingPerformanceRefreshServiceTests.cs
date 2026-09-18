using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRefreshServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 5, 0, 0, TimeSpan.FromHours(2));
    private readonly ApplicationDbContext _context;
    private readonly MarketingPerformanceRepository _repo;
    private readonly Mock<IMonthlyRevenueSource> _revenue = new();
    private readonly Mock<IMonthlyAdCostSource> _costs = new();
    private readonly FakeTimeProvider _time = new(Now);

    public MarketingPerformanceRefreshServiceTests()
    {
        _context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _repo = new MarketingPerformanceRepository(_context);
        _revenue.Setup(r => r.GetAsync(It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YearMonth m, CancellationToken _) => new MonthlyRevenueSnapshot { RetailOrderCount = m.Month, RetailRevenueWithVat = m.Month * 1000m, WholesaleOrderCount = 1, WholesaleRevenueWithVat = 500m, SkippedEurInvoiceCount = 2 });
        _costs.Setup(c => c.GetAsync(It.IsAny<YearMonth>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YearMonth m, IReadOnlyCollection<string> _, CancellationToken _) => new List<AdCostInvoice>
            {
                new() { InvoiceNumber = "A", SupplierVatId = "IE1", AmountWithoutVat = 100m * m.Month, AccountingDate = m.Start },
                new() { InvoiceNumber = "B", SupplierVatId = "XX", AmountWithoutVat = 5m, AccountingDate = m.Start }, // unmatched
            });
    }

    public void Dispose() => _context.Dispose();

    private MarketingPerformanceRefreshService Service(int window = 2) => new(
        _repo, _revenue.Object, _costs.Object,
        Options.Create(new MarketingPerformanceOptions
        {
            RecomputeWindowMonths = window,
            Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } }, new MarketingChannelOptions { Code = "google", Label = "Google", VatIds = { "IE2" } } },
        }),
        _time, NullLogger<MarketingPerformanceRefreshService>.Instance);

    [Fact]
    public async Task RefreshWindowAsync_UpsertsCurrentAndPreviousMonth_AndReplacesChannelRows()
    {
        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.Months.Select(m => m.Month).Should().Equal(new YearMonth(2026, 8), new YearMonth(2026, 9));
        var sep = (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!;
        sep.RetailOrderCount.Should().Be(9);
        sep.RetailRevenueWithVat.Should().Be(9000m);
        sep.WholesaleOrderCount.Should().Be(1);
        sep.SkippedEurInvoiceCount.Should().Be(2);
        sep.ChannelCosts.Should().HaveCount(2);
        sep.ChannelCosts.Single(c => c.ChannelCode == "meta").CostWithoutVat.Should().Be(900m);
        sep.ChannelCosts.Single(c => c.ChannelCode == "google").CostWithoutVat.Should().Be(0m);
        sep.RevenueComputedAt.Should().Be(Now.UtcDateTime);
        sep.CostsComputedAt.Should().Be(Now.UtcDateTime);
        sep.LastError.Should().BeNull();
        sep.IsLocked.Should().BeFalse();
        result.Months[1].UnmatchedVatIdCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshWindowAsync_SecondRun_UpdatesInPlaceWithoutDuplicates()
    {
        await Service().RefreshWindowAsync(CancellationToken.None);
        await Service().RefreshWindowAsync(CancellationToken.None);

        _context.MarketingPerformanceMonths.Count().Should().Be(2);
        _context.MarketingPerformanceChannelCosts.Count().Should().Be(4);
    }

    [Fact]
    public async Task RefreshWindowAsync_LocksMonthsOlderThanWindow_AndSkipsThem()
    {
        await _repo.AddAsync(new MarketingPerformanceMonth { Year = 2026, Month = 7 }, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.LockedMonths.Should().Be(1);
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 7), CancellationToken.None))!.IsLocked.Should().BeTrue();
        _revenue.Verify(r => r.GetAsync(new YearMonth(2026, 7), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshWindowAsync_FlexiFails_KeepsRevenueAndRecordsError()
    {
        _costs.Setup(c => c.GetAsync(new YearMonth(2026, 9), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Flexi 503"));

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        var sep = (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!;
        sep.RetailOrderCount.Should().Be(9);
        sep.RevenueComputedAt.Should().NotBeNull();
        sep.CostsComputedAt.Should().BeNull();
        sep.LastError.Should().Contain("Flexi 503");
        result.Months.Single(m => m.Month == new YearMonth(2026, 9)).CostsOk.Should().BeFalse();
        result.Months.Single(m => m.Month == new YearMonth(2026, 8)).CostsOk.Should().BeTrue();
        result.AllFailed.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshWindowAsync_EverythingFails_AllFailedIsTrue()
    {
        _revenue.Setup(r => r.GetAsync(It.IsAny<YearMonth>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        _costs.Setup(c => c.GetAsync(It.IsAny<YearMonth>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("flexi"));

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.AllFailed.Should().BeTrue();
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!.LastError.Should().Contain("db").And.Contain("flexi");
    }

    [Fact]
    public async Task RecomputeRangeAsync_IgnoresLocks_AndDoesNotLock()
    {
        await _repo.AddAsync(new MarketingPerformanceMonth { Year = 2025, Month = 1, IsLocked = true, RetailOrderCount = 999 }, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var result = await Service().RecomputeRangeAsync(new YearMonth(2025, 1), new YearMonth(2025, 2), CancellationToken.None);

        result.Months.Should().HaveCount(2);
        result.LockedMonths.Should().Be(0);
        var jan = (await _repo.GetForUpdateAsync(new YearMonth(2025, 1), CancellationToken.None))!;
        jan.RetailOrderCount.Should().Be(1);
        jan.IsLocked.Should().BeTrue("recompute preserves the lock flag");
    }

    [Fact]
    public async Task RefreshWindowAsync_AtMonthBoundary_PicksCurrentMonthFromLocalTimeNotUtc()
    {
        // The same instant is 30 Sept 2026 22:30 UTC / 1 Oct 2026 00:30 local (+2h) — different calendar months.
        // FakeTimeProvider.GetUtcNow() returns exactly the DateTimeOffset it is constructed with (it does not
        // normalize a non-zero offset to +00:00), and GetLocalNow() only diverges from it once a non-UTC
        // LocalTimeZone is set. So the fixture must be constructed with an already-UTC (offset zero) instant
        // and given its local zone explicitly via SetLocalTimeZone; otherwise GetLocalNow() and GetUtcNow()
        // would return the identical value and this test could not distinguish the two code paths.
        var utcInstant = new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero);
        var boundaryTime = new FakeTimeProvider(utcInstant);
        boundaryTime.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Plus2", TimeSpan.FromHours(2), "Plus2", "Plus2"));
        var service = new MarketingPerformanceRefreshService(
            _repo, _revenue.Object, _costs.Object,
            Options.Create(new MarketingPerformanceOptions
            {
                RecomputeWindowMonths = 2,
                Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } }, new MarketingChannelOptions { Code = "google", Label = "Google", VatIds = { "IE2" } } },
            }),
            boundaryTime, NullLogger<MarketingPerformanceRefreshService>.Instance);

        var result = await service.RefreshWindowAsync(CancellationToken.None);

        // A UTC-based month selection would pick August + September instead of September + October.
        // This fixture fails if RefreshWindowAsync's current-month lookup ever switches from GetLocalNow() to GetUtcNow().
        result.Months.Select(m => m.Month).Should().Equal(new YearMonth(2026, 9), new YearMonth(2026, 10));

        // Stored timestamps remain UTC even though month selection is local.
        var oct = (await _repo.GetForUpdateAsync(new YearMonth(2026, 10), CancellationToken.None))!;
        oct.RevenueComputedAt.Should().Be(new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RunGuard_IsExclusive()
    {
        var guard = new MarketingPerformanceRunGuard();
        guard.TryBegin().Should().BeTrue();
        guard.TryBegin().Should().BeFalse();
        guard.IsRunning.Should().BeTrue();
        guard.End();
        guard.TryBegin().Should().BeTrue();
    }
}
