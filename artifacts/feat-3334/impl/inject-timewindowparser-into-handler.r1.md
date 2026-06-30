# Implementation: inject-timewindowparser-into-handler

## What was implemented
`GetProductMarginSummaryHandler` now receives `TimeWindowParser` via constructor injection. The static call `TimeWindowParser.ParseTimeWindow(request.TimeWindow)` at line 31 was replaced with `_timeWindowParser.ParseTimeWindow(request.TimeWindow)`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` — added `private readonly TimeWindowParser _timeWindowParser` field; added `TimeWindowParser timeWindowParser` constructor parameter; replaced static method call with instance call

## Tests
N/A — handler constructor tests updated in next task.

## How to verify
`dotnet build` → 0 errors (verified: only pre-existing warnings remain).

## PR Summary
Injects `TimeWindowParser` into `GetProductMarginSummaryHandler` via constructor injection and replaces the static `TimeWindowParser.ParseTimeWindow()` call with the instance method. The build is now clean with no compile errors.

## Status
DONE
