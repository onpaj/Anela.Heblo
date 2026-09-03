using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
// ProductType and CatalogAggregate come from Anela.Heblo.Domain.Features.Catalog
using Anela.Heblo.Domain.Features.ProductPricing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceSyncServiceTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly List<ProductPriceSyncState> _savedStates = new();

    private bool _inScopeConfigured;

    private ProductPriceSyncService CreateService()
    {
        if (!_inScopeConfigured)
        {
            GivenInScope(("A", ProductType.Product), ("B", ProductType.Product));
        }

        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => _savedStates.Add(s))
            .Returns(Task.CompletedTask);

        return new ProductPriceSyncService(
            _repository.Object,
            _eshop.Object,
            _erpWriter.Object,
            _erpReader.Object,
            _catalog.Object,
            NullLogger<ProductPriceSyncService>.Instance);
    }

    private void GivenInScope(params (string Code, ProductType Type)[] products)
    {
        _inScopeConfigured = true;
        _catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products
                .Select(p => new CatalogAggregate { ProductCode = p.Code, Type = p.Type })
                .ToList());
    }

    private void GivenHebloPrice(string code, decimal priceWithVat) =>
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = code, PriceWithVat = priceWithVat, VatRate = 21m },
            });

    private void GivenSyncState(string code, PriceSyncTarget target, decimal? lastPushed) =>
        _repository
            .Setup(r => r.GetSyncStatesAsync(target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>
            {
                new() { ProductCode = code, Target = target, LastPushedPriceWithVat = lastPushed },
            });

    private void GivenErp(string code, int erpItemId, decimal priceWithVat) =>
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new()
                {
                    ProductCode = code,
                    ErpItemId = erpItemId,
                    PriceWithVat = priceWithVat,
                    PriceWithoutVat = Math.Round(priceWithVat / 1.21m, 2, MidpointRounding.AwayFromZero),
                },
            });

    [Fact]
    public async Task pushes_to_both_targets_when_only_heblo_changed()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Pushed.Should().Be(2);
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(147, 173.55m, It.IsAny<CancellationToken>()), Times.Once);
        _savedStates.Should().OnlyContain(s => s.Status == PriceSyncStatus.InSync);
        _savedStates.Should().OnlyContain(s => s.LastPushedPriceWithVat == 210.00m);
    }

    [Fact]
    public async Task records_a_conflict_and_pushes_nothing_when_the_remote_moved()
    {
        // Arrange
        GivenHebloPrice("A", 190.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 175.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Conflicts.Should().Be(1);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        var shoptetState = _savedStates.Single(s => s.Target == PriceSyncTarget.Shoptet);
        shoptetState.Status.Should().Be(PriceSyncStatus.Conflict);
        shoptetState.RemoteValueAtConflict.Should().Be(175.00m);
    }

    [Fact]
    public async Task a_conflict_on_one_target_does_not_block_the_other()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 175.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Conflicts.Should().Be(1);
        result.Pushed.Should().Be(1);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(147, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task seeds_heblo_from_shoptet_and_conflicts_flexi_when_the_two_disagree()
    {
        // Arrange
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProductPrice>());
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: null);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: null);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 175.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Seeded.Should().Be(1);
        result.Conflicts.Should().Be(1);
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.ProductCode == "A" && p.PriceWithVat == 190.00m),
                               It.IsAny<CancellationToken>()),
            Times.Once);
        _savedStates.Single(s => s.Target == PriceSyncTarget.Flexi).Status.Should().Be(PriceSyncStatus.Conflict);
    }

    [Fact]
    public async Task marks_failed_when_flexi_has_no_internal_item_id_and_never_creates_the_record()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 210.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 210.00m });
        GivenErp("A", erpItemId: 0, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Failed.Should().Be(1);
        _erpWriter.Verify(
            w => w.SetPriceWithoutVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _savedStates.Single(s => s.Target == PriceSyncTarget.Flexi).LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task one_products_push_failure_does_not_abort_the_run()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "A", PriceWithVat = 210.00m, VatRate = 21m },
                new() { ProductCode = "B", PriceWithVat = 310.00m, VatRate = 21m },
            });
        // Mimic production: GetSyncStatesAsync(target) returns only that target's rows.
        // A single shared list would hand the SAME instances to both passes, so the Flexi
        // pass would mutate the very objects the Shoptet assertion inspects.
        _repository
            .Setup(r => r.GetSyncStatesAsync(It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PriceSyncTarget target, CancellationToken _) => new List<ProductPriceSyncState>
            {
                new() { ProductCode = "A", Target = target, LastPushedPriceWithVat = 190.00m },
                new() { ProductCode = "B", Target = target, LastPushedPriceWithVat = 290.00m },
            });
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m, ["B"] = 290.00m });
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("422 Invalid price"));
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _eshop.Verify(c => c.SetPriceWithVatAsync("B", 310.00m, It.IsAny<CancellationToken>()), Times.Once);
        var failed = _savedStates.Single(s => s.ProductCode == "A" && s.Target == PriceSyncTarget.Shoptet);
        failed.Status.Should().Be(PriceSyncStatus.Failed);
        failed.LastError.Should().Contain("422");
    }

    [Fact]
    public async Task never_syncs_materials_or_semi_products()
    {
        // Arrange
        GivenInScope(("MAT001", ProductType.Material), ("SEMI001", ProductType.SemiProduct));
        _repository
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPrice>
            {
                new() { ProductCode = "MAT001", PriceWithVat = 10.00m, VatRate = 21m },
            });
        _repository
            .Setup(r => r.GetSyncStatesAsync(It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>());
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["SEMI001"] = 5.00m });
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _savedStates.Should().BeEmpty();
        _eshop.Verify(
            c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.Pushed.Should().Be(0);
        result.Seeded.Should().Be(0);
    }

    [Fact]
    public async Task leaves_states_untouched_when_the_bulk_read_of_a_target_fails()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        _savedStates.Should().NotContain(s => s.Target == PriceSyncTarget.Shoptet);
        result.Pushed.Should().Be(1); // Flexi still ran
    }
}
