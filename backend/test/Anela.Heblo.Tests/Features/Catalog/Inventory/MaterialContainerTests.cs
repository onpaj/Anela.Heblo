using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerTests
{
    [Fact]
    public void CreateUnassigned_SetsUnassignedStatus_WithNoMaterialOrLot()
    {
        var c = MaterialContainer.CreateUnassigned("M00000001", "tester");

        c.Status.Should().Be(MaterialContainerStatus.Unassigned);
        c.Code.Should().Be("M00000001");
        c.MaterialCode.Should().BeNull();
        c.LotCode.Should().BeNull();
        c.CreatedBy.Should().Be("tester");
    }

    [Fact]
    public void Assign_FillsMaterialAndLot_AndFlipsToAssigned()
    {
        var c = MaterialContainer.CreateUnassigned("M00000002", "tester");

        c.Assign("MAT-1", "LOT-9", amount: null, unit: null, purchaseOrderLineId: 5, updatedBy: "worker");

        c.Status.Should().Be(MaterialContainerStatus.Assigned);
        c.MaterialCode.Should().Be("MAT-1");
        c.LotCode.Should().Be("LOT-9");
        c.PurchaseOrderLineId.Should().Be(5);
        c.UpdatedBy.Should().Be("worker");
        c.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Assign_Throws_WhenNotUnassigned()
    {
        var c = MaterialContainer.CreateUnassigned("M00000003", "tester");
        c.Assign("MAT-1", "LOT-9", null, null, null, "worker");

        var act = () => c.Assign("MAT-2", "LOT-8", null, null, null, "worker");

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Assign_Throws_WhenAmountNotPositive(decimal amount)
    {
        var c = MaterialContainer.CreateUnassigned("M00000004", "tester");

        var act = () => c.Assign("MAT", "LOT", amount: amount, unit: null, purchaseOrderLineId: null, updatedBy: "u");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assign_Throws_WhenMaterialCodeBlank()
    {
        var c = MaterialContainer.CreateUnassigned("M00000005", "tester");

        var act = () => c.Assign("", "LOT", amount: null, unit: null, purchaseOrderLineId: null, updatedBy: "u");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assign_Throws_WhenLotCodeBlank()
    {
        var c = MaterialContainer.CreateUnassigned("M00000006", "tester");

        var act = () => c.Assign("MAT", "", amount: null, unit: null, purchaseOrderLineId: null, updatedBy: "u");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assign_Throws_WhenUpdatedByBlank()
    {
        var c = MaterialContainer.CreateUnassigned("M00000007", "tester");

        var act = () => c.Assign("MAT", "LOT", amount: null, unit: null, purchaseOrderLineId: null, updatedBy: "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateUnassigned_Throws_WhenCodeBlank()
    {
        var act = () => MaterialContainer.CreateUnassigned("", "tester");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateUnassigned_Throws_WhenCreatedByBlank()
    {
        var act = () => MaterialContainer.CreateUnassigned("M00000001", "");

        act.Should().Throw<ArgumentException>();
    }
}
