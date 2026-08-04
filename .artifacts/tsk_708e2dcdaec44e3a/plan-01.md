# Plan: DataQuality DQT jobs — replace local-time `DateTime.Today`/`DateTime.UtcNow` window derivation with injected `TimeProvider`

## Summary

Four recurring DQT (Data Quality Test) jobs each derive their own check-window date inline, using three different clock sources — three via local-time `DateTime.Today` and one via `DateTime.UtcNow` directly. This makes the "which day are we checking" boundary depend on the container's local timezone for three of the four jobs, inconsistent with the module's own `DqtYesterdayStatusTile`, which already does this correctly via injected `TimeProvider.GetUtcNow()`. Bring all four jobs onto that same pattern.

## Context

`docs/architecture/DateTime_StandardizationGuide.md` mandates UTC everywhere in business logic and forbids local time. `DateTime.Today` is `DateTime.Now.Date`, a local-kind value — a direct violation. If the container's `TZ` is ever changed from UTC (e.g. to Prague time, the operator's zone), the three `DateTime.Today`-based jobs would compute "yesterday"/"today" against local wall-clock date while `LotStockReconciliationDqtJob` (already on `DateTime.UtcNow`) would not — producing off-by-one date windows between DQT checks that run within a few hours of each other near midnight, and comparisons that silently target the wrong day. It also makes these four jobs impossible to unit-test for date-boundary behavior, since there is no way to inject a fixed clock — the same limitation cited and accepted for other code in PR #3334, but avoidable here since the tile in this same module already demonstrates the fix (`TimeProvider` is already registered in DI and injectable).

## Functional requirements

**FR-1 — Inject `TimeProvider` into all four DQT job classes.**
- `InvoiceDqtJob`, `StockWriteBackDqtJob`, `ProductPairingDqtJob`, `LotStockReconciliationDqtJob` each gain a `TimeProvider timeProvider` constructor parameter, stored as `_timeProvider`, alongside their existing dependencies.
- Acceptance: constructors compile with the added parameter; no other constructor signature changes.

**FR-2 — Replace ad hoc date derivation with `_timeProvider.GetUtcNow()`, matching `DqtYesterdayStatusTile`.**
- `InvoiceDqtJob.ExecuteAsync`: `DateOnly.FromDateTime(DateTime.Today.AddDays(-1))` → `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1)`.
- `StockWriteBackDqtJob.ExecuteAsync`: same transformation as above.
- `ProductPairingDqtJob.ExecuteAsync`: `DateOnly.FromDateTime(DateTime.Today)` → `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)`.
- `LotStockReconciliationDqtJob.ExecuteAsync`: `DateOnly.FromDateTime(DateTime.UtcNow)` → `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)`.
- Acceptance: no remaining reference to `DateTime.Today` or `DateTime.UtcNow` in any of the four job files; each resolved date is byte-for-byte what `TimeProvider.System.GetUtcNow()` would have produced today (i.e. behavior-preserving when running with the real system clock, since it's already UTC-based on all four — this is a source-of-truth consolidation, not a behavior change under normal operation).

**FR-3 — No DI registration changes needed.**
- `TimeProvider` is already resolvable from the container (proven by `DqtYesterdayStatusTile`'s existing constructor injection working today). Constructor injection of `TimeProvider` into the four jobs picks up the same registration automatically — no `Startup`/`Program.cs`/module registration edit required.
- Acceptance: `dotnet build` succeeds with no new DI registration; jobs resolve via the existing Hangfire/recurring-job DI wiring the same way they did before.

**FR-4 — Update existing unit tests to construct jobs with a controllable `TimeProvider` and assert on the resolved date.**
- `InvoiceDqtJobTests`, `StockWriteBackDqtJobTests`, `ProductPairingDqtJobTests`, `LotStockReconciliationDqtJobTests` each currently construct the SUT with mocked repository/runner/status-checker only (see `InvoiceDqtJobTests.cs:19-23`). Add a `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`, already a common .NET 8 testing dependency — check `backend/test/**/*.csproj` for whether it's already referenced elsewhere in the test project; if not, evaluate adding the package vs. hand-rolling a minimal `TimeProvider` subclass, and record the decision) set to a fixed instant, pass it into the constructor.
- Add/extend a test per job asserting the persisted `DqtRun.DateFrom`/`DateTo` matches the date derived from the fixed `TimeProvider` instant (not the real wall clock), including a case straddling a UTC midnight boundary (e.g. fixed time `2026-08-02T00:30:00Z` for the "yesterday" jobs, confirming the window resolves to `2026-08-01`, not `2026-08-02`).
- Acceptance: new/updated tests fail against the old `DateTime.Today`/`DateTime.UtcNow` implementation (i.e. they're meaningful regression guards) and pass against the fixed implementation; `dotnet test` green.

## Non-functional requirements

- **Consistency**: all four jobs and the existing tile now share one clock-derivation pattern; no remaining direct `DateTime.Now`/`DateTime.Today`/`DateTime.UtcNow` calls in the DQT jobs directory.
- **No behavior change under normal (UTC container) operation**: since the container currently runs in UTC, `DateTime.Today` and `DateTime.UtcNow` today resolve to the same date as `TimeProvider.System.GetUtcNow()` would — this change is a latent-bug fix (protects against a future TZ change), not a functional date shift today. Confirm this assumption holds (check container/deployment TZ config) so the change is understood as risk-elimination, not a live bugfix.
- **Testability**: the four jobs become independently testable for date-boundary logic without wall-clock dependency, closing the gap noted as accepted-limitation in PR #3334.

## Data model

No data model changes. `DqtRun.Start(...)` already takes `dateFrom`/`dateTo` as `DateOnly` parameters — only the computation feeding those parameters changes, not `DqtRun`'s shape or persistence.

## Interfaces

No API, contract, or UI changes. This is confined to:
- 4 job classes: `InvoiceDqtJob.cs`, `StockWriteBackDqtJob.cs`, `ProductPairingDqtJob.cs`, `LotStockReconciliationDqtJob.cs` (all in `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/`)
- 4 corresponding test files in `backend/test/Anela.Heblo.Tests/Features/DataQuality/`

## Dependencies and scope

**In scope**: the four job classes listed above and their unit tests.

**Explicitly out of scope**:
- `DqtYesterdayStatusTile.cs` — already correct, used as the reference pattern; not touched.
- Any other module's `DateTime.Now`/`.Today` usage outside DataQuality — a separate, broader cleanup if one is warranted, not part of this fix.
- Changing job cron schedules, Hangfire configuration, or `RecurringJobMetadata`.
- Adding a `TimeProvider` DI registration — not needed, it already exists (see FR-3).
- Behavior of `IInvoiceDqtJobRunner`/`IDriftDqtJobRunner` internals — the jobs only compute the window and hand it off; the runners are unaffected.

## Rough plan

1. Confirm `TimeProvider` DI registration currently in place (grep `Program.cs`/`Startup`/module registration for `AddSingleton<TimeProvider>` or equivalent) — document what's found, no change expected.
2. Edit the four job classes: add `TimeProvider` constructor parameter + `_timeProvider` field; replace the local-time/direct-UtcNow date computation with `_timeProvider.GetUtcNow().DateTime` per FR-2's exact mapping.
3. Update the four existing test files: construct jobs with a `FakeTimeProvider` (or minimal test double) set to a fixed, deliberately non-midnight-safe instant; add/extend an assertion on `DqtRun.DateFrom`/`DateTo` tied to that fixed instant, including one boundary-straddling case per job.
4. Run `dotnet build` and `dotnet format` on the backend; run the full `Anela.Heblo.Tests` DataQuality test subset (`dotnet test --filter "FullyQualifiedName~DataQuality"`) plus the full suite to check for regressions.
5. Grep the four job files afterward to confirm zero remaining `DateTime.Today`/`DateTime.Now`/direct `DateTime.UtcNow` references.

## Open questions

- **Test double source**: is `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) already referenced in the test project? If not, default to adding it (it's the standard .NET testing package for this exact purpose) rather than hand-rolling a subclass — note this as a small new test-only dependency if so.
- **Container TZ assumption**: this plan asserts the deployed container currently runs in UTC (so this is a risk-elimination fix, not a live date-shift bug). Default assumption: true, per `docs/architecture/DateTime_StandardizationGuide.md`'s "never use local time" rule implying UTC is the standing convention — worth a one-line confirmation during implementation (e.g. check `Dockerfile`/`docker-compose.yml` for `TZ` env var) but not blocking.
- **`ProductPairingDqtJob`'s `[AutomaticRetry(Attempts = 0, ...)]` attribute**: unrelated to this fix, left untouched — flagged only so the implementer doesn't mistake it for something needing alignment with the other three jobs.
