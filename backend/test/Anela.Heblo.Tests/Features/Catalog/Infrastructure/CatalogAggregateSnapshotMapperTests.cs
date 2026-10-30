using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Tests.Features.Manufacture.TestHelpers;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogAggregateSnapshotMapperTests
{
    [Fact]
    public void ToSnapshot_MapsScalarFields()
    {
        var aggregate = new CatalogAggregate
        {
            ProductCode = "ABC123XYZ",
            ProductName = "Krém Šípek",
            Type = ProductType.Product,
            MinimalManufactureQuantity = 12.5,
            NetWeight = 0.25,
            HasLots = true,
        };

        var snapshot = CatalogAggregateSnapshotMapper.ToSnapshot(aggregate);

        Assert.Equal("ABC123XYZ", snapshot.ProductCode);
        Assert.Equal("Krém Šípek", snapshot.ProductName);
        Assert.Equal(aggregate.ProductNameNormalized, snapshot.ProductNameNormalized);
        Assert.Equal(12.5, snapshot.MinimalManufactureQuantity);
        Assert.Equal(0.25, snapshot.NetWeight);
        Assert.True(snapshot.HasLots);
    }

    [Fact]
    public void ToSnapshot_MapsEveryProductType()
    {
        foreach (var type in Enum.GetValues<ProductType>())
        {
            var snapshot = CatalogAggregateSnapshotMapper.ToSnapshot(new CatalogAggregate { ProductCode = "X", Type = type });

            Assert.Equal(type.ToString(), snapshot.Type.ToString());
            Assert.Equal((int)type, (int)snapshot.Type);
        }
    }

    [Fact]
    public void ToSnapshot_UnknownProductType_Throws()
    {
        var aggregate = new CatalogAggregate { ProductCode = "X", Type = (ProductType)12345 };

        Assert.Throws<ArgumentOutOfRangeException>(() => CatalogAggregateSnapshotMapper.ToSnapshot(aggregate));
    }

    [Fact]
    public void ProductKind_HasSameMembersAsProductType()
    {
        Assert.Equal(
            Enum.GetNames<ProductType>().OrderBy(n => n),
            Enum.GetNames<ProductKind>().OrderBy(n => n));
    }

    [Fact]
    public void ToSnapshot_MapsStockAndPreservesDerivedValues()
    {
        var aggregate = new CatalogAggregate { ProductCode = "X" };
        aggregate.Stock.Erp = 10;
        aggregate.Stock.Eshop = 20;
        aggregate.Stock.Transport = 3;
        aggregate.Stock.Manufactured = 4;
        aggregate.Stock.Reserve = 5;
        aggregate.Stock.Quarantine = 6;
        aggregate.Stock.Ordered = 7;
        aggregate.Stock.Planned = 8;
        aggregate.Stock.PrimaryStockSource = StockSource.Eshop;

        var stock = CatalogAggregateSnapshotMapper.ToSnapshot(aggregate).Stock;

        Assert.Equal(StockSourceKind.Eshop, stock.PrimaryStockSource);
        Assert.Equal(aggregate.Stock.Available, stock.Available);
        Assert.Equal(aggregate.Stock.Total, stock.Total);
        Assert.Equal(aggregate.Stock.Planned, stock.Planned);
        Assert.Equal(aggregate.Stock.Quarantine, stock.Quarantine);
        Assert.Equal(aggregate.Stock.Ordered, stock.Ordered);
    }

    [Fact]
    public void ToSnapshot_MapsProperties_AndKeepsDefaults()
    {
        var defaults = CatalogAggregateSnapshotMapper.ToSnapshot(new CatalogAggregate { ProductCode = "X" }).Properties;
        Assert.Equal(12, defaults.ExpirationMonths);

        var aggregate = new CatalogAggregate { ProductCode = "X" };
        aggregate.Properties = aggregate.Properties with
        {
            OptimalStockDaysSetup = 30,
            StockMinSetup = 5,
            BatchSize = 100,
            ExpirationMonths = 24,
            AllowedResiduePercentage = 2.5,
            IsTopProduct = true,
        };

        var props = CatalogAggregateSnapshotMapper.ToSnapshot(aggregate).Properties;

        Assert.False(defaults.IsTopProduct);
        Assert.True(props.IsTopProduct);
        Assert.Equal(30, props.OptimalStockDaysSetup);
        Assert.Equal(5m, props.StockMinSetup);
        Assert.Equal(100, props.BatchSize);
        Assert.Equal(24, props.ExpirationMonths);
        Assert.Equal(2.5, props.AllowedResiduePercentage);
    }

    [Fact]
    public void ToSnapshot_MapsHistories()
    {
        var aggregate = new CatalogAggregate { ProductCode = "X" };
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new() { Date = new DateTime(2026, 1, 1), AmountB2B = 1, AmountB2C = 2 },
            new() { Date = new DateTime(2026, 2, 1), AmountB2B = 3, AmountB2C = 4 },
        };
        aggregate.ConsumedHistory = new List<ConsumedMaterialRecord>
        {
            new() { Date = new DateTime(2026, 3, 1), Amount = 9 },
        };
        aggregate.StockTakingHistory = new List<StockTakingRecord>
        {
            new() { Id = 7, Type = StockTakingType.Erp, Code = "X", AmountNew = 5, AmountOld = 4, Date = new DateTime(2026, 4, 1), User = "u", Error = "e" },
        };

        var snapshot = CatalogAggregateSnapshotMapper.ToSnapshot(aggregate);

        Assert.Equal(2, snapshot.SalesHistory.Count);
        Assert.Equal(new DateTime(2026, 2, 1), snapshot.SalesHistory[1].Date);
        Assert.Equal(3, snapshot.SalesHistory[1].AmountB2B);
        Assert.Equal(4, snapshot.SalesHistory[1].AmountB2C);
        Assert.Single(snapshot.ConsumedHistory);
        Assert.Equal(9, snapshot.ConsumedHistory[0].Amount);
        var st = Assert.Single(snapshot.StockTakingHistory);
        Assert.Equal(7, st.Id);
        Assert.Equal(StockTakingKind.Erp, st.Type);
        Assert.Equal("X", st.Code);
        Assert.Equal(5, st.AmountNew);
        Assert.Equal(4, st.AmountOld);
        Assert.Equal("u", st.User);
        Assert.Equal("e", st.Error);
    }

    [Fact]
    public void ToSnapshot_EmptyHistories_ProduceEmptyListsAndNullLastStockTaking()
    {
        var snapshot = CatalogAggregateSnapshotMapper.ToSnapshot(new CatalogAggregate { ProductCode = "X" });

        Assert.Empty(snapshot.SalesHistory);
        Assert.Empty(snapshot.ConsumedHistory);
        Assert.Empty(snapshot.StockTakingHistory);
        Assert.Null(snapshot.LastStockTaking);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("ABC")]
    [InlineData("ABCDEF")]
    [InlineData("ABCDEFGHIJ")]
    public void DerivedMembers_EqualCatalogAggregateValues(string code)
    {
        var aggregate = new CatalogAggregate { ProductCode = code };
        aggregate.StockTakingHistory = new List<StockTakingRecord>
        {
            new() { Id = 1, Code = code, Date = new DateTime(2026, 1, 1) },
            new() { Id = 2, Code = code, Date = new DateTime(2026, 5, 1) },
            new() { Id = 3, Code = code, Date = new DateTime(2026, 3, 1) },
        };

        var snapshot = CatalogAggregateSnapshotMapper.ToSnapshot(aggregate);

        Assert.Equal(aggregate.ProductFamily, snapshot.ProductFamily);
        Assert.Equal(aggregate.SizeCode, snapshot.SizeCode);
        Assert.Equal(aggregate.LastStockTaking, snapshot.LastStockTaking);
    }

    [Theory]
    [InlineData(StockTakingKind.Erp, StockTakingType.Erp)]
    [InlineData(StockTakingKind.Eshop, StockTakingType.Eshop)]
    public void ToDomain_MapsStockTakingSnapshot(StockTakingKind kind, StockTakingType expected)
    {
        var snapshot = new ProductStockTakingSnapshot
        {
            Id = 3,
            Type = kind,
            Code = "X",
            AmountNew = 2,
            AmountOld = 1,
            Date = new DateTime(2026, 1, 2),
            User = "u",
            Error = "e",
        };

        var record = CatalogAggregateSnapshotMapper.ToDomain(snapshot);

        Assert.Equal(3, record.Id);
        Assert.Equal(expected, record.Type);
        Assert.Equal("X", record.Code);
        Assert.Equal(2, record.AmountNew);
        Assert.Equal(1, record.AmountOld);
        Assert.Equal(new DateTime(2026, 1, 2), record.Date);
        Assert.Equal("u", record.User);
        Assert.Equal("e", record.Error);
    }

    [Fact]
    public void ToDomain_UnknownKind_Throws()
    {
        var snapshot = new ProductStockTakingSnapshot { Type = (StockTakingKind)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() => CatalogAggregateSnapshotMapper.ToDomain(snapshot));
    }

    [Fact]
    public void Builder_DefaultsMatchMappedDefaultAggregate()
    {
        var fromBuilder = new ProductCatalogSnapshotBuilder().WithProductCode("TEST001").Build();
        var fromAggregate = CatalogAggregateSnapshotMapper.ToSnapshot(new CatalogAggregate { ProductCode = "TEST001" });

        Assert.Equal(fromAggregate.Type, fromBuilder.Type);
        Assert.Equal(fromAggregate.ProductName, fromBuilder.ProductName);
        Assert.Equal(fromAggregate.Properties.ExpirationMonths, fromBuilder.Properties.ExpirationMonths);
        Assert.Equal(fromAggregate.Stock.Available, fromBuilder.Stock.Available);
        Assert.Equal(fromAggregate.HasLots, fromBuilder.HasLots);
        Assert.Equal(fromAggregate.NetWeight, fromBuilder.NetWeight);
    }
}
