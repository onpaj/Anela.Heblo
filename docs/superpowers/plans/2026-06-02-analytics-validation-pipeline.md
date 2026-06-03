# Analytics Margin Report Validation Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralise validation for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` in their FluentValidation validators by wiring a new MediatR pipeline behavior that returns the existing `BaseResponse` envelope, then deleting the duplicate guards from both handlers.

**Architecture:** Add a typed `ValidationResultBehavior<TRequest, TResponse>` (returning `TResponse : BaseResponse, new()`) that translates the first FluentValidation failure into the existing `BaseResponse` shape — `ErrorCode` parsed from `failure.ErrorCode`, `Params` taken from `failure.CustomState`. The two Analytics validators get `WithErrorCode` + `WithState` annotations so the behavior produces the same `ErrorCodes` enum value and the same `Params` dictionary keys (`startDate`/`endDate`/`period`/`field`) the handlers produce today. The two validators are wired per-request in `AnalyticsModule` matching the established Catalog/Photobank pattern. After wiring is verified, the duplicate `if`-blocks at the top of `GetMarginReportHandler.Handle` and `GetProductMarginAnalysisHandler.Handle` are removed.

**Tech Stack:** .NET 8, C#, MediatR (`IPipelineBehavior`), FluentValidation (`AbstractValidator`, `WithErrorCode`, `WithState`, `TestHelper`), xUnit, Moq, FluentAssertions.

**Important pre-read for the executor:**
- Spec: `artifacts/feat-arch-review-analytics-getmarginreporthan/spec.r1.md`
- Arch review: `artifacts/feat-arch-review-analytics-getmarginreporthan/arch-review.r1.md` (note: the spec is wrong about the validators already being wired — the arch review is authoritative)
- Project rules: top-level `CLAUDE.md` (DTOs are classes, surgical changes only, build+format+tests must pass)

---

## File Structure

**New files**

| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs` | New typed `IPipelineBehavior<TRequest, TResponse>` that returns a `BaseResponse` envelope (does **not** throw). Parses `ErrorCode` from `failure.ErrorCode` and `Params` from `failure.CustomState`. |
| `backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs` | xUnit tests for the new behavior covering: no validators, valid request, failure with parseable ErrorCode + CustomState, failure with unparseable ErrorCode, failure with null CustomState. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs` | Validator unit tests using `FluentValidation.TestHelper`. Cover happy path + each rule failure, asserting both message and `WithErrorCode`/`WithState` payloads. |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs` | Same pattern for the analysis validator (incl. `ProductId.NotEmpty`). |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` | End-to-end integration test: build a minimal `ServiceCollection`, register MediatR + validators + `ValidationResultBehavior`, send an invalid `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` via `IMediator`, and assert the returned `BaseResponse` carries the exact `ErrorCode` and `Params` shape the frontend depends on. |

**Modified files**

| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs` | Add `.WithErrorCode(((int)ErrorCodes.X).ToString())` + `.WithState(req => new Dictionary<string,string>{ … })` to each rule so the behavior emits the same `ErrorCode` + `Params` as the current handler. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs` | Same, including `ProductId.NotEmpty()` → `ErrorCodes.RequiredFieldMissing` with `Params { ["field"] = "ProductId" }`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` | Register `IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>` and `IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>` to point at `ValidationResultBehavior<,>`. |
| `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` | Delete lines 35–54 (the three `if`-blocks + the "kept here for backward compatibility with tests" comment). |
| `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` | Delete lines 31–42 (the `ProductId` + date-range `if`-blocks + comment). |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` | Delete `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_PeriodTooLong_ReturnsErrorResponse`, `Handle_ZeroDaysPeriod_ReturnsErrorResponse` (lines 173-232). |
| `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` | Delete `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_EmptyProductId_ReturnsErrorResponse` (lines 109-152). |

**Unchanged contracts:** `GetMarginReportRequest`, `GetMarginReportResponse`, `GetProductMarginAnalysisRequest`, `GetProductMarginAnalysisResponse`, `BaseResponse`, `ErrorCodes`, `AnalyticsConstants`. No OpenAPI regeneration.

**Out of scope, do not touch:** the existing throwing `ValidationBehavior.cs` (other modules depend on its throw semantics), any other module's validation wiring, any error-code definitions, any frontend.

---

## Task 1: Create the `ValidationResultBehavior` (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs`
- Create: `backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs`

**Why this is first:** Everything else depends on this class existing and behaving correctly.

- [ ] **Step 1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Behaviors;

public class ValidationResultBehaviorTests
{
    public class TestRequest : IRequest<TestResponse>
    {
        public string? Name { get; set; }
    }

    public class TestResponse : BaseResponse
    {
        public string? Echo { get; set; }
    }

    private static Mock<RequestHandlerDelegate<TestResponse>> NextReturning(TestResponse response)
    {
        var next = new Mock<RequestHandlerDelegate<TestResponse>>();
        next.Setup(n => n()).ReturnsAsync(response);
        return next;
    }

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        // Arrange
        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(
            Array.Empty<IValidator<TestRequest>>());
        var request = new TestRequest { Name = "ok" };
        var expected = new TestResponse { Echo = "ok" };
        var next = NextReturning(expected);

        // Act
        var result = await behavior.Handle(request, next.Object, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        next.Verify(n => n(), Times.Once);
    }

    [Fact]
    public async Task Handle_AllValidatorsPass_CallsNext()
    {
        // Arrange
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(new[] { validator.Object });
        var request = new TestRequest { Name = "ok" };
        var expected = new TestResponse { Echo = "ok" };
        var next = NextReturning(expected);

        // Act
        var result = await behavior.Handle(request, next.Object, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        next.Verify(n => n(), Times.Once);
    }

    [Fact]
    public async Task Handle_FailureWithErrorCodeAndState_ReturnsBaseResponseEnvelope()
    {
        // Arrange
        var failure = new ValidationFailure("Name", "Name is required")
        {
            ErrorCode = ((int)ErrorCodes.RequiredFieldMissing).ToString(),
            CustomState = new Dictionary<string, string> { ["field"] = "Name" }
        };
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { failure }));

        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(new[] { validator.Object });
        var next = NextReturning(new TestResponse());

        // Act
        var result = await behavior.Handle(new TestRequest(), next.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().ContainKey("field").WhoseValue.Should().Be("Name");
        next.Verify(n => n(), Times.Never);
    }

    [Fact]
    public async Task Handle_FailureWithUnparseableErrorCode_FallsBackToValidationError()
    {
        // Arrange
        var failure = new ValidationFailure("Name", "boom") { ErrorCode = "not-a-number" };
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { failure }));

        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(new[] { validator.Object });
        var next = NextReturning(new TestResponse());

        // Act
        var result = await behavior.Handle(new TestRequest(), next.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_FailureWithNullCustomState_ReturnsEnvelopeWithNullParams()
    {
        // Arrange
        var failure = new ValidationFailure("Name", "boom")
        {
            ErrorCode = ((int)ErrorCodes.InvalidValue).ToString()
        };
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { failure }));

        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(new[] { validator.Object });
        var next = NextReturning(new TestResponse());

        // Act
        var result = await behavior.Handle(new TestRequest(), next.Object, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        result.Params.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultipleFailures_UsesFirstFailureMetadata()
    {
        // Arrange
        var first = new ValidationFailure("A", "first")
        {
            ErrorCode = ((int)ErrorCodes.InvalidDateRange).ToString(),
            CustomState = new Dictionary<string, string> { ["k"] = "first" }
        };
        var second = new ValidationFailure("B", "second")
        {
            ErrorCode = ((int)ErrorCodes.InvalidReportPeriod).ToString(),
            CustomState = new Dictionary<string, string> { ["k"] = "second" }
        };
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { first, second }));

        var behavior = new ValidationResultBehavior<TestRequest, TestResponse>(new[] { validator.Object });
        var next = NextReturning(new TestResponse());

        // Act
        var result = await behavior.Handle(new TestRequest(), next.Object, CancellationToken.None);

        // Assert
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        result.Params!["k"].Should().Be("first");
    }
}
```

- [ ] **Step 2: Confirm the test file fails to compile**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build error `CS0246: The type or namespace name 'ValidationResultBehavior<,>' could not be found`.

- [ ] **Step 3: Implement the behavior**

Create `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Shared;
using FluentValidation;
using MediatR;

namespace Anela.Heblo.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that translates FluentValidation failures into a
/// <see cref="BaseResponse"/> envelope instead of throwing.
/// Each validator rule should set <c>WithErrorCode</c> to the integer string of an
/// <see cref="ErrorCodes"/> value, and <c>WithState</c> to a <see cref="Dictionary{TKey,TValue}"/>
/// of params the frontend uses for error-message interpolation.
/// </summary>
public class ValidationResultBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : BaseResponse, new()
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationResultBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var first = failures[0];

        var errorCode = Enum.TryParse<ErrorCodes>(first.ErrorCode, out var parsed)
            ? parsed
            : ErrorCodes.ValidationError;

        var parameters = first.CustomState as Dictionary<string, string>;

        return new TResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Params = parameters,
        };
    }
}
```

- [ ] **Step 4: Run the new tests, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ValidationResultBehaviorTests"
```
Expected: 6 tests pass, 0 fail.

- [ ] **Step 5: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs
```
Expected: no diagnostics reported.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs \
        backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs
git commit -m "feat(application): add ValidationResultBehavior returning BaseResponse envelope"
```

---

## Task 2: Annotate `GetMarginReportRequestValidator` and add validator tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs`

**Why:** the behavior reads `ErrorCode` (string) and `CustomState` (dictionary) from each failure. The current validator sets only `WithMessage`, so the behavior would map every failure to `ErrorCodes.ValidationError` with `Params = null`.

- [ ] **Step 1: Write the failing validator test file**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Validators;

public class GetMarginReportRequestValidatorTests
{
    private readonly GetMarginReportRequestValidator _validator = new();

    private static GetMarginReportRequest ValidRequest() => new()
    {
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31),
        MaxProducts = 50,
    };

    [Fact]
    public void Valid_Request_HasNoErrors()
    {
        // Arrange
        var request = ValidRequest();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void StartDate_AfterEndDate_FailsWithInvalidDateRange()
    {
        // Arrange
        var request = ValidRequest();
        request.StartDate = new DateTime(2024, 12, 31);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.ShouldHaveValidationErrorFor(x => x.StartDate)
            .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString());
        var failureForRule = result.Errors.First(e => e.PropertyName == nameof(request.StartDate));
        var state = failureForRule.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!.Should().ContainKey("startDate");
        state.Should().ContainKey("endDate");
        state["startDate"].Should().Be("2024-12-31");
        state["endDate"].Should().Be("2024-01-01");
    }

    [Fact]
    public void Period_LongerThanMax_FailsWithInvalidReportPeriod()
    {
        // Arrange — make EndDate exactly one day past MAX_REPORT_PERIOD_DAYS
        var request = ValidRequest();
        request.StartDate = new DateTime(2020, 1, 1);
        request.EndDate = request.StartDate.AddDays(AnalyticsConstants.MAX_REPORT_PERIOD_DAYS + 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.Errors.Should().Contain(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        var failure = result.Errors.First(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!.Should().ContainKey("period");
        state["period"].Should().Contain("max").And.Contain(AnalyticsConstants.MAX_REPORT_PERIOD_DAYS.ToString());
    }

    [Fact]
    public void Period_ShorterThanMin_FailsWithInvalidReportPeriod()
    {
        // Arrange — MIN_REPORT_PERIOD_DAYS = 1, so StartDate == EndDate gives 0 days
        var request = ValidRequest();
        request.StartDate = new DateTime(2024, 6, 1);
        request.EndDate = new DateTime(2024, 6, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.First(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!["period"].Should().Contain("min").And.Contain(AnalyticsConstants.MIN_REPORT_PERIOD_DAYS.ToString());
    }

    [Fact]
    public void MaxProducts_AtZero_FailsValidation()
    {
        // Arrange
        var request = ValidRequest();
        request.MaxProducts = 0;

        // Act
        var result = _validator.TestValidate(request);

        // Assert — keep existing behavior; this rule predates the change and no
        // handler ever guarded against it, so leave it without WithErrorCode for now.
        result.ShouldHaveValidationErrorFor(x => x.MaxProducts);
    }
}
```

- [ ] **Step 2: Run the new test, confirm it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReportRequestValidatorTests"
```
Expected: tests fail because the validator's rules have no `ErrorCode` set (the `ShouldHaveValidationErrorFor(...).WithErrorCode(...)` assertion fails) and no `CustomState` is set.

- [ ] **Step 3: Update the validator**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs` and replace its body with:

```csharp
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetMarginReportRequestValidator : AbstractValidator<GetMarginReportRequest>
{
    public GetMarginReportRequestValidator()
    {
        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["startDate"] = x.StartDate.ToString(AnalyticsConstants.DATE_FORMAT),
                ["endDate"] = x.EndDate.ToString(AnalyticsConstants.DATE_FORMAT),
            })
            .WithMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays <= AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["period"] = $"{(x.EndDate - x.StartDate).TotalDays} days (max {AnalyticsConstants.MAX_REPORT_PERIOD_DAYS})",
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG, AnalyticsConstants.MAX_REPORT_PERIOD_DAYS));

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays >= AnalyticsConstants.MIN_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["period"] = $"{(x.EndDate - x.StartDate).TotalDays} days (min {AnalyticsConstants.MIN_REPORT_PERIOD_DAYS})",
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_SHORT, AnalyticsConstants.MIN_REPORT_PERIOD_DAYS));

        RuleFor(x => x.MaxProducts)
            .GreaterThan(0)
            .WithMessage(AnalyticsConstants.ValidationMessages.MAX_PRODUCTS_MINIMUM)
            .LessThanOrEqualTo(AnalyticsConstants.ABSOLUTE_MAX_PRODUCTS)
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.MAX_PRODUCTS_EXCEEDED, AnalyticsConstants.ABSOLUTE_MAX_PRODUCTS));

        // ProductFilter and CategoryFilter are optional, so no validation needed
    }
}
```

Note: the `MaxProducts` rule is left without `WithErrorCode`/`WithState` because no handler currently enforces it — the behavior would still short-circuit, but with `ErrorCodes.ValidationError` and `null` params. That's an acceptable fallback for a rule no caller hits today, and changing it would expand scope without need.

- [ ] **Step 4: Re-run the validator tests, confirm green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReportRequestValidatorTests"
```
Expected: 5 tests pass.

- [ ] **Step 5: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs
```
Expected: no diagnostics.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs
git commit -m "feat(analytics): annotate GetMarginReportRequestValidator with WithErrorCode/WithState"
```

---

## Task 3: Annotate `GetProductMarginAnalysisRequestValidator` and add validator tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs`

- [ ] **Step 1: Write the failing validator test file**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Validators;

public class GetProductMarginAnalysisRequestValidatorTests
{
    private readonly GetProductMarginAnalysisRequestValidator _validator = new();

    private static GetProductMarginAnalysisRequest ValidRequest() => new()
    {
        ProductId = "PROD001",
        StartDate = new DateTime(2024, 1, 1),
        EndDate = new DateTime(2024, 12, 31),
        IncludeBreakdown = false,
    };

    [Fact]
    public void Valid_Request_HasNoErrors()
    {
        // Arrange
        var request = ValidRequest();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_ProductId_FailsWithRequiredFieldMissing()
    {
        // Arrange
        var request = ValidRequest();
        request.ProductId = string.Empty;

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ProductId)
            .WithErrorCode(((int)ErrorCodes.RequiredFieldMissing).ToString());
        var failure = result.Errors.First(e => e.PropertyName == nameof(request.ProductId));
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!["field"].Should().Be("ProductId");
    }

    [Fact]
    public void StartDate_AfterEndDate_FailsWithInvalidDateRange()
    {
        // Arrange
        var request = ValidRequest();
        request.StartDate = new DateTime(2024, 12, 31);
        request.EndDate = new DateTime(2024, 1, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.StartDate)
            .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString());
        var failure = result.Errors.First(e => e.PropertyName == nameof(request.StartDate));
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!["startDate"].Should().Be("2024-12-31");
        state["endDate"].Should().Be("2024-01-01");
    }

    [Fact]
    public void Period_LongerThanMax_FailsWithInvalidReportPeriod()
    {
        // Arrange
        var request = ValidRequest();
        request.StartDate = new DateTime(2020, 1, 1);
        request.EndDate = request.StartDate.AddDays(AnalyticsConstants.MAX_REPORT_PERIOD_DAYS + 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.First(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!["period"].Should().Contain("max");
    }

    [Fact]
    public void Period_ShorterThanMin_FailsWithInvalidReportPeriod()
    {
        // Arrange
        var request = ValidRequest();
        request.StartDate = new DateTime(2024, 6, 1);
        request.EndDate = new DateTime(2024, 6, 1);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        var failure = result.Errors.First(e => e.ErrorCode == ((int)ErrorCodes.InvalidReportPeriod).ToString());
        var state = failure.CustomState as Dictionary<string, string>;
        state.Should().NotBeNull();
        state!["period"].Should().Contain("min");
    }
}
```

- [ ] **Step 2: Run the new test, confirm it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginAnalysisRequestValidatorTests"
```
Expected: tests fail because the validator's rules have no `ErrorCode`/`CustomState`.

- [ ] **Step 3: Update the validator**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs` and replace its body with:

```csharp
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Shared;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Analytics.Validators;

public class GetProductMarginAnalysisRequestValidator : AbstractValidator<GetProductMarginAnalysisRequest>
{
    public GetProductMarginAnalysisRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .WithErrorCode(((int)ErrorCodes.RequiredFieldMissing).ToString())
            .WithState(_ => new Dictionary<string, string> { ["field"] = "ProductId" })
            .WithMessage(AnalyticsConstants.ValidationMessages.PRODUCT_ID_REQUIRED);

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate)
            .WithErrorCode(((int)ErrorCodes.InvalidDateRange).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["startDate"] = x.StartDate.ToString(AnalyticsConstants.DATE_FORMAT),
                ["endDate"] = x.EndDate.ToString(AnalyticsConstants.DATE_FORMAT),
            })
            .WithMessage(AnalyticsConstants.ValidationMessages.INVALID_DATE_RANGE);

        // Optional: Add reasonable period limit for performance
        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays <= AnalyticsConstants.MAX_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["period"] = $"{(x.EndDate - x.StartDate).TotalDays} days (max {AnalyticsConstants.MAX_REPORT_PERIOD_DAYS})",
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_LONG, AnalyticsConstants.MAX_REPORT_PERIOD_DAYS));

        RuleFor(x => x)
            .Must(x => (x.EndDate - x.StartDate).TotalDays >= AnalyticsConstants.MIN_REPORT_PERIOD_DAYS)
            .WithErrorCode(((int)ErrorCodes.InvalidReportPeriod).ToString())
            .WithState(x => new Dictionary<string, string>
            {
                ["period"] = $"{(x.EndDate - x.StartDate).TotalDays} days (min {AnalyticsConstants.MIN_REPORT_PERIOD_DAYS})",
            })
            .WithMessage(string.Format(AnalyticsConstants.ValidationMessages.PERIOD_TOO_SHORT, AnalyticsConstants.MIN_REPORT_PERIOD_DAYS));

        // IncludeBreakdown is optional boolean, no validation needed
    }
}
```

- [ ] **Step 4: Run the validator tests, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginAnalysisRequestValidatorTests"
```
Expected: 5 tests pass.

- [ ] **Step 5: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs
```
Expected: no diagnostics.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs
git commit -m "feat(analytics): annotate GetProductMarginAnalysisRequestValidator with WithErrorCode/WithState"
```

---

## Task 4: Wire the pipeline behavior in `AnalyticsModule` and verify with an integration test

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs`

**Why this comes before deleting handler code:** until the pipeline is actually wired up, the validators don't execute at runtime — deleting the handler guards now would silently regress production. The integration test should fail before wiring and pass after.

- [ ] **Step 1: Write the failing pipeline integration test**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Pipeline;

/// <summary>
/// Verifies that the Analytics module wires <c>ValidationResultBehavior</c> for the two
/// margin-report requests so invalid input is rejected by the pipeline (not the handler)
/// and returned as the standard <see cref="BaseResponse"/> envelope.
/// </summary>
public class AnalyticsValidationPipelineTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // Mocked handler dependencies — the pipeline short-circuits on invalid input
        // so these are never actually invoked, but MediatR resolves the handler
        // up-front and requires the constructor dependencies to be present.
        services.AddSingleton(new Mock<IAnalyticsRepository>().Object);
        services.AddSingleton(new Mock<IProductFilterService>().Object);
        services.AddSingleton(new Mock<IReportBuilderService>().Object);
        services.AddSingleton(new Mock<IMarginCalculator>().Object);

        // MediatR + the AnalyticsModule wiring under test.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMarginReportHandler).Assembly));
        services.AddAnalyticsModule();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetMarginReport_InvalidDateRange_ReturnsErrorEnvelopeFromPipeline()
    {
        // Arrange
        var mediator = BuildProvider().GetRequiredService<IMediator>();
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 1, 1),
            MaxProducts = 50,
        };

        // Act
        var response = await mediator.Send(request);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        response.Params.Should().ContainKey("startDate").WhoseValue.Should().Be("2024-12-31");
        response.Params.Should().ContainKey("endDate").WhoseValue.Should().Be("2024-01-01");
    }

    [Fact]
    public async Task GetMarginReport_PeriodTooLong_ReturnsErrorEnvelopeFromPipeline()
    {
        // Arrange
        var mediator = BuildProvider().GetRequiredService<IMediator>();
        var start = new DateTime(2020, 1, 1);
        var request = new GetMarginReportRequest
        {
            StartDate = start,
            EndDate = start.AddDays(AnalyticsConstants.MAX_REPORT_PERIOD_DAYS + 1),
            MaxProducts = 50,
        };

        // Act
        var response = await mediator.Send(request);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidReportPeriod);
        response.Params.Should().ContainKey("period");
        response.Params!["period"].Should().Contain("max");
    }

    [Fact]
    public async Task GetProductMarginAnalysis_EmptyProductId_ReturnsErrorEnvelopeFromPipeline()
    {
        // Arrange
        var mediator = BuildProvider().GetRequiredService<IMediator>();
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = string.Empty,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
        };

        // Act
        var response = await mediator.Send(request);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        response.Params.Should().ContainKey("field").WhoseValue.Should().Be("ProductId");
    }

    [Fact]
    public async Task GetProductMarginAnalysis_InvalidDateRange_ReturnsErrorEnvelopeFromPipeline()
    {
        // Arrange
        var mediator = BuildProvider().GetRequiredService<IMediator>();
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 1, 1),
        };

        // Act
        var response = await mediator.Send(request);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        response.Params.Should().ContainKey("startDate");
        response.Params.Should().ContainKey("endDate");
    }
}
```

- [ ] **Step 2: Run the test, confirm it fails because the behavior is not registered**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests"
```
Expected: tests fail. Because the pipeline behavior is NOT yet registered, MediatR will go straight to the handler. With the mocked `IAnalyticsRepository`, the handler today either (a) still trips its own `if`-guards and returns an error envelope (which would *accidentally* satisfy `GetMarginReport_InvalidDateRange` and `GetProductMarginAnalysis_EmptyProductId` because of the in-handler validation we haven't removed yet) or (b) reaches the `try { … }` path and returns a real result/exception envelope (for the `PeriodTooLong` case on the analysis request, which the analysis handler does *not* guard). The `PeriodTooLong` assertion in particular is the canary that proves the wiring is missing.

The test in this step is allowed to "partially pass" before wiring — that's fine as long as **at least one assertion fails before Step 3 and all four pass after Step 3.** If all four already pass, that means the in-handler guards are still doing the work and the pipeline integration is not actually being exercised; in that case the test plan is broken — stop and reread the arch review.

- [ ] **Step 3: Register the pipeline behavior in `AnalyticsModule`**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` and add two `IPipelineBehavior` registrations right after the existing two validator registrations (between line 30 and line 32). The using directives also need `Anela.Heblo.Application.Common.Behaviors` and `MediatR`.

Resulting file content:

```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Analytics.DashboardTiles;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Features.Analytics.Validators;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Analytics;

/// <summary>
/// Enhanced analytics module with refactored services and validation
/// Registers new services for better separation of concerns and testability
/// </summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register refactored services for clean separation of concerns
        // Note: IMarginCalculationService is registered by CatalogModule and injected here
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();

        // Register validators for FluentValidation
        services.AddScoped<IValidator<GetMarginReportRequest>, GetMarginReportRequestValidator>();
        services.AddScoped<IValidator<GetProductMarginAnalysisRequest>, GetProductMarginAnalysisRequestValidator>();

        // Wire the validators into the MediatR pipeline. ValidationResultBehavior returns
        // a BaseResponse envelope (it does not throw) — see Common/Behaviors/ValidationResultBehavior.cs.
        services.AddScoped<IPipelineBehavior<GetMarginReportRequest, GetMarginReportResponse>,
                           ValidationResultBehavior<GetMarginReportRequest, GetMarginReportResponse>>();
        services.AddScoped<IPipelineBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>,
                           ValidationResultBehavior<GetProductMarginAnalysisRequest, GetProductMarginAnalysisResponse>>();

        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();

        // Register dashboard tiles
        services.RegisterTile<InvoiceImportStatisticsTile>();

        return services;
    }
}
```

- [ ] **Step 4: Re-run the pipeline test, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests"
```
Expected: 4 tests pass.

- [ ] **Step 5: Run the whole Analytics test folder to confirm nothing else regressed**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Analytics"
```
Expected: every test passes. The existing in-handler `if`-guards still match the existing handler-tests' expectations because we haven't deleted them yet — the pipeline runs first but the per-handler tests instantiate the handler directly and therefore bypass the pipeline.

- [ ] **Step 6: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs
```
Expected: no diagnostics.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs
git commit -m "feat(analytics): wire ValidationResultBehavior for margin-report requests"
```

---

## Task 5: Remove duplicated validation from `GetMarginReportHandler` and prune its unit tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`

The pipeline behavior is now load-bearing for these checks, so the in-handler guards are finally dead code.

- [ ] **Step 1: Delete the three obsolete unit tests so they don't fail after the handler change**

Open `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` and delete the following test methods entirely (and their preceding blank lines / `[Fact]` attributes):

1. `Handle_InvalidDateRange_ReturnsErrorResponse` (currently lines 173–192)
2. `Handle_PeriodTooLong_ReturnsErrorResponse` (currently lines 194–212)
3. `Handle_ZeroDaysPeriod_ReturnsErrorResponse` (currently lines 214–232)

Equivalent validator-level coverage already exists in `GetMarginReportRequestValidatorTests` (Task 2) and pipeline-level coverage in `AnalyticsValidationPipelineTests` (Task 4). Do not move them — they would be duplicates.

- [ ] **Step 2: Run the remaining `GetMarginReportHandlerTests`, expect green (handler still has the `if`-blocks at this point)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReportHandlerTests"
```
Expected: all remaining tests pass.

- [ ] **Step 3: Delete the duplicate validation block from the handler**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`. Replace the body of `Handle` so that the section currently at lines 33–54 (from the `// Basic input validation (kept here for backward compatibility with tests)` comment through the third `if`-block, inclusive) is removed.

The resulting `Handle` method should start like this:

```csharp
public async Task<GetMarginReportResponse> Handle(GetMarginReportRequest request, CancellationToken cancellationToken)
{
    try
    {
        // Get products stream from repository
        var productTypes = new[] { AnalyticsProductType.Product, AnalyticsProductType.Goods };
        var productsStream = _analyticsRepository.StreamProductsWithSalesAsync(
            request.StartDate,
            request.EndDate,
            productTypes,
            cancellationToken);

        // … rest of the method unchanged …
```

Do not change any other line. Do not touch `CreateErrorResponse`, `BuildSuccessResponse`, or any of the helper classes.

- [ ] **Step 4: Re-run the handler tests, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReportHandlerTests"
```
Expected: all remaining tests pass.

- [ ] **Step 5: Re-run the pipeline test as a sanity check (pipeline is now the sole enforcer)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests"
```
Expected: 4 tests pass.

- [ ] **Step 6: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs
```
Expected: no diagnostics.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs
git commit -m "refactor(analytics): remove duplicate validation guards from GetMarginReportHandler"
```

---

## Task 6: Remove duplicated validation from `GetProductMarginAnalysisHandler` and prune its unit tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

- [ ] **Step 1: Delete the two obsolete unit tests**

Open `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` and delete:

1. `Handle_InvalidDateRange_ReturnsErrorResponse` (currently lines 109–130)
2. `Handle_EmptyProductId_ReturnsErrorResponse` (currently lines 132–152)

Validator-level coverage exists in `GetProductMarginAnalysisRequestValidatorTests` (Task 3); pipeline-level in `AnalyticsValidationPipelineTests` (Task 4). Do not move them.

- [ ] **Step 2: Run the remaining tests, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginAnalysisHandlerTests"
```
Expected: all remaining tests pass.

- [ ] **Step 3: Delete the duplicate validation block from the handler**

Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`. Remove lines 31–42 (from the `// Basic input validation` comment through the `InvalidDateRange` `if`-block, inclusive).

The resulting `Handle` method should start like this:

```csharp
public async Task<GetProductMarginAnalysisResponse> Handle(GetProductMarginAnalysisRequest request, CancellationToken cancellationToken)
{
    try
    {
        // Get product data
        var productData = await _analyticsRepository.GetProductAnalysisDataAsync(
            request.ProductId,
            request.StartDate,
            request.EndDate,
            cancellationToken);

        // … rest of the method unchanged …
```

Do not touch `CreateErrorResponse`, `BuildSuccessResponse`, or `HasSalesInPeriod`.

- [ ] **Step 4: Re-run the handler tests, expect green**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginAnalysisHandlerTests"
```
Expected: all remaining tests pass.

- [ ] **Step 5: Run the pipeline test as a sanity check**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsValidationPipelineTests"
```
Expected: 4 tests pass.

- [ ] **Step 6: Format**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --include backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs
```
Expected: no diagnostics.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs
git commit -m "refactor(analytics): remove duplicate validation guards from GetProductMarginAnalysisHandler"
```

---

## Task 7: Final validation gates

**Files:** none modified.

- [ ] **Step 1: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln -warnaserror
```
Expected: build succeeds with zero warnings.

- [ ] **Step 2: Full backend format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exits with code 0 (no formatting drift).

- [ ] **Step 3: Full backend test run**

Run:
```bash
dotnet test backend/Anela.Heblo.sln
```
Expected: 100% pass. Pay particular attention to:
- `ValidationResultBehaviorTests` (6 tests)
- `GetMarginReportRequestValidatorTests` (5 tests)
- `GetProductMarginAnalysisRequestValidatorTests` (5 tests)
- `AnalyticsValidationPipelineTests` (4 tests)
- `GetMarginReportHandlerTests` (now 7 tests, was 10)
- `GetProductMarginAnalysisHandlerTests` (now 6 tests, was 8)

- [ ] **Step 4: Review the diff one last time**

Run:
```bash
git log --oneline main..HEAD
git diff main..HEAD --stat
```
Confirm the change set matches: one new behavior + one behavior test, two validator changes + two validator-test files, one module wiring change + one pipeline-test file, two handler simplifications + two pruned handler-test files. Nothing else should be touched.

- [ ] **Step 5: PR description checklist (for the human reviewer's notes — not committed)**

When opening the PR (final step, not part of this plan), surface:
- The arch-review correction: validators were not actually pipeline-wired before — the spec said they were, but they weren't.
- The new `ValidationResultBehavior` is **separate** from the existing throwing `ValidationBehavior`. Other modules are unaffected.
- Deleted handler tests: `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_PeriodTooLong_ReturnsErrorResponse`, `Handle_ZeroDaysPeriod_ReturnsErrorResponse` (GetMarginReport); `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_EmptyProductId_ReturnsErrorResponse` (GetProductMarginAnalysis). Equivalent coverage moved to validator + pipeline tests.
- The pipeline now also enforces MIN/MAX period rules for `GetProductMarginAnalysis` (the validator always had those rules; the handler never enforced them). This is the intended behavior per the validator's existing rules.
- Out of scope (mentioned in the arch review): other Analytics handlers may have similar duplication; deliberately not touched in this PR.

---

## Spec Coverage Check

| Requirement | Covered by |
|---|---|
| FR-1: remove validation block in `GetMarginReportHandler.Handle` | Task 5 (steps 3–4) |
| FR-2: remove validation block in `GetProductMarginAnalysisHandler.Handle` (incl. arch-review's amendment 3: also remove the `ProductId` guard) | Task 6 (steps 3–4) |
| FR-3 (arch-review's strengthened version): invalid input flowing through `IMediator.Send` still produces the existing `ErrorCode` + `Params` shape | Tasks 1 + 3 + 4 (`ValidationResultBehavior` + validator annotations + pipeline integration test) |
| FR-4: handler unit tests no longer cover validation; equivalent validator-level coverage exists | Tasks 2, 3, 5, 6 (new validator tests; deleted handler tests) |
| FR-5: at least one `IMediator`-level test per handler asserting the expected error code | Task 4 (`AnalyticsValidationPipelineTests` covers both handlers' `InvalidDateRange` paths plus `RequiredFieldMissing` and `InvalidReportPeriod`) |
| FR-6: no changes to public contracts | Verified by Task 7 (no contracts, no OpenAPI files touched) |
| NFR-1 (performance): no regression | Pipeline behavior runs in place of the handler `if`-blocks — same cost, same short-circuit |
| NFR-2 (security): unchanged | No trust-boundary changes |
| NFR-3 (maintainability): single source of truth | Achieved — validator owns rules + error codes via `WithErrorCode`/`WithState` |
| NFR-4: build + format clean, all tests pass | Task 7 |
| Arch-review amendment 1 (FR-0 — wire pipeline BEFORE deleting handler code) | Task 4 happens before Tasks 5 and 6 |
| Arch-review amendment 2 (FR-0b — introduce `ValidationResultBehavior`, annotate with `WithErrorCode`) | Tasks 1, 2, 3 |
| Arch-review amendment 5 (Params shape preserved) | Validator `WithState` builds the exact dictionaries; pipeline test asserts them |
| Arch-review amendment 6 (pipeline wiring is in scope, not deferred) | Task 4 |
