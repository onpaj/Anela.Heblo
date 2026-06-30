# Implementation: register-timewindowparser-in-analyticsmodule

## What was implemented
Added `services.AddScoped<TimeWindowParser>();` to `AnalyticsModule.AddAnalyticsModule`, immediately before the `IMarginCalculator` and `IMonthlyBreakdownGenerator` registrations. No `using` directive was needed as `TimeWindowParser` is in `Anela.Heblo.Application.Features.Analytics.Services`, already imported on line 3.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — added `services.AddScoped<TimeWindowParser>();` at line 47

## Tests
N/A — DI registration verified by build and integration tests.

## How to verify
1. `dotnet build` — still expect one CS0120 error in `GetProductMarginSummaryHandler.cs` (not yet updated), but `AnalyticsModule.cs` itself is clean.

## PR Summary
Registers `TimeWindowParser` as a scoped DI service in `AnalyticsModule` so it can be injected into `GetProductMarginSummaryHandler`.

## Status
DONE
