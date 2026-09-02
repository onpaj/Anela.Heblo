using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryGetMaterialNamesByIdsAsyncTests
{
    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"MaterialNameLookup_{Guid.NewGuid()}")
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task ReturnsNamesOnlyForRequestedIds_AndOmitsUnmatchedIds()
    {
        // Arrange
        using var context = NewContext();
        var m1 = new PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("Box", 1m, ConsumptionType.PerDay, 100m);
        var m3 = new PackingMaterial("Label", 1m, ConsumptionType.PerDay, 100m);
        await context.PackingMaterials.AddRangeAsync(m1, m2, m3);
        await context.SaveChangesAsync();

        var repository = new PackingMaterialRepository(context);

        // Act: request m1, m3, and one id that does not exist. m2 is deliberately omitted.
        var missingId = m1.Id + m2.Id + m3.Id + 1000;
        var result = await repository.GetMaterialNamesByIdsAsync(new[] { m1.Id, m3.Id, missingId }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[m1.Id].Should().Be("Tape");
        result[m3.Id].Should().Be("Label");
        result.Should().NotContainKey(m2.Id);
        result.Should().NotContainKey(missingId);
    }

    [Fact]
    public async Task DuplicateIds_ReturnEachMaterialOnlyOnce()
    {
        // Arrange
        using var context = NewContext();
        var m1 = new PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        await context.PackingMaterials.AddRangeAsync(m1);
        await context.SaveChangesAsync();

        var repository = new PackingMaterialRepository(context);

        // Act
        var result = await repository.GetMaterialNamesByIdsAsync(new[] { m1.Id, m1.Id, m1.Id }, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[m1.Id].Should().Be("Tape");
    }

    [Fact]
    public async Task EmptyIds_ReturnsEmptyDictionary_WithoutQueryingTheDatabase()
    {
        // Arrange: dispose the context up front. If the implementation does not short-circuit
        // on an empty id collection, touching the disposed context will throw.
        var context = NewContext();
        var repository = new PackingMaterialRepository(context);
        context.Dispose();

        // Act
        var result = await repository.GetMaterialNamesByIdsAsync(Array.Empty<int>(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
