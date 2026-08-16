# Implementation: consume-rule-in-transport-box-repository

## What was implemented

Both code-lookup paths in `TransportBoxRepository` now derive from `TransportBoxStateRules` instead of restating the active/busy-state partition:

- `IsBoxCodeActiveAsync` dropped its local `activeStates` allow-list (which was missing `Quarantine` and `Error` — the reported bug) and now filters with `TransportBoxStateRules.OccupiesCodePredicate` composed via a second `.Where(...)`.
- `GetByCodeAsync` was restructured from `FirstOrDefaultAsync(predicate)` to `.Where(...).OrderByDescending(TransportBoxStateRules.OccupiesCodePredicate).ThenByDescending(o => o.Id).FirstOrDefaultAsync()`, so the code filter is applied before ordering and the resolution now ranks any code-occupying box (not just non-`Closed`) ahead of released (`Closed`/`Stocked`) ones.

Per Amendment A1 (task context), the fix composes `TransportBoxStateRules.OccupiesCodePredicate` directly rather than hand-writing any new restatement of the invariant.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` — rewrote `IsBoxCodeActiveAsync` and `GetByCodeAsync` as specified; `GetPagedListAsync`, `GetReceivedBoxesAsync`, `GetStateSummaryAsync`, `FindAsync`, `GetByIdWithDetailsAsync` untouched.
- `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs` — added state-builder helpers (`NewBoxWithCode`, `OpenedBox`, `InTransitBox`, `ReceivedBox`, `StockedBox`, `ClosedBox`, `ReserveBox`, `QuarantineBox`, `ErrorBox`) and 12 new tests: a per-state truth table for `IsBoxCodeActiveAsync` (`Quarantine`/`Error`/`New`/`Opened`/`InTransit`/`Received`/`Reserve` → true; `Stocked`/`Closed`/unheld code → false) plus two `GetByCodeAsync` ordering tests. All new fixtures are seeded inside their own test methods on unused codes (`B500`-`B511`), per the task context's note not to touch `SeedTestData()` (which the six pre-existing case-insensitivity theories depend on for exact counts).

## Tests

- `TransportBoxRepositoryCaseHandlingTests` — 42 tests total (30 pre-existing + 12 new), all passing.
- Regression sweep `FullyQualifiedName~TransportBox&Category!=Integration` — 220 tests, all passing (includes `ChangeTransportBoxStateHandlerTests`, `GetTransportBoxByCodeHandlerTests`, `TransportBoxCodeCaseHandlingTests`, `TransportBoxStateRulesTests`, `TransportBoxUniquenessTests`, unmodified).

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxRepositoryCaseHandlingTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
grep -n "TransportBoxState.Closed\|TransportBoxState.Stocked" backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs
# -> only the isActiveFilter line (x.State != TransportBoxState.Closed) in GetPagedListAsync
dotnet build
dotnet format Anela.Heblo.sln --no-restore  # (run from repo root)
```

Before the fix, running the new tests against the original code confirmed the exact expected failure set: `IsBoxCodeActiveAsync_QuarantineBox_ReturnsTrue`, `IsBoxCodeActiveAsync_ErrorBox_ReturnsTrue`, and `GetByCodeAsync_StockedBoxWithHigherId_ReturnsOccupyingOpenedBox` failed (3 failed / 39 passed of 42); everything else passed unmodified. After the fix, all 42 pass.

## Notes

- Confirmed via `ps`/environment investigation that this sandbox's `dotnet build`/`dotnet test` can hang indefinitely when stale `nodeReuse:true` MSBuild server processes survive across sessions combined with the repo's Debug-only `GenerateAccessMatrix` pre-build step (`Anela.Heblo.API.csproj`) crashing (a pre-existing, `ContinueOnError="true"`-tolerated `Anela.Heblo.AccessMatrixGen` argument-order bug, unrelated to this change). Running with `DOTNET_CLI_DISABLE_BUILD_SERVERS=1 MSBUILDDISABLENODEREUSE=1 ... -nodeReuse:false` avoided the hang; this is an environment workaround, not a code change.
- No deviations from the task-context spec; the diff matches the prescribed `IsBoxCodeActiveAsync`/`GetByCodeAsync` bodies verbatim.

## PR Summary

Fixed issue #3887 (box-code uniqueness checked two different ways) at its actual source: `TransportBoxRepository.IsBoxCodeActiveAsync` and `GetByCodeAsync` now derive from the shared `TransportBoxStateRules.OccupiesCodePredicate` instead of restating the active/busy-state partition locally. This is the bug fix — `IsBoxCodeActiveAsync`'s old local allow-list omitted `Quarantine` and `Error`, so a box quarantined mid-flow silently freed its code for reuse. `GetByCodeAsync` also now ranks any code-occupying box ahead of released (`Closed`/`Stocked`) ones by `Id` descending, rather than only demoting `Closed`, so a barcode scan resolves to the box that actually holds the code even when a released `Stocked` row has a higher `Id`.

12 new repository tests cover a full per-state truth table for `IsBoxCodeActiveAsync` plus the `GetByCodeAsync` ordering fix; all pre-existing tests in the file and a 220-test regression sweep across the transport-box area pass unmodified.

### Changes
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` — `IsBoxCodeActiveAsync`/`GetByCodeAsync` rewritten to compose `TransportBoxStateRules.OccupiesCodePredicate`
- `backend/test/Anela.Heblo.Tests/Repositories/TransportBoxRepositoryCaseHandlingTests.cs` — added state-builder helpers and 12 new tests

## Status
DONE
