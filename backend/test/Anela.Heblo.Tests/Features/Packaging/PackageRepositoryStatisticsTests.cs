using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class PackageRepositoryStatisticsTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PackageRepository _repo;

    // Fixed Prague zone so day/hour bucketing is deterministic regardless of host TZ.
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2); // CEST (summer)
    private static readonly DateTimeOffset WindowStart = new(2026, 6, 1, 0, 0, 0, Offset);
    private static readonly DateTimeOffset WindowEnd = new(2026, 6, 30, 0, 0, 0, Offset);

    public PackageRepositoryStatisticsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PackingStats_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repo = new PackageRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static Package MakePackage(
        string orderCode,
        string packageNumber,
        DateTimeOffset packedAt,
        string carrierCode = "DPD",
        string? carrierName = "DPD",
        string? packedBy = null,
        Guid? packedByUserId = null,
        string? trackingNumber = "TRACK")
        => new()
        {
            OrderCode = orderCode,
            CustomerName = "Test",
            PackageNumber = packageNumber,
            ShippingProviderCode = carrierCode,
            ShippingProviderName = carrierName,
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = packedAt,
            CreatedAt = packedAt,
            PackedBy = packedBy,
            PackedByUserId = packedByUserId,
            TrackingNumber = trackingNumber,
        };

    private Task<PackingStatistics> Aggregate() =>
        _repo.GetPackingStatisticsAsync(WindowStart, WindowEnd, Prague);

    [Fact]
    public async Task TotalsCountPackagesAndDistinctOrders()
    {
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "1", WindowStart.AddDays(2)),
            MakePackage("ORD-1", "2", WindowStart.AddDays(2)),
            MakePackage("ORD-2", "1", WindowStart.AddDays(3)));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.TotalPackages.Should().Be(3);
        stats.TotalOrders.Should().Be(2);
    }

    [Fact]
    public async Task ExcludesRowsOutsideWindow()
    {
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-BEFORE", "1", WindowStart.AddSeconds(-1)),
            MakePackage("ORD-AFTER", "1", WindowEnd),
            MakePackage("ORD-IN", "1", WindowStart.AddDays(1)));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.TotalPackages.Should().Be(1);
        stats.TotalOrders.Should().Be(1);
    }

    [Fact]
    public async Task DailyThroughputGroupsByLocalDay()
    {
        // Two packages on Jun 3 (one order), one on Jun 5.
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-A", "1", new DateTimeOffset(2026, 6, 3, 9, 0, 0, Offset)),
            MakePackage("ORD-A", "2", new DateTimeOffset(2026, 6, 3, 9, 5, 0, Offset)),
            MakePackage("ORD-B", "1", new DateTimeOffset(2026, 6, 5, 14, 0, 0, Offset)));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        var jun3 = stats.ThroughputDaily.Single(d => d.Date == new DateOnly(2026, 6, 3));
        jun3.PackageCount.Should().Be(2);
        jun3.OrderCount.Should().Be(1);
        stats.ThroughputDaily.Single(d => d.Date == new DateOnly(2026, 6, 5)).PackageCount.Should().Be(1);
    }

    [Fact]
    public async Task HourHeatmapUsesLocalWeekdayAndHour()
    {
        // 2026-06-03 is a Wednesday (ISO day 3). 09:30 local.
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-A", "1", new DateTimeOffset(2026, 6, 3, 9, 30, 0, Offset)));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        var cell = stats.HourHeatmap.Single();
        cell.DayOfWeek.Should().Be(3); // Wednesday, ISO
        cell.Hour.Should().Be(9);
        cell.PackageCount.Should().Be(1);
    }

    [Fact]
    public async Task ByPackerCountsDistinctOrdersAndExcludesUnattributed()
    {
        var alice = Guid.NewGuid();
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "1", WindowStart.AddDays(10), packedBy: "Alice", packedByUserId: alice),
            MakePackage("ORD-2", "1", WindowStart.AddDays(10), packedBy: "Alice", packedByUserId: alice),
            MakePackage("ORD-3", "1", WindowStart.AddDays(1), packedBy: null, packedByUserId: null));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.ByPacker.Should().ContainSingle();
        var packer = stats.ByPacker.Single();
        packer.PackerId.Should().Be(alice);
        packer.OrderCount.Should().Be(2);
        packer.PackageCount.Should().Be(2);
    }

    [Fact]
    public async Task PackerAttributionSinceIsEarliestAttributedLocalDay()
    {
        var alice = Guid.NewGuid();
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-OLD", "1", WindowStart.AddDays(1), packedBy: null, packedByUserId: null),
            MakePackage("ORD-NEW", "1", new DateTimeOffset(2026, 6, 9, 8, 0, 0, Offset), packedBy: "Alice", packedByUserId: alice));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.PackerAttributionSince.Should().Be(new DateOnly(2026, 6, 9));
    }

    [Fact]
    public async Task ByCarrierCountsPackagesPerProvider()
    {
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "1", WindowStart.AddDays(1), carrierCode: "DPD", carrierName: "DPD"),
            MakePackage("ORD-2", "1", WindowStart.AddDays(1), carrierCode: "DPD", carrierName: "DPD"),
            MakePackage("ORD-3", "1", WindowStart.AddDays(1), carrierCode: "GLS", carrierName: "GLS"));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.ByCarrier.Single(c => c.Code == "DPD").PackageCount.Should().Be(2);
        stats.ByCarrier.Single(c => c.Code == "GLS").PackageCount.Should().Be(1);
    }

    [Fact]
    public async Task PackagesPerOrderBucketsCapAtThreePlus()
    {
        // ORD-1: 1 package, ORD-2: 2 packages, ORD-3: 4 packages → bucket 3+ .
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "1", WindowStart.AddDays(1)),
            MakePackage("ORD-2", "1", WindowStart.AddDays(1)),
            MakePackage("ORD-2", "2", WindowStart.AddDays(1)),
            MakePackage("ORD-3", "1", WindowStart.AddDays(1)),
            MakePackage("ORD-3", "2", WindowStart.AddDays(1)),
            MakePackage("ORD-3", "3", WindowStart.AddDays(1)),
            MakePackage("ORD-3", "4", WindowStart.AddDays(1)));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.PackagesPerOrder.Single(b => b.PackageCount == 1).OrderCount.Should().Be(1);
        stats.PackagesPerOrder.Single(b => b.PackageCount == 2).OrderCount.Should().Be(1);
        stats.PackagesPerOrder.Single(b => b.PackageCount == 3).OrderCount.Should().Be(1); // 3 means "3+"
    }

    [Fact]
    public async Task TrackingCoverageCountsNonNullTrackingNumbers()
    {
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "1", WindowStart.AddDays(1), trackingNumber: "T1"),
            MakePackage("ORD-2", "1", WindowStart.AddDays(1), trackingNumber: null));
        await _db.SaveChangesAsync();

        var stats = await Aggregate();

        stats.TotalPackages.Should().Be(2);
        stats.PackagesWithTracking.Should().Be(1);
    }

    [Fact]
    public async Task EmptyWindowReturnsEmptySeries()
    {
        var stats = await Aggregate();

        stats.TotalPackages.Should().Be(0);
        stats.TotalOrders.Should().Be(0);
        stats.ThroughputDaily.Should().BeEmpty();
        stats.ByPacker.Should().BeEmpty();
        stats.PackerAttributionSince.Should().BeNull();
    }
}
