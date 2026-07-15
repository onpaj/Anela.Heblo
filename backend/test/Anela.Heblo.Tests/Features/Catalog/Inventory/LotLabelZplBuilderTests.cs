using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class LotLabelZplBuilderTests
{
    [Fact]
    public void Build_EmitsOneLabelBlockPerCount_WithBothTextLines_AndNoBarcode()
    {
        var zpl = LotLabelZplBuilder.Build("2926", "07/29", 3, 148, 0);

        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^XA").Should().HaveCount(3);
        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^XZ").Should().HaveCount(3);
        zpl.Should().Contain("^MNN");          // continuous media: no gap/mark sensing
        zpl.Should().Contain("^PW118");        // 10 mm @ 300 dpi width
        zpl.Should().Contain("^LL148");        // pitch is the caller-supplied calibration value
        zpl.Should().Contain("^FD2926^FS");    // line 1: lot number
        zpl.Should().Contain("^FD07/29^FS");   // line 2: expiration
        zpl.Should().NotContain("^BC");        // text-only: no Code128
        zpl.Should().NotContain("^BQ");        // text-only: no QR
    }

    [Theory]
    [InlineData(null, "07/29")]
    [InlineData("", "07/29")]
    [InlineData("   ", "07/29")]
    [InlineData("2926", null)]
    [InlineData("2926", "")]
    public void Build_Throws_OnEmptyLotNumberOrExpiration(string? lotNumber, string? expiration)
    {
        var act = () => LotLabelZplBuilder.Build(lotNumber!, expiration!, 1, 148, 0);
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Build_SpreadsDriftDotsEvenlyOver10Labels()
    {
        // 4 dots per 10 labels: exactly 4 of the 10 labels feed +1 dot, the rest the base pitch.
        var zpl = LotLabelZplBuilder.Build("2926", "07/29", 10, 148, 4);

        System.Text.RegularExpressions.Regex.Matches(zpl, @"\^LL149(?![0-9])").Should().HaveCount(4);
        System.Text.RegularExpressions.Regex.Matches(zpl, @"\^LL148(?![0-9])").Should().HaveCount(6);
    }

    [Fact]
    public void Build_SpreadsDriftProportionally_ForPartialRun()
    {
        // 4 dots per 10 labels over a run of 5 => 2 correction dots.
        var zpl = LotLabelZplBuilder.Build("2926", "07/29", 5, 148, 4);

        System.Text.RegularExpressions.Regex.Matches(zpl, @"\^LL149(?![0-9])").Should().HaveCount(2);
        System.Text.RegularExpressions.Regex.Matches(zpl, @"\^LL148(?![0-9])").Should().HaveCount(3);
    }

    [Fact]
    public void Build_AppliesNoDriftCorrection_WhenDriftIsZero()
    {
        var zpl = LotLabelZplBuilder.Build("2926", "07/29", 6, 148, 0);

        System.Text.RegularExpressions.Regex.Matches(zpl, @"\^LL148(?![0-9])").Should().HaveCount(6);
        zpl.Should().NotContain("^LL149");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_Throws_OnNonPositiveCount(int count)
    {
        var act = () => LotLabelZplBuilder.Build("2926", "07/29", count, 148, 0);
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void BuildCalibrationCross_EmitsCrosshair_WithSameMediaSetup_AndNoText()
    {
        var zpl = LotLabelZplBuilder.BuildCalibrationCross(148);

        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^XA").Should().HaveCount(1);
        zpl.Should().Contain("^MNN");   // same continuous-media setup as a real label
        zpl.Should().Contain("^LL148");
        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^GB").Should().HaveCount(2); // two crosshair lines
        zpl.Should().NotContain("^FD"); // no text
    }

    [Fact]
    public void BuildMediaFeed_EmitsFeedLabel_OfRequestedLength()
    {
        var zpl = LotLabelZplBuilder.BuildMediaFeed(40);

        zpl.Should().Contain("^MNN");
        zpl.Should().Contain("^LL40");
        zpl.Should().Contain("^GB");    // tiny mark that forces the printer to advance
        zpl.Should().NotContain("^FD"); // no text content
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BuildMediaFeed_Throws_OnNonPositiveDots(int dots)
    {
        var act = () => LotLabelZplBuilder.BuildMediaFeed(dots);
        act.Should().Throw<System.ArgumentException>();
    }
}
