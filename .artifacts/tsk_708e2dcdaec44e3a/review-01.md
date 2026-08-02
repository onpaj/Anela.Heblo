# Review: inject `TimeProvider` into the four recurring DQT jobs

## Verdict: done

## What was checked

Read `plan-01.md`, `design-01.md`, `development-01.md`, then independently verified the actual diff (`git diff HEAD~1 HEAD`) against both — did not rely on the development report's claims.

## Spec conformance (plan-01.md FR-1..FR-4)

- **FR-1** (constructor injection): confirmed in all four job files — `TimeProvider timeProvider` param added in the position specified (before `logger`), `_timeProvider` field added, assigned in constructor. No other constructor signature changes.
- **FR-2** (date derivation): confirmed exact substitutions per file, matching the before/after table in `design-01.md` verbatim:
  - `InvoiceDqtJob.cs`: `DateTime.Today.AddDays(-1)` → `_timeProvider.GetUtcNow().DateTime).AddDays(-1)`
  - `StockWriteBackDqtJob.cs`: same transformation
  - `ProductPairingDqtJob.cs`: `DateTime.Today` → `_timeProvider.GetUtcNow().DateTime`
  - `LotStockReconciliationDqtJob.cs`: `DateTime.UtcNow` → `_timeProvider.GetUtcNow().DateTime` (this one was UTC-correct already but inconsistent with the others' clock *source*; now unified)
  - No remaining `DateTime.Today`/`DateTime.Now`/`DateTime.UtcNow` in any of the four files.
- **FR-3** (no DI change needed): confirmed no `ServiceCollectionExtensions.cs` diff — `TimeProvider.System` singleton registration already covers the new parameter, same as `DqtYesterdayStatusTile` already resolves.
- **FR-4** (tests): confirmed all four test classes updated — `Mock<TimeProvider>` field added, wired into constructor, `.Setup(x => x.GetUtcNow()).Returns(FixedNow)`; new `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock` fact per file asserting `DqtRun.DateFrom`/`DateTo` against the fixed-clock-derived expected date. `FixedNow = 2026-08-02T00:30:00Z` straddles UTC midnight, which is a real regression guard against a wall-clock/local-time implementation.

## Architecture adherence

Matches `DqtYesterdayStatusTile`'s existing pattern exactly (constructor-injected `TimeProvider`, `GetUtcNow().DateTime`), per `docs/architecture/DateTime_StandardizationGuide.md`'s "never use local time" / UTC-only rule cited in the original issue. No new abstractions introduced, no scope creep — only the four cited files and their tests changed.

## Correctness — independently re-verified, not just trusted

- `dotnet build` on `Anela.Heblo.Application.csproj`: **0 errors** (pre-existing warnings only, none newly introduced by the diff).
- `dotnet test --filter "FullyQualifiedName~DataQuality"`: **115/115 passed**, including the 4 new `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock` facts.
- `dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes` scoped to the 4 changed source files: **clean, exit 0**, no formatting diffs.
- Diffed every changed line via `git diff HEAD~1 HEAD` — no unrelated lines touched (`Metadata` blocks, `[AutomaticRetry]`, logging calls, enabled-check/persist/run sequence all untouched, as claimed).

## Completeness

All four jobs identified in the original issue evidence are covered. No job was missed, no job over-changed. Tests were explicitly required by the plan and are present with meaningful (UTC-midnight-boundary) assertions.

## Non-blocking notes (not grounds for request_changes)

- None. The implementation is a clean, surgical, behavior-preserving-under-UTC fix that closes the actual defect (local-time/UTC clock-source inconsistency across the four jobs) exactly as scoped.
