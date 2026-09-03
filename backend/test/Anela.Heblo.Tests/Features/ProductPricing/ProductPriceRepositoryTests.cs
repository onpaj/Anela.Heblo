using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.ProductPricing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProductPriceRepository _repository;

    public ProductPriceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new ProductPriceRepository(_context);
    }

    [Fact]
    public async Task upsert_inserts_a_price_that_does_not_exist_yet()
    {
        // Arrange
        var price = new ProductPrice
        {
            ProductCode = "OCH001030",
            PriceWithVat = 190.00m,
            VatRate = 21m,
            ModifiedAt = new DateTime(2026, 9, 3, 10, 0, 0),
            ModifiedBy = "ondra@anela.cz",
        };

        // Act
        await _repository.UpsertAsync(price, CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        var stored = await _repository.GetAsync("OCH001030", CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.PriceWithVat.Should().Be(190.00m);
    }

    [Fact]
    public async Task upsert_overwrites_an_existing_price_without_duplicating_the_row()
    {
        // Arrange
        await _repository.UpsertAsync(
            new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 190.00m, VatRate = 21m },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        await _repository.UpsertAsync(
            new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 210.00m, VatRate = 21m },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Assert
        var all = await _repository.GetAllAsync(CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].PriceWithVat.Should().Be(210.00m);
    }

    [Fact]
    public async Task sync_states_are_keyed_by_product_and_target_independently()
    {
        // Arrange
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState
            {
                ProductCode = "OCH001030",
                Target = PriceSyncTarget.Shoptet,
                Status = PriceSyncStatus.InSync,
                LastPushedPriceWithVat = 190.00m,
            },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState
            {
                ProductCode = "OCH001030",
                Target = PriceSyncTarget.Flexi,
                Status = PriceSyncStatus.Conflict,
                RemoteValueAtConflict = 175.00m,
            },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        var shoptet = await _repository.GetSyncStateAsync("OCH001030", PriceSyncTarget.Shoptet, CancellationToken.None);
        var flexi = await _repository.GetSyncStateAsync("OCH001030", PriceSyncTarget.Flexi, CancellationToken.None);

        // Assert
        shoptet!.Status.Should().Be(PriceSyncStatus.InSync);
        flexi!.Status.Should().Be(PriceSyncStatus.Conflict);
        flexi.RemoteValueAtConflict.Should().Be(175.00m);
    }

    [Fact]
    public async Task get_conflicts_returns_only_conflicted_states_across_both_targets()
    {
        // Arrange
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "A", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.InSync },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "B", Target = PriceSyncTarget.Shoptet, Status = PriceSyncStatus.Conflict },
            CancellationToken.None);
        await _repository.UpsertSyncStateAsync(
            new ProductPriceSyncState { ProductCode = "C", Target = PriceSyncTarget.Flexi, Status = PriceSyncStatus.Conflict },
            CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        // Act
        var conflicts = await _repository.GetConflictsAsync(CancellationToken.None);

        // Assert
        conflicts.Select(c => c.ProductCode).Should().BeEquivalentTo(new[] { "B", "C" });
    }

    public void Dispose() => _context.Dispose();
}
