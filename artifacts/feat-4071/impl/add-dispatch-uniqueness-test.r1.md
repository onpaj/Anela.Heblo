# Implementation: add-dispatch-uniqueness-test

## What was implemented

Added a guard test that asserts, for every known `(from, to)` transport-box state
transition pair, exactly one registered `ITransportBoxTransitionSideEffect`
claims to support it. This closes the risk flagged in `arch-review.r1.md`: two
side effects both claiming the same pair would make dispatch order silently
significant (whichever side effect the handler iterates to first would win,
with no compiler or runtime signal that a second implementation also matched).

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs` —
  new test file. Instantiates all four registered side effects
  (`NewToOpenedSideEffect`, `OpenToReserveSideEffect`, `OpenToQuarantineSideEffect`,
  `ReceivedSideEffect`) with mocked dependencies, and for each of the six known
  transition pairs asserts `AllSideEffects().Count(s => s.Supports(from, to))`
  equals exactly 1.

## Tests

- `TransportBoxTransitionSideEffectDispatchTests.ExactlyOneSideEffectSupports_EachKnownTransitionPair`
  — `[Theory]`/`[MemberData]` over the six known pairs:
  - `New -> Opened`
  - `Opened -> Reserve`
  - `Opened -> Quarantine`
  - `InTransit -> Received`
  - `Reserve -> Received`
  - `Quarantine -> Received`

  Each assertion fails with a descriptive message
  (`"exactly one side effect should handle ({from} -> {to})"`) if zero or more
  than one side effect matches.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransportBoxTransitionSideEffectDispatchTests"
```

Result: build succeeded (pre-existing warnings only, none introduced by this
change), test run passed — 6/6 (all six known pairs), 0 failed, 0 skipped.

## Notes

- Used exactly the test code given in the task context, verbatim — the
  constructor signatures for `NewToOpenedSideEffect`
  (`ITransportBoxRepository`, `ICurrentUserService`, `TimeProvider`) and
  `ReceivedSideEffect` (`ILogisticsStockOperationService`,
  `ILogger<ReceivedSideEffect>`) were cross-checked against the actual source
  files before writing the test and match as given.
- `ICurrentUserService` lives in `Anela.Heblo.Domain.Features.Users` and
  `ILogisticsStockOperationService` in
  `Anela.Heblo.Application.Features.Logistics.Contracts` — both `using`
  directives were added per the task context's note.
- No production code was touched; this is a test-only addition.
- Did not run a repo-wide `dotnet format` pass (out of scope for this
  surgical, single-file task) — the new file's style (usings, naming,
  brace/indentation conventions) matches sibling files in the same directory
  (e.g. `NewToOpenedSideEffectTests.cs`).

## PR Summary
Adds a dispatch-uniqueness guard test for transport box transition side effects, closing the risk flagged in the arch review that two side effects could silently both claim the same `(from, to)` state pair and make dispatch order matter. The test instantiates all four registered `ITransportBoxTransitionSideEffect` implementations and asserts exactly one supports each of the six known transition pairs.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/TransportBoxTransitionSideEffectDispatchTests.cs` — new test file, 6/6 passing

## Status
DONE
