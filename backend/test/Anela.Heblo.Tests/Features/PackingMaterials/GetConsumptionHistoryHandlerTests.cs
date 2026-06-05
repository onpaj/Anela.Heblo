using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryHandlerTests
{
    private static PackingMaterial MakeMaterial(int id, string name)
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial).GetProperty("Id")!.SetValue(material, id);
        return material;
    }

    private static PackingMaterialConsumption MakeConsumption(int materialId, DateOnly date, decimal amount, string? invoiceId = null, string? productCode = null)
        => new(materialId, date, ConsumptionType.PerOrder, amount, invoiceId, productCode);

    private static PackingMaterialLog MakeLog(int materialId, DateOnly date, decimal oldQty, decimal newQty)
        => new(materialId, date, oldQty, newQty, LogEntryType.Manual);

    private static (GetConsumptionHistoryHandler handler, MockPackingMaterialRepository repo) Build()
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(new[] { MakeMaterial(1, "Tape") });
        var handler = new GetConsumptionHistoryHandler(repo, new MockLogger<GetConsumptionHistoryHandler>());
        return (handler, repo);
    }

    [Fact]
    public async Task Handle_ResolvesMaterialName_AndUnionsSources()
    {
        // Arrange
        var (handler, repo) = Build();
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(1, new DateOnly(2026, 1, 10), 5m, "INV-1") };
        repo.RecentLogsByMaterial[1] = new() { MakeLog(1, new DateOnly(2026, 1, 12), 100m, 90m) };

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.TotalCount.Should().Be(2);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(20);
        response.TotalPages.Should().Be(1);
        response.Items.Should().HaveCount(2);
        response.Items[0].RecordType.Should().Be(HistoryRecordType.QuantityChange);
        response.Items[0].RecordTypeText.Should().Be("Změna množství");
        response.Items[0].ChangeAmount.Should().Be(-10m);
        response.Items[1].RecordType.Should().Be(HistoryRecordType.Consumption);
        response.Items[1].MaterialName.Should().Be("Tape");
        response.Items[1].Amount.Should().Be(5m);
    }

    [Fact]
    public async Task Handle_ClampsPageSizeToMaximum()
    {
        // Arrange
        var (handler, _) = Build();

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest { PageSize = 5000 }, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_ConsumptionOnlyFilter_ExcludesLogs()
    {
        // Arrange
        var (handler, repo) = Build();
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(1, new DateOnly(2026, 1, 10), 5m, "INV-1") };
        repo.RecentLogsByMaterial[1] = new() { MakeLog(1, new DateOnly(2026, 1, 12), 100m, 90m) };

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest { InvoiceId = "INV-1" }, CancellationToken.None);

        // Assert
        response.TotalCount.Should().Be(1);
        response.Items.Should().ContainSingle().Which.RecordType.Should().Be(HistoryRecordType.Consumption);
    }

    [Fact]
    public async Task Handle_UnknownMaterial_FallsBackToPlaceholderName()
    {
        // Arrange
        var repo = new MockPackingMaterialRepository(); // no materials registered
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(99, new DateOnly(2026, 1, 10), 5m) };
        var handler = new GetConsumptionHistoryHandler(repo, new MockLogger<GetConsumptionHistoryHandler>());

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle().Which.MaterialName.Should().Be("Neznámý");
    }
}
