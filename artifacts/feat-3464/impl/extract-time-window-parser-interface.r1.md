# Implementation: extract-time-window-parser-interface

## What was implemented
Extracted an `ITimeWindowParser` interface for the previously-concrete `TimeWindowParser` class in the Analytics module, so `GetProductMarginSummaryHandler` depends on an abstraction like every other collaborator in the module (`IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`). This is a pure DIP-compliance refactor — no behavior, parsing logic, or public contracts changed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — added `ITimeWindowParser` interface (colocated with the implementation, matching the `MarginCalculator`/`IMarginCalculator` pattern) with signature `(DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)`; `TimeWindowParser` now implements it. Constructor and method body unchanged.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — DI registration changed from `services.AddScoped<TimeWindowParser>();` to `services.AddScoped<ITimeWindowParser, TimeWindowParser>();` (Scoped lifetime preserved).
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — constructor parameter and backing field changed from `TimeWindowParser` to `ITimeWindowParser`. No other logic touched.

## Tests
No test files were modified. `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` constructs a concrete `TimeWindowParser` and passes it to the handler constructor; since `TimeWindowParser` now implements `ITimeWindowParser`, this continues to compile and exercise the same behavior unchanged (per spec FR-4 and the task plan, updating this test was explicitly optional/not required).

## How to verify
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — succeeds with 0 errors, 0 warnings (verified).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"` — **could not be executed**: the `Anela.Heblo.Tests` project currently fails to compile on `main` (commit `2b9d8ce`, pre-dating this branch) due to an unrelated pre-existing bug: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs:95` references `ConfigurationConstants.APP_VERSION`, but that constant was moved to `InfrastructureConfigurationKeys` in a prior commit (`#3430`/`#3435`) and this one call site was never updated (`error CS0117`). This breaks the whole test assembly's build, independent of and prior to this change. Fixing it is out of scope for issue #3464 (unrelated file/module) and is not touched by this diff. Verified by inspection that `GetProductMarginSummaryHandlerTests.cs`'s constructor and all 6 tests are unaffected by the interface extraction (types remain assignment-compatible).

## Notes
The pre-existing `GetConfigurationHandlerTests.cs` compile error should be reported/fixed separately — it currently blocks `dotnet test` for the entire backend test suite on `main`.

## PR Summary
`GetProductMarginSummaryHandler` was the only collaborator in the Analytics module injected as a concrete class (`TimeWindowParser`) instead of an interface, inconsistent with `IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, and `IMonthlyBreakdownGenerator`, and harder to unit-test as a result. This change extracts `ITimeWindowParser`, has `TimeWindowParser` implement it, updates the DI registration in `AnalyticsModule.cs`, and updates the handler's constructor/field to depend on the interface. No behavior, parsing logic, or public API changed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — added `ITimeWindowParser` interface, implemented by `TimeWindowParser`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — DI registration now maps `ITimeWindowParser` to `TimeWindowParser`
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — constructor/field now depend on `ITimeWindowParser`

## Status
DONE_WITH_CONCERNS
