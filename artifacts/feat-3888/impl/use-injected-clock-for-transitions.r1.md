# Implementation: use-injected-clock-for-transitions

## What was implemented

Followed TDD as specified: first strengthened the three transition tests in
`TransportBoxCompletionServiceTests` to assert `box.LastStateChanged` and the
single `StateLog` entry's `StateDate`/`User`/(`Description`) against the
fixture's frozen `FrozenNow` instant, confirmed they failed against the
wall-clock-reading service (`Failed: 3, Passed: 4, Total: 7`), then replaced
the three `DateTime.UtcNow` call sites in `TransportBoxCompletionService.ProcessBoxAsync`
with `_timeProvider.GetUtcNow().UtcDateTime` (the `TimeProvider` field was
already injected via constructor in the prior task, `inject-timeprovider`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — lines 94, 114, 134: `DateTime.UtcNow` → `_timeProvider.GetUtcNow().UtcDateTime` in the "no operations" error path, the "all completed" `ToPick` transition, and the "any failed" error path. No other lines touched — branch conditions, log statements, `UpdateAsync`/`SaveChangesAsync` ordering, and returned `BoxProcessingResult` values are byte-identical to before.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — strengthened assertions in `CompleteReceivedBoxesAsync_AllOperationsCompleted_TransitionsBoxToStocked`, `CompleteReceivedBoxesAsync_AnyOperationFailed_TransitionsBoxToError`, and `CompleteReceivedBoxesAsync_NoOperationsForBox_TransitionsToError` to assert `box.LastStateChanged` and the `StateLog` entry against `FrozenNow.UtcDateTime`.

## Tests

`backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — all 7 tests in the class, including the 3 strengthened ones, now assert the frozen clock value instead of just the resulting `State` enum.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"
# Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7

grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
# no output, exit 1

grep -n "GetUtcNow()\.DateTime\|SpecifyKind" backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
# no output, exit 1
```

Confirmed both red phase (3 failing before the service edit, matching the
expected `LastStateChanged` mismatch error) and green phase (all 7 passing
after) by actually running the suite, not just inspecting code.

## Notes

An unrelated pre-existing build warning (MSB3073, `AccessMatrixGen` post-build
tool throwing a `JsonException` reading `access-matrix.generated.json`)
appeared during `dotnet test`'s build step. It did not fail the build or
block test execution (exit code 0, tests ran and passed) and is unrelated to
this change — left untouched per the surgical-changes policy.

## PR Summary

Switched `TransportBoxCompletionService.ProcessBoxAsync`'s three transition
call sites (`box.Error(...)` ×2, `box.ToPick(...)`) from `DateTime.UtcNow` to
the already-injected `_timeProvider.GetUtcNow().UtcDateTime`, and strengthened
the corresponding unit tests to assert the resulting `LastStateChanged` and
`StateLog` timestamps against the test fixture's frozen clock instead of only
checking the resulting `State` enum.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — 3 call sites now read from `_timeProvider` instead of `DateTime.UtcNow`
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — 3 tests now assert `LastStateChanged`/`StateLog` against the frozen clock

## Status
DONE
