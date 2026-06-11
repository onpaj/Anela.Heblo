using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class PackageRepositoryAddMissingTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PackageRepository _repo;

    public PackageRepositoryAddMissingTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AddMissing_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repo = new PackageRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static Package MakePackage(string orderCode, string packageNumber, string? trackingNumber = null)
        => new()
        {
            OrderCode = orderCode,
            CustomerName = "Test",
            PackageNumber = packageNumber,
            TrackingNumber = trackingNumber,
            ShippingProviderCode = "PPL",
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task AddMissingAsync_InsertsRows_WhenNoneExist()
    {
        // Arrange
        var packages = new[]
        {
            MakePackage("ORD-1", "PKG-1", "TRK1"),
            MakePackage("ORD-1", "PKG-2", "TRK2"),
        };

        // Act
        await _repo.AddMissingAsync(packages);

        // Assert
        var stored = await _db.Packages.AsNoTracking().ToListAsync();
        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddMissingAsync_SkipsRows_WithExistingOrderCodeAndPackageNumber()
    {
        // Arrange — ORD-1/PKG-1 already persisted
        await _db.Packages.AddAsync(MakePackage("ORD-1", "PKG-1", "OLD"));
        await _db.SaveChangesAsync();

        // Act — re-submit PKG-1 (duplicate) plus a new PKG-2
        await _repo.AddMissingAsync(new[]
        {
            MakePackage("ORD-1", "PKG-1", "NEW"),
            MakePackage("ORD-1", "PKG-2", "TRK2"),
        });

        // Assert — PKG-1 not duplicated and not overwritten; PKG-2 added
        var stored = await _db.Packages.AsNoTracking().OrderBy(p => p.PackageNumber).ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(p => p.PackageNumber == "PKG-1").TrackingNumber.Should().Be("OLD");
        stored.Should().ContainSingle(p => p.PackageNumber == "PKG-2");
    }

    [Fact]
    public async Task AddMissingAsync_DeduplicatesWithinBatch()
    {
        // Arrange — same (OrderCode, PackageNumber) appears twice in one batch
        // Act
        await _repo.AddMissingAsync(new[]
        {
            MakePackage("ORD-1", "PKG-1", "TRK1"),
            MakePackage("ORD-1", "PKG-1", "TRK1"),
        });

        // Assert
        var stored = await _db.Packages.AsNoTracking().ToListAsync();
        stored.Should().ContainSingle();
    }

    [Fact]
    public async Task AddMissingAsync_NoOps_WhenBatchEmpty()
    {
        // Act
        await _repo.AddMissingAsync(Array.Empty<Package>());

        // Assert
        (await _db.Packages.CountAsync()).Should().Be(0);
    }
}
