using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Validators;

public class GetProductMarginSummaryRequestValidatorTests
{
    private readonly GetProductMarginSummaryRequestValidator _validator;

    public GetProductMarginSummaryRequestValidatorTests()
    {
        _validator = new GetProductMarginSummaryRequestValidator();
    }

    [Theory]
    [InlineData("current-year")]
    [InlineData("current-and-previous-year")]
    [InlineData("last-6-months")]
    [InlineData("last-12-months")]
    [InlineData("last-24-months")]
    public void TimeWindow_SupportedValue_ShouldNotHaveValidationError(string timeWindow)
    {
        var request = new GetProductMarginSummaryRequest { TimeWindow = timeWindow };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.TimeWindow);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("Current-Year")]
    public void TimeWindow_UnsupportedValue_ShouldHaveValidationError(string timeWindow)
    {
        var request = new GetProductMarginSummaryRequest { TimeWindow = timeWindow };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.TimeWindow)
            .WithErrorCode(((int)ErrorCodes.InvalidTimeWindow).ToString());
    }

    [Fact]
    public void TimeWindow_UnsupportedValue_CarriesOffendingValueInState()
    {
        var request = new GetProductMarginSummaryRequest { TimeWindow = "bogus" };

        var result = _validator.TestValidate(request);

        var failure = result.Errors.Should().ContainSingle().Subject;
        var state = failure.CustomState.Should().BeOfType<Dictionary<string, string>>().Subject;
        state["timeWindow"].Should().Be("bogus");
    }
}
