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
