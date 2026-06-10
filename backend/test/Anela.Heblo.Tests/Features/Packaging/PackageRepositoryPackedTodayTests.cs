using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class PackageRepositoryPackedTodayTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PackageRepository _repo;

    // All times in Prague offset (+02:00) for clarity
    private static readonly TimeSpan Prague = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Today = new(2026, 6, 10, 0, 0, 0, Prague);
    private static readonly DateTimeOffset Tomorrow = Today.AddDays(1);

    public PackageRepositoryPackedTodayTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"PackedToday_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repo = new PackageRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static Package MakePackage(string orderCode, string? packedBy, Guid? packedByUserId, DateTimeOffset packedAt)
        => new()
        {
            OrderCode = orderCode,
            CustomerName = "Test",
            PackageNumber = $"PKG-{Guid.NewGuid():N}",
            ShippingProviderCode = "6",
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = packedAt,
            CreatedAt = packedAt,
            PackedBy = packedBy,
            PackedByUserId = packedByUserId,
        };

    [Fact]
    public async Task GetPackedTodayByPackerAsync_CountsDistinctOrdersPerPacker()
    {
        // Arrange — Alice packed ORD-1 twice (two packages), ORD-2 once → 2 distinct orders
        //           Bob packed ORD-3 once → 1 distinct order
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var withinWindow = Today.AddHours(10);

        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-1", "Alice", aliceId, withinWindow),
            MakePackage("ORD-1", "Alice", aliceId, withinWindow),
            MakePackage("ORD-2", "Alice", aliceId, withinWindow),
            MakePackage("ORD-3", "Bob", bobId, withinWindow));
        await _db.SaveChangesAsync();

        // Act
        var (total, byPacker) = await _repo.GetPackedTodayByPackerAsync(Today, Tomorrow);

        // Assert — distinct orders: ORD-1, ORD-2, ORD-3 → 3
        total.Should().Be(3);
        byPacker.Should().HaveCount(2);

        var alice = byPacker.First(p => p.PackedBy == "Alice");
        alice.DistinctOrderCount.Should().Be(2);
        alice.PackedByUserId.Should().Be(aliceId);

        var bob = byPacker.First(p => p.PackedBy == "Bob");
        bob.DistinctOrderCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPackedTodayByPackerAsync_ExcludesPackagesOutsideWindow()
    {
        // Arrange — one package before today, one package today
        var userId = Guid.NewGuid();
        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-YESTERDAY", "Alice", userId, Today.AddSeconds(-1)),
            MakePackage("ORD-TODAY", "Alice", userId, Today.AddHours(1)));
        await _db.SaveChangesAsync();

        // Act
        var (total, byPacker) = await _repo.GetPackedTodayByPackerAsync(Today, Tomorrow);

        // Assert
        total.Should().Be(1);
        byPacker.Should().ContainSingle(p => p.PackedBy == "Alice" && p.DistinctOrderCount == 1);
    }

    [Fact]
    public async Task GetPackedTodayByPackerAsync_ReturnsZeroTotal_WhenNoPackagesInWindow()
    {
        // Arrange — no packages at all
        // Act
        var (total, byPacker) = await _repo.GetPackedTodayByPackerAsync(Today, Tomorrow);

        // Assert
        total.Should().Be(0);
        byPacker.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPackedTodayByPackerAsync_OrdersByCountDescending()
    {
        // Arrange — Bob has more orders than Alice
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var t = Today.AddHours(5);

        await _db.Packages.AddRangeAsync(
            MakePackage("ORD-A1", "Alice", aliceId, t),
            MakePackage("ORD-B1", "Bob", bobId, t),
            MakePackage("ORD-B2", "Bob", bobId, t));
        await _db.SaveChangesAsync();

        // Act
        var (_, byPacker) = await _repo.GetPackedTodayByPackerAsync(Today, Tomorrow);

        // Assert — Bob first (2 orders), Alice second (1 order)
        byPacker[0].PackedBy.Should().Be("Bob");
        byPacker[1].PackedBy.Should().Be("Alice");
    }
}
