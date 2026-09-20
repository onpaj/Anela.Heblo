using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceOptionsValidatorTests
{
    private static MarketingPerformanceOptions Valid() => new()
    {
        RecomputeWindowMonths = 2,
        VatRate = 1.21m,
        Channels =
        {
            new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE9692928F" } },
            new MarketingChannelOptions { Code = "google", Label = "Google", VatIds = { "IE6388047V" } },
        },
    };

    private static readonly MarketingPerformanceOptionsValidator Validator = new();

    [Fact]
    public void Validate_ValidOptions_Succeeds() =>
        Validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_NoChannels_Fails()
    {
        var o = Valid(); o.Channels.Clear();
        Validator.Validate(null, o).FailureMessage.Should().Contain("at least one channel");
    }

    [Fact]
    public void Validate_ChannelWithoutVatIds_Fails()
    {
        var o = Valid(); o.Channels[0].VatIds.Clear();
        Validator.Validate(null, o).FailureMessage.Should().Contain("meta").And.Contain("VatIds");
    }

    [Fact]
    public void Validate_DuplicateVatIdAcrossChannels_Fails()
    {
        var o = Valid(); o.Channels[1].VatIds.Add("ie9692928f"); // case-insensitive duplicate
        Validator.Validate(null, o).FailureMessage.Should().Contain("IE9692928F");
    }

    [Fact]
    public void Validate_VatIdRepeatedWithinOneChannel_SaysSoInsteadOfBlamingTwoChannels()
    {
        // Arrange
        var o = Valid(); o.Channels[0].VatIds.Add("ie9692928f");

        // Act
        var result = Validator.Validate(null, o);

        // Assert
        result.FailureMessage.Should().Contain("meta").And.Contain("more than once");
        result.FailureMessage.Should().NotContain("more than one channel");
    }

    [Fact]
    public void Validate_MaxRecomputeRangeMonthsAboveTheCeiling_Fails()
    {
        // Arrange - a config typo here is the only thing between one request and
        // thousands of live ERP queries.
        var o = Valid(); o.MaxRecomputeRangeMonths = 6000;

        // Act & Assert
        Validator.Validate(null, o).FailureMessage.Should().Contain("MaxRecomputeRangeMonths");
    }

    [Fact]
    public void Validate_DuplicateChannelCode_Fails()
    {
        var o = Valid(); o.Channels[1].Code = "META";
        Validator.Validate(null, o).FailureMessage.Should().Contain("Code");
    }

    [Theory]
    [InlineData(0, 1.21)]
    [InlineData(2, 1.0)]
    [InlineData(2, 0.5)]
    public void Validate_BadWindowOrVatRate_Fails(int window, double vat)
    {
        var o = Valid(); o.RecomputeWindowMonths = window; o.VatRate = (decimal)vat;
        Validator.Validate(null, o).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ToDefinitions_NormalizesCodesToLowerAndTrimsVatIds()
    {
        var o = Valid(); o.Channels[0].Code = " Meta "; o.Channels[0].VatIds[0] = " IE9692928F ";
        var defs = o.ToDefinitions();
        defs[0].Code.Should().Be("meta");
        defs[0].VatIds.Should().Equal("IE9692928F");
    }
}
