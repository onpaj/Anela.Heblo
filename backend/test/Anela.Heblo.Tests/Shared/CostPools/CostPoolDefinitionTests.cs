using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolDefinitionTests
{
    [Theory]
    [InlineData("VYROBA")]
    [InlineData("vyroba")]
    [InlineData("Vyroba")]
    public void Resolve_MapsManufacturingDepartmentToM1(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M1);
    }

    [Theory]
    [InlineData("SKLAD")]
    [InlineData("MARKETING")]
    [InlineData("marketing")]
    public void Resolve_MapsWarehouseAndMarketingDepartmentsToM2(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M2);
    }

    [Theory]
    [InlineData("CENTRALA")]
    [InlineData("ESHOP")]
    [InlineData("something-nobody-has-seen-before")]
    public void Resolve_MapsUnrecognisedDepartmentToM3(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MapsMissingDepartmentToM3(string? department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M3);
    }

    [Fact]
    public void All_ListsEveryPoolExactlyOnce()
    {
        // Act
        var all = CostPoolDefinition.All;

        // Assert
        all.Should().BeEquivalentTo(new[] { CostPool.M1, CostPool.M2, CostPool.M3 });
        all.Should().OnlyHaveUniqueItems();
    }
}
