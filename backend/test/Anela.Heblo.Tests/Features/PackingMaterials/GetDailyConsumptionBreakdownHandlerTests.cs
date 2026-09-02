using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetDailyConsumptionBreakdown;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetDailyConsumptionBreakdownHandlerTests
{
    private static readonly DateOnly TestDate = new DateOnly(2025, 6, 15);

    private static PackingMaterial MakeMaterial(int id, string name)
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    private static PackingMaterialConsumption MakeConsumption(
        int packingMaterialId,
        decimal amount,
        string? invoiceId = null,
        string? productCode = null)
    {
        return new PackingMaterialConsumption(
            packingMaterialId,
            TestDate,
            ConsumptionType.PerOrder,
            amount,
            invoiceId,
            productCode);
    }

    private static MockPackingMaterialRepository BuildRepo(
        IEnumerable<PackingMaterial> materials,
        IEnumerable<PackingMaterialConsumption> consumptions)
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(materials);
        repo.ConsumptionRowsByDate[TestDate] = consumptions.ToList();
        return repo;
    }

    private static GetDailyConsumptionBreakdownHandler BuildHandler(MockPackingMaterialRepository repo)
    {
        return new GetDailyConsumptionBreakdownHandler(repo, new MockLogger<GetDailyConsumptionBreakdownHandler>());
    }

    [Fact]
    public async Task GroupByMaterial_ReturnsGroupedByMaterialId()
    {
        // Arrange
        var materialA = MakeMaterial(1, "Tape");
        var materialB = MakeMaterial(2, "Box");

        var consumptions = new[]
        {
            MakeConsumption(1, 5m, invoiceId: "INV-1"),
            MakeConsumption(1, 3m, invoiceId: "INV-2"),
            MakeConsumption(2, 10m, invoiceId: "INV-3"),
        };

        var repo = BuildRepo(new[] { materialA, materialB }, consumptions);
        var handler = BuildHandler(repo);

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(2, response.Groups.Count);

        var first = response.Groups[0];
        Assert.Equal("2", first.Key);
        Assert.Equal("Box", first.Label);
        Assert.Equal(10m, first.TotalAmount);
        Assert.Equal(1, first.RowCount);

        var second = response.Groups[1];
        Assert.Equal("1", second.Key);
        Assert.Equal("Tape", second.Label);
        Assert.Equal(8m, second.TotalAmount);
        Assert.Equal(2, second.RowCount);

        Assert.Equal(2, second.Details.Count);
        var detailKeys = second.Details.Select(d => d.Key).ToHashSet();
        Assert.Contains("INV-1", detailKeys);
        Assert.Contains("INV-2", detailKeys);
    }

    [Fact]
    public async Task GroupByOrder_ReturnsGroupedByInvoiceId()
    {
        // Arrange
        var materialA = MakeMaterial(1, "Tape");
        var materialB = MakeMaterial(2, "Box");

        var consumptions = new[]
        {
            MakeConsumption(1, 5m, invoiceId: "INV-1"),
            MakeConsumption(2, 3m, invoiceId: "INV-1"),
            MakeConsumption(1, 10m, invoiceId: "INV-2"),
        };

        var repo = BuildRepo(new[] { materialA, materialB }, consumptions);
        var handler = BuildHandler(repo);

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Order },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(2, response.Groups.Count);

        var inv2Group = response.Groups[0];
        Assert.Equal("INV-2", inv2Group.Key);
        Assert.Equal("INV-2", inv2Group.Label);
        Assert.Equal(10m, inv2Group.TotalAmount);
        Assert.Equal(1, inv2Group.RowCount);

        var inv1Group = response.Groups[1];
        Assert.Equal("INV-1", inv1Group.Key);
        Assert.Equal(8m, inv1Group.TotalAmount);
        Assert.Equal(2, inv1Group.RowCount);

        Assert.Equal(2, inv1Group.Details.Count);
        var detailKeys = inv1Group.Details.Select(d => d.Key).ToHashSet();
        Assert.Contains("1", detailKeys);
        Assert.Contains("2", detailKeys);
    }

    [Fact]
    public async Task GroupByProduct_ReturnsEmptyGroups_WhenProductCodeIsNull()
    {
        // Arrange
        var material = MakeMaterial(1, "Tape");
        var consumptions = new[]
        {
            MakeConsumption(1, 5m, invoiceId: "INV-1", productCode: null),
            MakeConsumption(1, 3m, invoiceId: "INV-2", productCode: null),
        };

        var repo = BuildRepo(new[] { material }, consumptions);
        var handler = BuildHandler(repo);

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Product },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Empty(response.Groups);
    }

    [Fact]
    public async Task GroupBy_OutOfRangeEnumValue_ReturnsFailureResponse()
    {
        // Arrange: an out-of-range enum value can only occur via an unchecked cast — ASP.NET Core's
        // model binder can never produce one for a real HTTP request, but the handler's switch must
        // still fail (not silently succeed) if it ever receives one, e.g. from a future internal caller.
        // The discard arm of the switch throws ArgumentOutOfRangeException, which Handle's surrounding
        // try/catch converts into a Success=false response rather than letting it propagate.
        var repo = BuildRepo(Array.Empty<PackingMaterial>(), new[] { MakeConsumption(1, 5m, invoiceId: "INV-1") });
        var handler = BuildHandler(repo);
        var outOfRangeGroupBy = (ConsumptionGroupBy)99;

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = outOfRangeGroupBy },
            CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public async Task GroupByMaterial_ExcludesPerDayRowsFromDetails()
    {
        // Arrange: 1 PerDay row (InvoiceId=null) + 1 PerOrder row (InvoiceId=INV-1) for same material
        var material = MakeMaterial(1, "Tape");

        var perDayRow = new PackingMaterialConsumption(1, TestDate, ConsumptionType.PerDay, 5m);
        var perOrderRow = new PackingMaterialConsumption(1, TestDate, ConsumptionType.PerOrder, 3m, invoiceId: "INV-1");

        var repo = BuildRepo(new[] { material }, new[] { perDayRow, perOrderRow });
        var handler = BuildHandler(repo);

        // Act
        var response = await handler.Handle(
            new GetDailyConsumptionBreakdownRequest { Date = TestDate, GroupBy = ConsumptionGroupBy.Material },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Single(response.Groups);

        var group = response.Groups[0];
        Assert.Equal(8m, group.TotalAmount);
        Assert.Equal(2, group.RowCount);

        Assert.Single(group.Details);
        Assert.Equal("INV-1", group.Details[0].Key);
        Assert.Equal(3m, group.Details[0].Amount);
    }
}
