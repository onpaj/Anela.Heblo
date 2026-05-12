using Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreateAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeleteAllocation;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetAllocations;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdateAllocation;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class AllocationHandlerTests
{
    private static PackingMaterial MakeMaterial(int id, string name = "TestMaterial")
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial)
            .GetProperty("Id")!
            .SetValue(material, id);
        return material;
    }

    private static PackingMaterialAllocation MakeAllocation(int id, int materialId, string productCode, decimal amount)
    {
        var allocation = new PackingMaterialAllocation(materialId, productCode, amount);
        typeof(PackingMaterialAllocation)
            .GetProperty("Id")!
            .SetValue(allocation, id);
        return allocation;
    }

    private static void AddAllocationToMaterial(PackingMaterial material, PackingMaterialAllocation allocation)
    {
        var field = typeof(PackingMaterial).GetField("_allocations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (List<PackingMaterialAllocation>)field.GetValue(material)!;
        list.Add(allocation);
    }

    private static MockPackingMaterialRepository BuildMaterialRepo(params PackingMaterial[] materials)
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(materials);
        return repo;
    }

    // ---- GetAllocations ----

    [Fact]
    public async Task GetAllocations_ReturnsMappedDtos_WhenMaterialExists()
    {
        // Arrange
        var material = MakeMaterial(1);
        var allocation = MakeAllocation(10, 1, "PROD-001", 2.5m);
        AddAllocationToMaterial(material, allocation);

        var materialRepo = BuildMaterialRepo(material);
        var handler = new GetAllocationsHandler(materialRepo, new MockLogger<GetAllocationsHandler>());

        // Act
        var response = await handler.Handle(new GetAllocationsRequest { PackingMaterialId = 1 }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Single(response.Allocations);
        var dto = response.Allocations[0];
        Assert.Equal(10, dto.Id);
        Assert.Equal(1, dto.PackingMaterialId);
        Assert.Equal("PROD-001", dto.ProductCode);
        Assert.Equal(2.5m, dto.AmountPerUnit);
    }

    [Fact]
    public async Task GetAllocations_ReturnsError_WhenMaterialNotFound()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo();
        var handler = new GetAllocationsHandler(materialRepo, new MockLogger<GetAllocationsHandler>());

        // Act
        var response = await handler.Handle(new GetAllocationsRequest { PackingMaterialId = 99 }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
        Assert.Empty(response.Allocations);
    }

    [Fact]
    public async Task GetAllocations_ReturnsEmptyList_WhenNoAllocationsOnMaterial()
    {
        // Arrange
        var material = MakeMaterial(1);
        var materialRepo = BuildMaterialRepo(material);
        var handler = new GetAllocationsHandler(materialRepo, new MockLogger<GetAllocationsHandler>());

        // Act
        var response = await handler.Handle(new GetAllocationsRequest { PackingMaterialId = 1 }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Empty(response.Allocations);
    }

    // ---- CreateAllocation ----

    [Fact]
    public async Task CreateAllocation_PersistsAndReturnsDto_WhenValid()
    {
        // Arrange
        var material = MakeMaterial(1);
        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new CreateAllocationHandler(materialRepo, allocationRepo, new MockLogger<CreateAllocationHandler>());

        // Act
        var response = await handler.Handle(new CreateAllocationRequest
        {
            PackingMaterialId = 1,
            ProductCode = "PROD-A",
            AmountPerUnit = 3m
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Allocation);
        Assert.Equal("PROD-A", response.Allocation!.ProductCode);
        Assert.Equal(3m, response.Allocation.AmountPerUnit);
        Assert.Single(allocationRepo.AddedAllocations);
    }

    [Fact]
    public async Task CreateAllocation_ReturnsError_WhenAmountPerUnitIsZero()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo(MakeMaterial(1));
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new CreateAllocationHandler(materialRepo, allocationRepo, new MockLogger<CreateAllocationHandler>());

        // Act
        var response = await handler.Handle(new CreateAllocationRequest
        {
            PackingMaterialId = 1,
            ProductCode = "PROD-A",
            AmountPerUnit = 0m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("AmountPerUnit", response.Error);
        Assert.Empty(allocationRepo.AddedAllocations);
    }

    [Fact]
    public async Task CreateAllocation_ReturnsError_WhenProductCodeIsEmpty()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo(MakeMaterial(1));
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new CreateAllocationHandler(materialRepo, allocationRepo, new MockLogger<CreateAllocationHandler>());

        // Act
        var response = await handler.Handle(new CreateAllocationRequest
        {
            PackingMaterialId = 1,
            ProductCode = "  ",
            AmountPerUnit = 1m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("ProductCode", response.Error);
        Assert.Empty(allocationRepo.AddedAllocations);
    }

    [Fact]
    public async Task CreateAllocation_ReturnsError_WhenMaterialNotFound()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo();
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new CreateAllocationHandler(materialRepo, allocationRepo, new MockLogger<CreateAllocationHandler>());

        // Act
        var response = await handler.Handle(new CreateAllocationRequest
        {
            PackingMaterialId = 99,
            ProductCode = "PROD-A",
            AmountPerUnit = 1m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
        Assert.Empty(allocationRepo.AddedAllocations);
    }

    [Fact]
    public async Task CreateAllocation_ReturnsError_WhenDuplicateProductCode()
    {
        // Arrange
        var material = MakeMaterial(1);
        var existing = MakeAllocation(5, 1, "PROD-A", 1m);
        AddAllocationToMaterial(material, existing);

        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new CreateAllocationHandler(materialRepo, allocationRepo, new MockLogger<CreateAllocationHandler>());

        // Act
        var response = await handler.Handle(new CreateAllocationRequest
        {
            PackingMaterialId = 1,
            ProductCode = "PROD-A",
            AmountPerUnit = 2m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("PROD-A", response.Error);
        Assert.Empty(allocationRepo.AddedAllocations);
    }

    // ---- UpdateAllocation ----

    [Fact]
    public async Task UpdateAllocation_CallsUpdateAndSave_WhenValid()
    {
        // Arrange
        var material = MakeMaterial(1);
        var allocation = MakeAllocation(10, 1, "PROD-A", 1m);
        AddAllocationToMaterial(material, allocation);

        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new UpdateAllocationHandler(materialRepo, allocationRepo, new MockLogger<UpdateAllocationHandler>());

        // Act
        var response = await handler.Handle(new UpdateAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 10,
            ProductCode = "PROD-B",
            AmountPerUnit = 5m
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Single(allocationRepo.UpdatedAllocations);
        Assert.Equal("PROD-B", allocation.ProductCode);
        Assert.Equal(5m, allocation.AmountPerUnit);
    }

    [Fact]
    public async Task UpdateAllocation_ReturnsError_WhenAllocationNotFound()
    {
        // Arrange
        var material = MakeMaterial(1);
        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new UpdateAllocationHandler(materialRepo, allocationRepo, new MockLogger<UpdateAllocationHandler>());

        // Act
        var response = await handler.Handle(new UpdateAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 99,
            ProductCode = "PROD-A",
            AmountPerUnit = 1m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
        Assert.Empty(allocationRepo.UpdatedAllocations);
    }

    [Fact]
    public async Task UpdateAllocation_ReturnsError_WhenMaterialNotFound()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo();
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new UpdateAllocationHandler(materialRepo, allocationRepo, new MockLogger<UpdateAllocationHandler>());

        // Act
        var response = await handler.Handle(new UpdateAllocationRequest
        {
            PackingMaterialId = 99,
            AllocationId = 1,
            ProductCode = "PROD-A",
            AmountPerUnit = 1m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
    }

    [Fact]
    public async Task UpdateAllocation_ReturnsError_WhenProductCodeConflictsWithAnotherAllocation()
    {
        // Arrange
        var material = MakeMaterial(1);
        var allocationA = MakeAllocation(10, 1, "PROD-A", 1m);
        var allocationB = MakeAllocation(11, 1, "PROD-B", 2m);
        AddAllocationToMaterial(material, allocationA);
        AddAllocationToMaterial(material, allocationB);

        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new UpdateAllocationHandler(materialRepo, allocationRepo, new MockLogger<UpdateAllocationHandler>());

        // Act – try to rename allocation 11 (PROD-B) to PROD-A, which is taken by allocation 10
        var response = await handler.Handle(new UpdateAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 11,
            ProductCode = "PROD-A",
            AmountPerUnit = 3m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("PROD-A", response.Error);
        Assert.Empty(allocationRepo.UpdatedAllocations);
    }

    [Fact]
    public async Task UpdateAllocation_ReturnsError_WhenAmountIsNegative()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo(MakeMaterial(1));
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new UpdateAllocationHandler(materialRepo, allocationRepo, new MockLogger<UpdateAllocationHandler>());

        // Act
        var response = await handler.Handle(new UpdateAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 1,
            ProductCode = "PROD-A",
            AmountPerUnit = -1m
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("AmountPerUnit", response.Error);
    }

    // ---- DeleteAllocation ----

    [Fact]
    public async Task DeleteAllocation_CallsDeleteAndSave_WhenValid()
    {
        // Arrange
        var material = MakeMaterial(1);
        var allocation = MakeAllocation(10, 1, "PROD-A", 1m);
        AddAllocationToMaterial(material, allocation);

        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new DeleteAllocationHandler(materialRepo, allocationRepo, new MockLogger<DeleteAllocationHandler>());

        // Act
        var response = await handler.Handle(new DeleteAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 10
        }, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Single(allocationRepo.DeletedAllocations);
        Assert.Equal(10, allocationRepo.DeletedAllocations[0].Id);
    }

    [Fact]
    public async Task DeleteAllocation_ReturnsError_WhenMaterialNotFound()
    {
        // Arrange
        var materialRepo = BuildMaterialRepo();
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new DeleteAllocationHandler(materialRepo, allocationRepo, new MockLogger<DeleteAllocationHandler>());

        // Act
        var response = await handler.Handle(new DeleteAllocationRequest
        {
            PackingMaterialId = 99,
            AllocationId = 1
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
        Assert.Empty(allocationRepo.DeletedAllocations);
    }

    [Fact]
    public async Task DeleteAllocation_ReturnsError_WhenAllocationNotFoundOnMaterial()
    {
        // Arrange
        var material = MakeMaterial(1);
        var materialRepo = BuildMaterialRepo(material);
        var allocationRepo = new MockPackingMaterialAllocationRepository();
        var handler = new DeleteAllocationHandler(materialRepo, allocationRepo, new MockLogger<DeleteAllocationHandler>());

        // Act
        var response = await handler.Handle(new DeleteAllocationRequest
        {
            PackingMaterialId = 1,
            AllocationId = 99
        }, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
        Assert.Empty(allocationRepo.DeletedAllocations);
    }
}
