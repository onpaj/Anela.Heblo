### task: product-margin-summary-validator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs`
- Depends on: `time-window-shared-allowlist`, `analytics-error-code-and-message` (both must be committed first — this task references `TimeWindowParser.SupportedTimeWindows` and `ErrorCodes.InvalidTimeWindow`)

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryRequestValidatorTests"`
Expected: FAIL — build error, `GetProductMarginSummaryRequestValidator` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetProductMarginSummaryRequestValidator : AbstractValidator<GetProductMarginSummaryRequest>
{
    public GetProductMarginSummaryRequestValidator()
    {
        RuleFor(x => x.TimeWindow)
            .Must(v => TimeWindowParser.SupportedTimeWindows.Contains(v))
            .WithErrorCode(((int)ErrorCodes.InvalidTimeWindow).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "timeWindow", x.TimeWindow }
            })
            .WithMessage(x => string.Format(
                AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW,
                x.TimeWindow,
                string.Join(", ", TimeWindowParser.SupportedTimeWindows)));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryRequestValidatorTests"`
Expected: PASS (all 9 theory/fact cases)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs
git commit -m "feat(analytics): add GetProductMarginSummaryRequestValidator for TimeWindow"
```

---
