using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class PurchaseMaterialCatalogAdapterTests
{
    private readonly Mock<ICatalogRepository> _repository = new();

    private PurchaseMaterialCatalogAdapter CreateAdapter() => new(_repository.Object);

    private static CatalogAggregate MakeMaterial(
        string productCode,
        string productName = "Material",
        ProductType type = ProductType.Material,
        string? supplierName = null,
        decimal stockMinSetup = 0m,
        int optimalStockDaysSetup = 0,
        string minimalOrderQuantity = "",
        decimal erpStock = 0m,
        decimal ordered = 0m,
        bool hasBoM = false,
        int? bomId = null,
        string? note = null)
    {
        var aggregate = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productName,
            Type = type,
            SupplierName = supplierName,
            MinimalOrderQuantity = minimalOrderQuantity,
            Note = note,
            Stock = new StockData
            {
                Erp = erpStock,
                Ordered = ordered,
                PrimaryStockSource = StockSource.Erp,
            },
            Properties = new CatalogProperties
            {
                StockMinSetup = stockMinSetup,
                OptimalStockDaysSetup = optimalStockDaysSetup,
            },
        };

        if (hasBoM)
        {
            aggregate.ErpPrice = new ProductPriceErp
            {
                BoMId = bomId,
            };
        }

        return aggregate;
    }

    [Fact]
    public async Task GetByIdAsync_delegates_to_repository_and_projects_to_material_info()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetByIdAsync("MAT-1", ct))
            .ReturnsAsync(MakeMaterial("MAT-1", "First", note: "n1", hasBoM: true, bomId: 42));

        var adapter = CreateAdapter();

        var result = await adapter.GetByIdAsync("MAT-1", ct);

        result.Should().NotBeNull();
        result!.ProductCode.Should().Be("MAT-1");
        result.ProductName.Should().Be("First");
        result.Note.Should().Be("n1");
        result.HasBoM.Should().BeTrue();
        result.BoMId.Should().Be(42);

        _repository.Verify(r => r.GetByIdAsync("MAT-1", ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_repository_returns_null()
    {
        _repository
            .Setup(r => r.GetByIdAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        var result = await CreateAdapter().GetByIdAsync("MISSING", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdsAsync_delegates_to_bulk_repository_method_once_and_keys_by_product_code()
    {
        var ct = CancellationToken.None;
        var ids = new[] { "MAT-1", "MAT-2" };

        var dictionary = new Dictionary<string, CatalogAggregate>(StringComparer.Ordinal)
        {
            ["MAT-1"] = MakeMaterial("MAT-1", "First"),
            ["MAT-2"] = MakeMaterial("MAT-2", "Second"),
        };

        _repository
            .Setup(r => r.GetByIdsAsync(ids, ct))
            .ReturnsAsync(dictionary);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Should().HaveCount(2);
        result["MAT-1"].ProductName.Should().Be("First");
        result["MAT-2"].ProductName.Should().Be("Second");

        _repository.Verify(r => r.GetByIdsAsync(ids, ct), Times.Once);
    }

    [Fact]
    public async Task GetByIdsAsync_omits_ids_that_are_missing_from_the_repository_result()
    {
        var ct = CancellationToken.None;
        var ids = new[] { "MAT-1", "MISSING" };

        var dictionary = new Dictionary<string, CatalogAggregate>(StringComparer.Ordinal)
        {
            ["MAT-1"] = MakeMaterial("MAT-1", "First"),
        };

        _repository
            .Setup(r => r.GetByIdsAsync(ids, ct))
            .ReturnsAsync(dictionary);

        var result = await CreateAdapter().GetByIdsAsync(ids, ct);

        result.Keys.Should().BeEquivalentTo(new[] { "MAT-1" });
    }

    [Fact]
    public async Task GetAllAsync_projects_every_catalog_item_to_material_info()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("A", "Apple"),
                MakeMaterial("B", "Banana"),
            });

        var result = await CreateAdapter().GetAllAsync(ct);

        result.Select(m => m.ProductCode).Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_filters_to_material_and_goods()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("MAT-1", "Material 1", ProductType.Material),
                MakeMaterial("GOODS-1", "Goods 1", ProductType.Goods),
                MakeMaterial("PROD-1", "Product 1", ProductType.Product),
                MakeMaterial("SEMI-1", "Semiproduct 1", ProductType.SemiProduct),
                MakeMaterial("SET-1", "Set 1", ProductType.Set),
                MakeMaterial("UNDEF-1", "Undef 1", ProductType.UNDEFINED),
            });

        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct);

        result.Select(s => s.ProductCode).Should().BeEquivalentTo(new[] { "MAT-1", "GOODS-1" });
        result.Should().OnlyContain(s =>
            s.ProductType == MaterialProductType.Material || s.ProductType == MaterialProductType.Goods);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_projects_stock_and_properties()
    {
        var ct = CancellationToken.None;
        var material = MakeMaterial(
            productCode: "MAT-1",
            productName: "Krém",
            type: ProductType.Material,
            supplierName: "ACME",
            stockMinSetup: 5m,
            optimalStockDaysSetup: 30,
            minimalOrderQuantity: "100",
            erpStock: 10m,
            ordered: 7m);

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ct)).Single();

        snapshot.ProductCode.Should().Be("MAT-1");
        snapshot.ProductName.Should().Be("Krém");
        snapshot.ProductNameNormalized.Should().Be(material.ProductNameNormalized);
        snapshot.ProductType.Should().Be(MaterialProductType.Material);
        snapshot.SupplierName.Should().Be("ACME");
        snapshot.MinimalOrderQuantity.Should().Be("100");
        snapshot.IsMinStockConfigured.Should().BeTrue();
        snapshot.IsOptimalStockConfigured.Should().BeTrue();
        snapshot.Stock.Available.Should().Be(material.Stock.Available);
        snapshot.Stock.Ordered.Should().Be(7m);
        snapshot.Stock.EffectiveStock.Should().Be(material.Stock.EffectiveStock);
        snapshot.StockMinSetup.Should().Be(5m);
        snapshot.OptimalStockDaysSetup.Should().Be(30);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_consumption_uses_GetConsumed_for_material()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.ConsumedHistory = new List<ConsumedMaterialRecord>
        {
            new ConsumedMaterialRecord { Date = new DateTime(2025, 6, 10), Amount = 3 },
            new ConsumedMaterialRecord { Date = new DateTime(2025, 6, 20), Amount = 4 },
            new ConsumedMaterialRecord { Date = new DateTime(2025, 5, 31), Amount = 99 }, // before range
            new ConsumedMaterialRecord { Date = new DateTime(2025, 7, 1),  Amount = 99 }, // after range
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.ConsumptionInPeriod.Should().Be(material.GetConsumed(from, to));
        snapshot.ConsumptionInPeriod.Should().Be(7.0);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_consumption_uses_GetTotalSold_for_goods()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var goods = MakeMaterial("GOODS-1", "Goods", ProductType.Goods);
        goods.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 10), AmountB2B = 5, AmountB2C = 3 },
            new CatalogSaleRecord { Date = new DateTime(2025, 6, 20), AmountB2B = 1, AmountB2C = 2 },
            new CatalogSaleRecord { Date = new DateTime(2025, 7, 1),  AmountB2B = 99, AmountB2C = 99 }, // after
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { goods });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.ConsumptionInPeriod.Should().Be(goods.GetTotalSold(from, to));
        snapshot.ConsumptionInPeriod.Should().Be(11.0);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_last_purchase_is_most_recent_record()
    {
        var ct = CancellationToken.None;
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2025, 3, 1), SupplierName = "Old Supplier",
                Amount = 10, PricePerPiece = 1m, PriceTotal = 10m,
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2025, 9, 1), SupplierName = "New Supplier",
                Amount = 20, PricePerPiece = 2m, PriceTotal = 40m,
            },
        };

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(from, to, ct)).Single();

        snapshot.LastPurchase.Should().NotBeNull();
        snapshot.LastPurchase!.Date.Should().Be(new DateTime(2025, 9, 1));
        snapshot.LastPurchase.SupplierName.Should().Be("New Supplier");
        snapshot.LastPurchase.Amount.Should().Be(20m);
        snapshot.LastPurchase.UnitPrice.Should().Be(2m);
        snapshot.LastPurchase.TotalPrice.Should().Be(40m);
    }

    [Fact]
    public async Task GetStockAnalysisSnapshotsAsync_last_purchase_is_null_when_history_is_empty()
    {
        var ct = CancellationToken.None;
        var material = MakeMaterial("MAT-1", "Material", ProductType.Material);
        material.PurchaseHistory = new List<CatalogPurchaseRecord>();

        _repository.Setup(r => r.GetAllAsync(ct)).ReturnsAsync(new[] { material });

        var snapshot = (await CreateAdapter().GetStockAnalysisSnapshotsAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ct)).Single();

        snapshot.LastPurchase.Should().BeNull();
    }

    [Fact]
    public async Task GetMaterialsWithBomAsync_filters_by_hasBoM_and_bomId_present()
    {
        var ct = CancellationToken.None;
        _repository
            .Setup(r => r.GetAllAsync(ct))
            .ReturnsAsync(new[]
            {
                MakeMaterial("WITH-BOM", "With BoM", hasBoM: true, bomId: 7),
                MakeMaterial("BOM-NO-ID", "BoM but no Id", hasBoM: true, bomId: null),
                MakeMaterial("NO-BOM", "No BoM", hasBoM: false),
            });

        var result = await CreateAdapter().GetMaterialsWithBomAsync(ct);

        result.Should().ContainSingle();
        result[0].ProductCode.Should().Be("WITH-BOM");
        result[0].BoMId.Should().Be(7);
    }
}
