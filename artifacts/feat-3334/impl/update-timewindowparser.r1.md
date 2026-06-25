# Implementation: update-timewindowparser

## What was implemented
`TimeWindowParser` was converted from a static utility class to an injectable instance class. The class now receives a `TimeProvider` via constructor injection and uses it to determine the current date, replacing the hard-coded `DateTime.Today`. The silent catch-all fallback (`_ => (new DateTime(today.Year, 1, 1), today)`) was replaced with an `ArgumentException` that clearly identifies the unknown time window value.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — converted from `static class` with `static` method to injectable instance class with constructor accepting `TimeProvider`; `DateTime.Today` replaced with `_timeProvider.GetLocalNow().Date`; silent fallback replaced with `ArgumentException`

## Tests
N/A (tests updated in a later task)

## How to verify
1. Build the solution: `dotnet build` from the repo root — expect one compile error in `GetProductMarginSummaryHandler.cs` (CS0120) because it still calls `TimeWindowParser.ParseTimeWindow` as a static method. That error will be resolved in the subsequent task that updates the handler to inject and use the instance.
2. Once the handler is updated and DI registration is added, the build should be clean.

## PR Summary
Converts `TimeWindowParser` from a static class to an injectable instance class that accepts `TimeProvider` via constructor injection. This enables testable, deterministic time handling and replaces the opaque silent fallback for unknown time window values with an `ArgumentException` that surfaces the bad input clearly.

## Status
DONE
