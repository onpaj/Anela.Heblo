# Implementation: clock-advance-regression-test

## What was implemented

Added a regression test, `CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp`, to
`TransportBoxCompletionServiceTests` that advances the injected `FakeTimeProvider` by one hour
before invoking `CompleteReceivedBoxesAsync`, then asserts `box.LastStateChanged` and the
`StateLog` entry's `StateDate` equal `FrozenNow + 1h` rather than the frozen construction-time
value. This is the assertion that would fail if the service ever captured `_timeProvider.GetUtcNow()`
once at construction/startup instead of reading it fresh on each call — a plain "does it equal
the frozen time" test would still pass under that regression, so advancing the clock mid-test is
what makes this a real guard.

The guard was proven with a temporary sabotage-and-revert cycle (not committed): reverting
`TransportBoxCompletionService.cs` line `box.ToPick(_timeProvider.GetUtcNow().UtcDateTime, "System")`
back to `box.ToPick(DateTime.UtcNow, "System")` reproduced exactly the expected 2-test failure
(`AllOperationsCompleted_TransitionsBoxToStocked` and the new `ClockAdvanced_WritesAdvancedTimestamp`),
confirming the test suite would catch a regression to `DateTime.UtcNow`. The sabotage was then
reverted; `git diff --stat` on the service file is empty, confirming it is byte-identical to the
last commit.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`
  — added `CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp`, inserted after
  `CompleteReceivedBoxesAsync_OperationsSubmitted_LeavesBoxInReceived` and before the
  `SetupQueryReturns` helper, exactly as specified in the task context.

## Tests

- `CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp` (new) — advances the fake
  clock by 1 hour mid-test and asserts the box's state, `LastStateChanged`, and `StateLog` entry
  reflect the advanced time, not the time the test class was constructed.

Full suite run (`dotnet test ... --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"`):
`Passed! - Failed: 0, Passed: 8, Total: 8`.

Sabotage run (temporary, reverted before commit): `Failed! - Failed: 2, Passed: 6, Total: 8`.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxCompletionServiceTests"
```

Expected: `Passed! - Failed: 0, Passed: 8, Total: 8`.

`git diff --stat backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`
should produce no output (service file unchanged by this task).

`grep -n "DateTime\.UtcNow\|DateTime\.Now" backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`
should produce no matches — the test suite exercises only the injected `FakeTimeProvider`, never
the real system clock.

## Notes

`dotnet format Anela.Heblo.sln --include <test file>` ran clean (exit 0, no changes). The full
solution build (via `dotnet test`) succeeds with only pre-existing warnings unrelated to this
change.

## PR Summary
Locked in the TimeProvider-based clock behaviour from the two prior tasks with a regression test
that actually advances the fake clock mid-test, rather than merely asserting against the frozen
construction-time value — the weaker assertion would still pass if a static `DateTime.UtcNow`
read were reintroduced. Verified the guard is real via a temporary sabotage-and-revert cycle that
reproduced the expected 2-test failure before reverting.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs` — added `CompleteReceivedBoxesAsync_ClockAdvanced_WritesAdvancedTimestamp`

## Status
DONE
