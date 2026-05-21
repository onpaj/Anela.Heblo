using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialCrudHandlerTests
{
    private static PackingMaterial MakeMaterial(int id, string name = "TestMaterial")
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    private static MockPackingMaterialRepository BuildRepo(params PackingMaterial[] materials)
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(materials);
        return repo;
    }

    // ---- UpdatePackingMaterial ----

    [Fact]
    public async Task UpdatePackingMaterial_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
    {
        // Arrange
        var repo = BuildRepo();
        var handler = new UpdatePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialRequest
        {
            Id = 99,
            Name = "Anything",
            ConsumptionRate = 1m,
            ConsumptionType = ConsumptionType.PerOrder
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.NotNull(response.Error);
        Assert.Contains("99", response.Error!);
    }

    [Fact]
    public async Task UpdatePackingMaterial_UpdatesMaterialAndReturnsSuccess_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1, "Old");
        var repo = BuildRepo(material);
        var handler = new UpdatePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialRequest
        {
            Id = 1,
            Name = "New",
            ConsumptionRate = 2m,
            ConsumptionType = ConsumptionType.PerProduct
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Error);
        Assert.NotNull(response.Material);
        Assert.Equal(1, response.Material.Id);
        Assert.Equal("New", response.Material.Name);
        Assert.Single(repo.UpdatedMaterials);
    }
}
