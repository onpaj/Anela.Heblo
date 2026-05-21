using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

internal sealed class StubCurrentUserService : Anela.Heblo.Domain.Features.Users.ICurrentUserService
{
    public Anela.Heblo.Domain.Features.Users.CurrentUser GetCurrentUser()
        => new Anela.Heblo.Domain.Features.Users.CurrentUser("test-user-id", "Test User", "test@example.com", true);

    public bool IsInRole(string role) => false;
}

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

    // ---- UpdatePackingMaterialQuantity ----

    [Fact]
    public async Task UpdatePackingMaterialQuantity_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
    {
        // Arrange
        var repo = BuildRepo();
        var handler = new UpdatePackingMaterialQuantityHandler(repo, new StubCurrentUserService());

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialQuantityRequest
        {
            Id = 99,
            NewQuantity = 5m,
            Date = DateOnly.FromDateTime(DateTime.UtcNow)
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.NotNull(response.Error);
        Assert.Contains("99", response.Error!);
    }

    [Fact]
    public async Task UpdatePackingMaterialQuantity_UpdatesAndReturnsSuccess_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1);
        var repo = BuildRepo(material);
        var handler = new UpdatePackingMaterialQuantityHandler(repo, new StubCurrentUserService());

        // Act
        var response = await handler.Handle(new UpdatePackingMaterialQuantityRequest
        {
            Id = 1,
            NewQuantity = 42m,
            Date = DateOnly.FromDateTime(DateTime.UtcNow)
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Error);
        Assert.NotNull(response.Material);
        Assert.Equal(1, response.Material.Id);
        Assert.Single(repo.UpdatedMaterials);
    }

    // ---- GetPackingMaterialLogs ----

    [Fact]
    public async Task GetPackingMaterialLogs_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
    {
        // Arrange
        var repo = BuildRepo();
        var handler = new GetPackingMaterialLogsHandler(repo);

        // Act
        var response = await handler.Handle(new GetPackingMaterialLogsRequest
        {
            PackingMaterialId = 99,
            Days = 30
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.NotNull(response.Error);
        Assert.Contains("99", response.Error!);
    }

    [Fact]
    public async Task GetPackingMaterialLogs_ReturnsSuccess_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1);
        var repo = BuildRepo(material);
        var handler = new GetPackingMaterialLogsHandler(repo);

        // Act
        var response = await handler.Handle(new GetPackingMaterialLogsRequest
        {
            PackingMaterialId = 1,
            Days = 30
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Error);
        Assert.NotNull(response.Material);
        Assert.Equal(1, response.Material.Id);
        Assert.Empty(response.Logs);
    }

    // ---- DeletePackingMaterial ----

    [Fact]
    public async Task DeletePackingMaterial_ReturnsNotFoundResponse_WhenMaterialDoesNotExist()
    {
        // Arrange
        var repo = BuildRepo();
        var handler = new DeletePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new DeletePackingMaterialRequest { Id = 99 }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ResourceNotFound, response.ErrorCode);
        Assert.NotNull(response.Error);
        Assert.Contains("99", response.Error!);
    }

    [Fact]
    public async Task DeletePackingMaterial_DeletesAndReturnsSuccess_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1);
        var repo = BuildRepo(material);
        var handler = new DeletePackingMaterialHandler(repo);

        // Act
        var response = await handler.Handle(new DeletePackingMaterialRequest { Id = 1 }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.Error);
        Assert.Empty(repo.Materials);
    }
}
