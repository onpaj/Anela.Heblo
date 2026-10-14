using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.Packaging;

public class LabelOffsetQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParse_NoValue_IsValidAndEmpty(string? value)
    {
        LabelOffsetQuery.TryParse(value, out var offsets).Should().BeTrue();
        offsets.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_SeveralCarriers_ReturnsEachOffset()
    {
        LabelOffsetQuery.TryParse("Zasilkovna:0:-5, PPL:2:0,GLS:-30:30", out var offsets).Should().BeTrue();

        offsets.Should().BeEquivalentTo(new Dictionary<Carriers, LabelOffset>
        {
            [Carriers.Zasilkovna] = new(0, -5),
            [Carriers.PPL] = new(2, 0),
            [Carriers.GLS] = new(-30, 30),
        });
    }

    [Theory]
    [InlineData("Zasilkovna")]
    [InlineData("Zasilkovna:1")]
    [InlineData("Zasilkovna:1:2:3")]
    [InlineData("Osobak:1:2")]
    [InlineData("zasilkovna:1:2")]
    [InlineData("1:1:2")]
    [InlineData("Unknown:1:2")]
    [InlineData("PPL:31:0")]
    [InlineData("PPL:0:-31")]
    [InlineData("PPL:1.5:0")]
    [InlineData("PPL:x:0")]
    [InlineData("PPL:1:1,PPL:2:2")]
    [InlineData("PPL:1:1,")]
    public void TryParse_InvalidValue_ReturnsFalseAndNoOffsets(string value)
    {
        LabelOffsetQuery.TryParse(value, out var offsets).Should().BeFalse();
        offsets.Should().BeEmpty();
    }
}
