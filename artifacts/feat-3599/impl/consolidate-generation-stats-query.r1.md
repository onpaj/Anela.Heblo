# Implementation: consolidate-generation-stats-query

## What was implemented
Rewrote `LeafletGenerationRepository.GetGenerationStatsAsync` to compute `total`, `withFeedback`, `avgPrecision`, and `avgStyle` in a single database round trip using `GroupBy(g => 1)` + an anonymous-type `Select` + `FirstOrDefaultAsync`, replacing the previous four sequential `CountAsync`/`AverageAsync` calls. Public signature, return type (`LeafletFeedbackStats`), and all callers (`GetLeafletFeedbackListHandler`, `ILeafletGenerationRepository`) are unchanged. Added unit tests proving value parity with the old implementation and a new Postgres integration test class proving the single-round-trip guarantee.

## Files created/modified
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — `GetGenerationStatsAsync` body replaced with the `GroupBy(g => 1)` single-query shape; null-group guard returns `LeafletFeedbackStats(0, 0, null, null)` for an empty table. No other methods touched.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryTests.cs` — added 3 new `[Fact]` methods (EF In-Memory provider): empty-table zeroed stats, all-rows-without-feedback (zero `TotalWithFeedback`, null averages), and mixed rows (asserting exact totals and manually-computed averages for one-sided/both-sided/no-feedback rows).
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs` (new) — Postgres Testcontainers SQL-shape test class modeled on `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`, using `PostgresSharedContainerFixture` and a `DbCommandInterceptor` to assert exactly one SQL command is emitted, correct values against a real database (5-row seed), and a third fact (`GetGenerationStatsAsync_EmptyTable_ReturnsZeroedStatsWithoutThrowing`) that truncates the table and asserts zeroed/null stats without throwing (option (a) from the task spec).

## Tests
- `LeafletGenerationRepositoryTests` (EF In-Memory, `--filter "FullyQualifiedName~LeafletGenerationRepositoryTests"`): **6/6 passed** (3 pre-existing + 3 new).
- `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests` (Postgres Testcontainers, 3 facts): **could not run** in this sandbox — Docker daemon is not reachable (`docker info` reports `failed to connect to the docker API at unix:///var/run/docker.sock ... no such file or directory`). Verified this is a pre-existing environment limitation, not something introduced by this change, by running the sibling `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` (untouched, pre-existing file) with the same filter — it fails identically with the same Testcontainers/Docker error. Code compiles and follows the proven sibling pattern exactly.
- `GetLeafletFeedbackListHandlerTests` (regression check, mocks the repository): **15/15 passed**, confirming the handler contract is unchanged.
- Full `dotnet build` (`Anela.Heblo.sln`): **0 errors** (250 pre-existing warnings, none related to this change).
- `dotnet format Anela.Heblo.sln --verify-no-changes`: **no diffs reported**.

## How to verify
```
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LeafletGenerationRepositoryTests" --no-build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletFeedbackListHandlerTests" --no-build
# Requires a running Docker daemon:
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests" --no-build
```

## Notes
- Docker/Testcontainers is unavailable in this sandbox (`/var/run/docker.sock` does not exist), so the new Postgres-backed SQL-shape tests could not be executed here. This is an environment limitation confirmed against the pre-existing, unmodified sibling test (`IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests`), which fails with the identical Docker connection error — i.e., not a regression or defect introduced by this change. These tests should be run in an environment with Docker (e.g., CI or local dev) before merge to get full confidence on the "exactly one SQL command" assertion.
- `git diff --stat` (repo root, after staging) confirms only the three intended files changed under `backend/`: `LeafletGenerationRepository.cs`, `LeafletGenerationRepositoryTests.cs`, and the new `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs`. `artifacts/feat-3599/state.json` had pipeline-owned changes present in the working tree but was deliberately left uncommitted, per instructions not to touch `artifacts/`.

## PR Summary
This change consolidates `LeafletGenerationRepository.GetGenerationStatsAsync` from four sequential database queries into a single round trip, using the same `GroupBy(g => 1)` LINQ aggregation shape already proven in `IssuedInvoiceRepository.GetSyncStatsAsync`. The method is called on every `GET /api/leaflet/feedback/list` request alongside the paged generations query, so this reduces that endpoint from 5 DB round trips to 2 per page load. The public method signature, return type, and all call sites are unchanged — this is a pure internal implementation change. Test coverage was added at two levels: EF In-Memory unit tests in the existing `LeafletGenerationRepositoryTests.cs` proving value parity with the old four-query implementation across empty-table, no-feedback, and mixed-feedback scenarios; and a new Postgres Testcontainers integration test class, `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests`, asserting the query emits exactly one SQL command and returns correct values against a real PostgreSQL database, including an empty-table case. The Postgres-backed tests could not be executed in this sandbox because Docker is unavailable here, but the same limitation was confirmed against an existing, unmodified sibling test class, so it is an environment gap rather than a defect in this change.

### Changes
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — `GetGenerationStatsAsync` rewritten to a single `GroupBy(g => 1)` aggregation query instead of four sequential queries.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryTests.cs` — added 3 EF In-Memory facts covering empty table, no-feedback rows, and mixed feedback rows.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests.cs` (new) — Postgres Testcontainers SQL-shape tests: single-SQL-command assertion, real-database value correctness, and empty-table (truncated) zeroed-stats assertion.

## Status
DONE_WITH_CONCERNS
