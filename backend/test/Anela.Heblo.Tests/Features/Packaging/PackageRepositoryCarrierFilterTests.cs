using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories.Packaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class PackageRepositoryCarrierFilterTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PackageRepository _repo;

    public PackageRepositoryCarrierFilterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CarrierFilter_{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
        _repo = new PackageRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static Package MakePackage(string orderCode, string code, string? name)
        => new()
        {
            OrderCode = orderCode,
            CustomerName = "Test",
            PackageNumber = "1",
            ShippingProviderCode = code,
            ShippingProviderName = name,
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private async Task<List<string>> QueryAsync(IReadOnlyList<string>? codes, IReadOnlyList<string>? names)
    {
        var (items, _) = await _repo.GetPaginatedAsync(
            null, null, null, codes, names, null, null, 1, 20, "PackedAt", true);
        return items.Select(p => p.OrderCode).OrderBy(o => o).ToList();
    }

    [Fact]
    public async Task GetPaginatedAsync_MatchesByStoredName_WhenCodeIsAPerOrderShippingId()
    {
        // Arrange — prod stores the Shoptet option's per-order ShippingId, not the registry id
        await _db.Packages.AddRangeAsync(
            MakePackage("Z-1", "280591", "Zásilkovna boxy a výdejní místa"),
            MakePackage("G-1", "280700", "GLS boxy a výdejní místa"));
        await _db.SaveChangesAsync();

        // Act
        var result = await QueryAsync(new[] { "502" }, new[] { "Zásilkovna boxy a výdejní místa" });

        // Assert
        result.Should().Equal("Z-1");
    }

    [Fact]
    public async Task GetPaginatedAsync_StillMatchesByRegistryCode()
    {
        // Arrange
        await _db.Packages.AddRangeAsync(
            MakePackage("P-1", "6", null),
            MakePackage("G-1", "97", null));
        await _db.SaveChangesAsync();

        // Act
        var result = await QueryAsync(new[] { "6" }, new[] { "PPL" });

        // Assert
        result.Should().Equal("P-1");
    }

    [Fact]
    public async Task GetPaginatedAsync_WithoutCarrierFilter_ReturnsEverything()
    {
        // Arrange
        await _db.Packages.AddRangeAsync(
            MakePackage("A", "6", null),
            MakePackage("B", "280591", "x"));
        await _db.SaveChangesAsync();

        // Act
        var result = await QueryAsync(null, null);

        // Assert
        result.Should().Equal("A", "B");
    }
}
