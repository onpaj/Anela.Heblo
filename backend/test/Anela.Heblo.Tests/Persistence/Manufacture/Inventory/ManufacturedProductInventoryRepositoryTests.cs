using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ManufacturedInventoryTests_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ManufacturedProductInventoryItem CreateItem(string productCode, decimal amount, string? lot = null)
        => new(productCode, $"Name {productCode}", amount, "test", DateTime.UtcNow, lotNumber: lot);

    [Fact]
    public async Task GetTotalAmountByProductCodeAsync_SumsAmountsAcrossLots()
    {
        // Arrange
        await using var context = CreateContext();
        context.ManufacturedProductInventoryItems.AddRange(
            CreateItem("PROD001", 10m, lot: "L1"),
            CreateItem("PROD001", 5m, lot: "L2"),
            CreateItem("PROD002", 3m));
        await context.SaveChangesAsync();
        var repository = new ManufacturedProductInventoryRepository(context);

        // Act
        var result = await repository.GetTotalAmountByProductCodeAsync();

        // Assert
        result.Should().HaveCount(2);
        result["PROD001"].Should().Be(15m);
        result["PROD002"].Should().Be(3m);
    }

    [Fact]
    public async Task GetTotalAmountByProductCodeAsync_WithNoItems_ReturnsEmptyDictionary()
    {
        // Arrange
        await using var context = CreateContext();
        var repository = new ManufacturedProductInventoryRepository(context);

        // Act
        var result = await repository.GetTotalAmountByProductCodeAsync();

        // Assert
        result.Should().BeEmpty();
    }
}
