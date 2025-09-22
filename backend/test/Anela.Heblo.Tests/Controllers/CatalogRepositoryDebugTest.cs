using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

/// <summary>
/// Debug test to verify that our catalog repository mock is working correctly
/// </summary>
public class CatalogRepositoryDebugTest : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;

    public CatalogRepositoryDebugTest(ManufactureOrderTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CatalogRepository_Should_Return_Test_Data()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var catalogRepository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        // Act
        var semi001 = await catalogRepository.GetByIdAsync("SEMI001");
        var prod001 = await catalogRepository.GetByIdAsync("PROD001");
        var allItems = await catalogRepository.GetAllAsync();

        // Assert
        semi001.Should().NotBeNull();
        semi001!.ProductName.Should().Be("Test Semi Product");
        semi001.Type.Should().Be(ProductType.Material);

        prod001.Should().NotBeNull();
        prod001!.ProductName.Should().Be("Test Final Product");
        prod001.Type.Should().Be(ProductType.Product);

        allItems.Should().HaveCount(4);
    }
}