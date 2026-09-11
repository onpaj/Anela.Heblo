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

    /// <summary>
    /// Builds the ERP fixture the way production actually flows: from a without-VAT
    /// <c>cenaZakl</c> upward to the with-VAT price Flexi reconstructs on read
    /// (<c>cena * (100 + vat) / 100</c>). <see cref="GivenErp"/> instead builds backward from
    /// a with-VAT price, which round-trips exactly and so can never reproduce the Flexi
    /// rounding-drift bug this fixture exists to exercise.
    /// </summary>
    private void GivenErpFromCenaZakl(string code, int erpItemId, decimal cenaZakl, decimal vatRate = 21m) =>
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new()
                {
                    ProductCode = code,
                    ErpItemId = erpItemId,
                    PriceWithoutVat = cenaZakl,
                    PriceWithVat = Math.Round(cenaZakl * (100 + vatRate) / 100m, 2, MidpointRounding.AwayFromZero),
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
    public async Task seeds_the_master_row_from_flexi_when_present_only_in_flexi()
    {
        // Arrange: spec §7 — present in Flexi, absent from Heblo's master table and from
        // Shoptet. Before I3's fix, ReconcileErpSeedAsync wrote the Flexi state InSync
        // without ever creating the master row, so the next run's missing-master-row guard
        // marked it Failed forever and it never appeared in the grid (which iterates
        // ProductPrices). The Shoptet state must stay untouched: it never saw this product
        // this run (absent from both the master table and Shoptet's remote list).
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProductPrice>());
        _repository
            .Setup(r => r.GetSyncStatesAsync(It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceSyncState>());
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal>());
        GivenErp("A", erpItemId: 500, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Seeded.Should().Be(1);
        result.Failed.Should().Be(0);
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.ProductCode == "A" && p.PriceWithVat == 190.00m),
                               It.IsAny<CancellationToken>()),
            Times.Once);
        var flexiState = _savedStates.Single(s => s.ProductCode == "A" && s.Target == PriceSyncTarget.Flexi);
        flexiState.Status.Should().Be(PriceSyncStatus.InSync);
        flexiState.LastPushedPriceWithVat.Should().Be(190.00m);
        _savedStates.Should().NotContain(s => s.Target == PriceSyncTarget.Shoptet);
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

    [Fact]
    public async Task never_pushes_when_the_master_price_row_is_missing()
    {
        // Arrange: a sync state was already pushed once, but the master ProductPrice row
        // for "A" no longer exists. Without the guard, Decide would see a 0m Heblo price
        // and, since remote hasn't drifted from the last-pushed value, compute Push(0).
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProductPrice>());
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Failed.Should().Be(2);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        _savedStates.Should().OnlyContain(s => s.Status == PriceSyncStatus.Failed);
    }

    [Fact]
    public async Task refuses_to_push_a_non_positive_price()
    {
        // Arrange: a genuine zero master price with no remote drift forces Decide into
        // Push(0) directly — exercising the guard inside PushAsync itself, distinct from
        // the missing-master-row guard above.
        GivenHebloPrice("A", 0.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErp("A", erpItemId: 147, priceWithVat: 190.00m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Failed.Should().Be(2);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        _savedStates.Should().OnlyContain(s => s.LastError != null && s.LastError.Contains("non-positive"));
    }

    [Fact]
    public async Task an_erp_read_failure_skips_flexi_but_still_syncs_shoptet()
    {
        // Arrange
        GivenHebloPrice("A", 210.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Pushed.Should().Be(1);
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _savedStates.Should().NotContain(s => s.Target == PriceSyncTarget.Flexi);
    }

    [Fact]
    public async Task flexi_with_vat_round_trip_rounding_does_not_manufacture_a_conflict()
    {
        // Arrange: Heblo pushed 190.00, which was written to Flexi as cenaZakl 157.02
        // (190.00 / 1.21 rounded). Reading it back reconstructs 157.02 * 1.21 = 189.9942,
        // rounded to 189.99 — a haler short of the 190.00 that was actually pushed. Without
        // the Flexi round-trip tolerance this manufactures a permanent Conflict every run.
        GivenHebloPrice("A", 190.00m);
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: 190.00m);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: 190.00m);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        GivenErpFromCenaZakl("A", erpItemId: 147, cenaZakl: 157.02m);
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Conflicts.Should().Be(0);
        result.Pushed.Should().Be(0);
        _erpWriter.Verify(
            w => w.SetPriceWithoutVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Both targets land on PriceSyncAction.None (heblo == lastPushed and, with the
        // tolerance, remote == lastPushed too), which touches no state at all.
        _savedStates.Should().BeEmpty();
    }

    [Fact]
    public async Task an_erp_read_failure_defers_seeding()
    {
        // Arrange
        _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProductPrice>());
        GivenSyncState("A", PriceSyncTarget.Shoptet, lastPushed: null);
        GivenSyncState("A", PriceSyncTarget.Flexi, lastPushed: null);
        _eshop.Setup(c => c.GetPricesWithVatAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 190.00m });
        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));
        var service = CreateService();

        // Act
        var result = await service.SyncAsync(CancellationToken.None);

        // Assert
        result.Seeded.Should().Be(0);
        _repository.Verify(r => r.UpsertAsync(It.IsAny<ProductPrice>(), It.IsAny<CancellationToken>()), Times.Never);
        _savedStates.Should().BeEmpty();
    }
}
