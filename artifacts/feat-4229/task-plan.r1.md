# Validate TimeWindow on GetProductMarginSummaryRequest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Pipeline note:** this plan is consumed by the AgentHarness planning pipeline. Task boundaries below are marked with `### task: {name}` headers (not `### Task N:`) so the orchestrator can extract them programmatically into per-task context files. Each task is still written to the bite-sized, TDD, fully-coded standard of the writing-plans skill.

**Goal:** Make `GET /api/analytics/product-margin-summary` return HTTP 400 with a structured error body for an invalid `timeWindow` query value, instead of an unhandled `ArgumentException` producing a 500.

**Architecture:** Add a `GetProductMarginSummaryRequestValidator` (FluentValidation) and register it with the existing generic `ValidationResultBehavior<TRequest, TResponse>` MediatR pipeline behavior in `AnalyticsModule`, exactly mirroring the already-working pattern for `GetMarginReportRequest`/`GetProductMarginAnalysisRequest`. The allowed `TimeWindow` values are exposed as a single shared static list on `TimeWindowParser` so the validator and the parser's `switch` never drift apart. A new `ErrorCodes.InvalidTimeWindow = 1706` (`[HttpStatusCode(BadRequest)]`) and a new `AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW` message carry the specific, user-actionable error.

**Tech Stack:** .NET 8, MediatR, FluentValidation, xUnit, FluentAssertions, Moq.

---

### task: time-window-shared-allowlist

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs` (new file — no existing test file for this class was found; create it)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Services;

public class TimeWindowParserTests
{
    [Fact]
    public void SupportedTimeWindows_ContainsExactlyTheFiveKnownValues()
    {
        TimeWindowParser.SupportedTimeWindows.Should().BeEquivalentTo(new[]
        {
            "current-year",
            "current-and-previous-year",
            "last-6-months",
            "last-12-months",
            "last-24-months"
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimeWindowParserTests"`
Expected: FAIL — build error, `TimeWindowParser` has no member `SupportedTimeWindows`.

- [ ] **Step 3: Write minimal implementation**

In `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`, add the static field to the existing class (do not change `ParseTimeWindow`'s body or its `ArgumentException` fallback):

```csharp
public class TimeWindowParser : ITimeWindowParser
{
    public static readonly string[] SupportedTimeWindows =
        ["current-year", "current-and-previous-year", "last-6-months", "last-12-months", "last-24-months"];

    private readonly TimeProvider _timeProvider;

    public TimeWindowParser(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = _timeProvider.GetLocalNow().Date;

        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            "last-24-months" => (today.AddMonths(-24), today),
            _ => throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow))
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimeWindowParserTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs
git commit -m "feat(analytics): expose TimeWindowParser.SupportedTimeWindows as shared allow-list"
```

---

### task: analytics-error-code-and-message

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after line 199, `InvalidReportPeriod = 1705,`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/ErrorCodesTests.cs` if it exists — otherwise this task has no dedicated test (enum members and string constants are exercised indirectly by the validator test in the next task); skip Steps 1/2/4 for this task and go straight to the edit.

- [ ] **Step 1: Add the new error code**

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, immediately after `InvalidReportPeriod = 1705,` (still inside the `// Analytics module errors (17XX)` block):

```csharp
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    InvalidTimeWindow = 1706,
```

- [ ] **Step 2: Add the new validation message constant**

In `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs`, inside the existing `ValidationMessages` nested class, after `MAX_PRODUCTS_MINIMUM`:

```csharp
        public const string INVALID_TIME_WINDOW = "Invalid time window: '{0}'. Supported values: {1}";
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs
git commit -m "feat(analytics): add InvalidTimeWindow error code and message"
```

---

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

### task: register-validation-pipeline

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs:37-44`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs`
- Depends on: `product-margin-summary-validator` (must be committed first)

- [ ] **Step 1: Write the failing integration test**

In `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs`, extend the existing `BuildMediator` helper to also register the new pair, and add a new test case.

Add these `using`s at the top of the file:

```csharp
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
```

In `BuildMediator`, alongside the existing `services.AddScoped<IValidator<GetMarginReportRequest>, ...>` / `IPipelineBehavior<...>` lines, add:

```csharp
services.AddScoped<IValidator<GetProductMarginSummaryRequest>, GetProductMarginSummaryRequestValidator>();
services.AddScoped<IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>,
    ValidationResultBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>>();
```

`GetProductMarginSummaryHandler`'s other constructor dependencies (`ITimeWindowParser`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`, `ITopProductSorter`, `IAnalyticsRepository`) must also be registered in `BuildMediator` for the handler to resolve on the valid-input path — add mocks/registrations for whichever of these aren't already present in the helper (the file's imports already show `IAnalyticsRepository`, `IMarginCalculator` registered for the `GetMarginReport` tests; add `services.AddScoped<ITimeWindowParser>(_ => new TimeWindowParser(TimeProvider.System));`, `services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();`, `services.AddScoped<ITopProductSorter, TopProductSorter>();`, and `services.AddScoped<IProductFilterService>(_ => new Mock<IProductFilterService>().Object);`, `services.AddScoped<IReportBuilderService>(_ => new Mock<IReportBuilderService>().Object);` as needed by the handler's actual constructor — check `GetProductMarginSummaryHandler`'s constructor signature before wiring, since this test only needs the invalid-input path to short-circuit before the handler runs).

Add the test case (append to the class):

```csharp
    [Fact]
    public async Task GetProductMarginSummary_InvalidTimeWindow_ReturnsInvalidTimeWindowErrorCode()
    {
        // Arrange
        var mediator = BuildMediator();
        var request = new GetProductMarginSummaryRequest { TimeWindow = "not-a-real-window" };

        // Act
        var response = await mediator.Send(request);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidTimeWindow);
        response.Params.Should().ContainKey("timeWindow").WhoseValue.Should().Be("not-a-real-window");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests.GetProductMarginSummary_InvalidTimeWindow_ReturnsInvalidTimeWindowErrorCode"`
Expected: FAIL — `GetProductMarginSummaryRequest` has no registered validator/behavior yet, so the mediator either throws resolving the handler's missing dependencies or the request reaches the real (unvalidated) handler and throws `ArgumentException` instead of returning a `Success=false` response.

- [ ] **Step 3: Register the validator and pipeline behavior**

In `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`, add these two lines immediately after the existing `GetProductMarginAnalysisRequest` registrations (after line 38 for the validator, after line 44 for the behavior), so the block reads:

```csharp
        // Register validators for FluentValidation
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginSummaryRequest>, GetProductMarginSummaryRequestValidator>();

        // Register MediatR validation pipeline behavior for Analytics requests
        services.AddScoped<IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>,
            ValidationResultBehavior<GetMarginReportRequest, GetMarginReportResponse>>();
        services.AddScoped<IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>,
            ValidationResultBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>>();
        services.AddScoped<IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>,
            ValidationResultBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>>();
```

Add the necessary `using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;` to the top of `AnalyticsModule.cs` if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests"`
Expected: PASS — all existing pipeline tests plus the new one.

- [ ] **Step 5: Run the full Analytics test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics"`
Expected: PASS. In particular, confirm `GetProductMarginSummaryHandlerTests.ParseTimeWindow_UnknownValue_ThrowsArgumentException` (which calls `_handler.Handle(...)` directly, bypassing MediatR and the new pipeline behavior entirely) still passes unchanged — it documents `TimeWindowParser`'s defensive fallback for direct/non-pipeline callers and requires no edit, since neither the handler nor the parser's `switch` were modified by this plan.

- [ ] **Step 6: Full backend build and format check**

Run: `dotnet build` (from `backend/`)
Run: `dotnet format --verify-no-changes` (from `backend/`)
Expected: both succeed with no errors/no formatting diffs.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs
git commit -m "feat(analytics): register GetProductMarginSummaryRequest validation pipeline"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (reject invalid values, 400 not 500, standard error contract, empty-string case, all 5 values still accepted) → covered by `product-margin-summary-validator` (validator + tests including `""` case) and `register-validation-pipeline` (integration test proving 400-shaped response, regression via full Analytics suite run).
- FR-2 (register validator + behavior in `AnalyticsModule`, same style/location as siblings) → `register-validation-pipeline`.
- FR-3 (new specific `ErrorCodes` value + message, `Params` carries offending value) → `analytics-error-code-and-message` + validator test `TimeWindow_UnsupportedValue_CarriesOffendingValueInState`.
- NFR-1 (backward compatibility) → no changes to request/response shape; `product-margin-summary-validator`'s supported-value theory test plus the untouched existing handler tests confirm this.
- NFR-2 (single source of truth) → `time-window-shared-allowlist`.
- NFR-3 (no other request affected) → `register-validation-pipeline` only adds lines, never edits the `GetMarginReportRequest`/`GetProductMarginAnalysisRequest` registrations.
- Out of Scope items (enum redesign, removing the parser's exception, changing sibling validators, frontend changes) → intentionally not addressed by any task.

**Placeholder scan:** No "TBD"/"handle appropriately"/unfollowed "similar to Task N" references — every step above has literal, complete code or an exact command.

**Type consistency:** `GetProductMarginSummaryRequestValidator`, `TimeWindowParser.SupportedTimeWindows`, `ErrorCodes.InvalidTimeWindow`, and `AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW` are named identically across every task that references them (`time-window-shared-allowlist` → `analytics-error-code-and-message` → `product-margin-summary-validator` → `register-validation-pipeline`).
