using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerLabelZplBuilderTests
{
    [Fact]
    public void Build_EmitsOneLabelBlockPerCode_WithCode128AndText()
    {
        var zpl = MaterialContainerLabelZplBuilder.Build(new[] { "M00000001", "M00000002" });

        zpl.Split("^XZ", System.StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^XA").Should().HaveCount(2);
        zpl.Should().Contain("^BCN");              // Code128 barcode command
        zpl.Should().Contain("^FDM00000001^FS");   // barcode + human-readable use same data
    }

    [Fact]
    public void Build_Throws_OnEmptyInput()
    {
        var act = () => MaterialContainerLabelZplBuilder.Build(System.Array.Empty<string>());
        act.Should().Throw<System.ArgumentException>();
    }
}
