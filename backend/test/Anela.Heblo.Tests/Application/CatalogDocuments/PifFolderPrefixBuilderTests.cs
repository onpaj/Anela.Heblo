using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class PifFolderPrefixBuilderTests
{
    [Fact]
    public void Build_ProductCodeLongerThanSix_TruncatesToFirstSixChars()
    {
        var result = PifFolderPrefixBuilder.Build("ABC12345");
        result.Should().Be("ABC123__");
    }

    [Fact]
    public void Build_ProductCodeExactlySix_UsesWholeCode()
    {
        var result = PifFolderPrefixBuilder.Build("ABC123");
        result.Should().Be("ABC123__");
    }

    [Fact]
    public void Build_ProductCodeShorterThanSix_UsesWholeCode()
    {
        var result = PifFolderPrefixBuilder.Build("AB1");
        result.Should().Be("AB1__");
    }

    [Fact]
    public void Build_EmptyProductCode_ReturnsSeparatorOnly()
    {
        var result = PifFolderPrefixBuilder.Build(string.Empty);
        result.Should().Be("__");
    }
}
