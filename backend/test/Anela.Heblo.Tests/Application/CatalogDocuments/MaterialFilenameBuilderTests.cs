using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class MaterialFilenameBuilderTests
{
    [Fact]
    public void Build_LotRequired_ProducesExpectedName()
    {
        var result = MaterialFilenameBuilder.Build(
            typeCode: "COA",
            lot: "2024-001",
            commonName: "Bisabolol",
            originalExtension: ".pdf");

        result.Should().Be("COA__2024-001__Bisabolol.pdf");
    }

    [Fact]
    public void Build_LotNotRequired_PreservesDoubleSeparator()
    {
        var result = MaterialFilenameBuilder.Build(
            typeCode: "SDS",
            lot: string.Empty,
            commonName: "Hyaluronic Acid",
            originalExtension: ".pdf");

        result.Should().Be("SDS____Hyaluronic Acid.pdf");
    }

    [Fact]
    public void Build_TrimsCommonName()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "  Vitamin E  ", ".docx");
        result.Should().Be("COA__L001__Vitamin E.docx");
    }

    [Fact]
    public void Build_ExtensionWithoutDot_AddsLeadingDot()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "Vitamin E", "pdf");
        result.Should().Be("COA__L001__Vitamin E.pdf");
    }

    [Fact]
    public void Build_EmptyExtension_ProducesNameWithoutExtension()
    {
        var result = MaterialFilenameBuilder.Build("COA", "L001", "Vitamin E", string.Empty);
        result.Should().Be("COA__L001__Vitamin E");
    }
}
