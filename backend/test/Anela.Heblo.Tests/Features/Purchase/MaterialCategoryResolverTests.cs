using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Features.Purchase.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class MaterialCategoryResolverTests
{
    [Theory]
    [InlineData("ETI001", true)]
    [InlineData("eti001", true)]
    [InlineData("ETIKETA-X", true)]
    [InlineData("VIC001", false)]
    [InlineData("LAH001", false)]
    [InlineData("KEL001", false)]
    [InlineData("UZA001", false)]
    [InlineData("MAT001", false)]
    [InlineData("", false)]
    public void Matches_Labels_AcceptsOnlyEtiPrefix(string productCode, bool expected)
    {
        MaterialCategoryResolver.Matches(productCode, MaterialCategoryFilter.Labels).Should().Be(expected);
    }

    [Theory]
    [InlineData("VIC001", true)]
    [InlineData("vic001", true)]
    [InlineData("LAH001", true)]
    [InlineData("lah001", true)]
    [InlineData("KEL001", true)]
    [InlineData("kel001", true)]
    [InlineData("UZA001", true)]
    [InlineData("uza001", true)]
    [InlineData("ETI001", false)]
    [InlineData("MAT001", false)]
    public void Matches_Packaging_AcceptsPackagingPrefixes(string productCode, bool expected)
    {
        MaterialCategoryResolver.Matches(productCode, MaterialCategoryFilter.Packaging).Should().Be(expected);
    }

    [Theory]
    [InlineData("MAT001", true)]
    [InlineData("GOD001", true)]
    [InlineData("", true)]
    [InlineData("ETI001", false)]
    [InlineData("VIC001", false)]
    [InlineData("LAH001", false)]
    [InlineData("KEL001", false)]
    [InlineData("UZA001", false)]
    public void Matches_Other_ExcludesLabelsAndPackaging(string productCode, bool expected)
    {
        MaterialCategoryResolver.Matches(productCode, MaterialCategoryFilter.Other).Should().Be(expected);
    }

    [Theory]
    [InlineData("ETI001")]
    [InlineData("VIC001")]
    [InlineData("MAT001")]
    public void Matches_All_AcceptsEveryCode(string productCode)
    {
        MaterialCategoryResolver.Matches(productCode, MaterialCategoryFilter.All).Should().BeTrue();
    }
}
