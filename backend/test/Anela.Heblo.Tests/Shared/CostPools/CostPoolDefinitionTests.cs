using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolDefinitionTests
{
    private const string ServicesAccount = "518100";
    private const string PersonnelAccount = "521100";
    private const string ConsumablesAccount = "501001";
    private const string OutOfScopeAccount = "601000";

    [Theory]
    [InlineData("VYROBA")]
    [InlineData("vyroba")]
    [InlineData("Vyroba")]
    public void Resolve_MapsManufacturingDepartmentToM1(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department, ServicesAccount);

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
        var pool = CostPoolDefinition.Resolve(department, ServicesAccount);

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
        var pool = CostPoolDefinition.Resolve(department, ServicesAccount);

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
        var pool = CostPoolDefinition.Resolve(department, ServicesAccount);

        // Assert
        pool.Should().Be(CostPool.M3);
    }

    [Theory]
    [InlineData("BUVOL")]
    [InlineData("buvol")]
    public void Resolve_ExcludesSeparateActivityFromEveryPool_IncludingTheM3CatchAll(string department)
    {
        // A separate activity sharing the ledger is not Anela overhead. Unlike an
        // unmapped cost centre it must not be absorbed by M3.
        CostPoolDefinition.Resolve(department, ServicesAccount).Should().BeNull();
        CostPoolDefinition.Resolve(department, PersonnelAccount).Should().BeNull();
        CostPoolDefinition.Resolve(department, ConsumablesAccount).Should().BeNull();
    }

    [Theory]
    [InlineData("SKLAD")]
    [InlineData("MARKETING")]
    public void Resolve_CountsConsumablesInsideM2(string department)
    {
        // 50x here is shipping packaging and marketing print.
        var pool = CostPoolDefinition.Resolve(department, ConsumablesAccount);

        // Assert
        pool.Should().Be(CostPool.M2);
    }

    [Theory]
    [InlineData("VYROBA")]
    [InlineData("CENTRALA")]
    [InlineData(null)]
    public void Resolve_IgnoresConsumablesOutsideM2(string? department)
    {
        // Outside the warehouse and marketing the same prefix is cost of goods sold,
        // an order of magnitude larger than every pool combined.
        var pool = CostPoolDefinition.Resolve(department, ConsumablesAccount);

        // Assert
        pool.Should().BeNull();
    }

    [Theory]
    [InlineData("SKLAD")]
    [InlineData("VYROBA")]
    [InlineData("CENTRALA")]
    public void Resolve_IgnoresAccountsNoPoolCounts(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department, OutOfScopeAccount);

        // Assert
        pool.Should().BeNull();
    }

    [Fact]
    public void Resolve_IgnoresEntryWithNoDebitAccount()
    {
        // Act
        var pool = CostPoolDefinition.Resolve("SKLAD", null);

        // Assert
        pool.Should().BeNull();
    }

    [Fact]
    public void AccountPrefixesFor_GivesM2TheWiderSet()
    {
        // Assert
        CostPoolDefinition.AccountPrefixesFor(CostPool.M2).Should().BeEquivalentTo(new[] { "50", "51", "52" });
        CostPoolDefinition.AccountPrefixesFor(CostPool.M1).Should().BeEquivalentTo(new[] { "51", "52" });
        CostPoolDefinition.AccountPrefixesFor(CostPool.M3).Should().BeEquivalentTo(new[] { "51", "52" });
    }

    [Fact]
    public void AccountPrefixes_CoversEveryPrefixAnyPoolCounts()
    {
        // The single ledger pull must be a superset of every pool's own set,
        // otherwise a pool silently loses spend it is entitled to.
        foreach (var pool in CostPoolDefinition.All)
        {
            CostPoolDefinition.AccountPrefixes
                .Should().Contain(CostPoolDefinition.AccountPrefixesFor(pool));
        }
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
