# Specification: Consolidate `GetGenerationStatsAsync` into a single database query

## Summary
`LeafletGenerationRepository.GetGenerationStatsAsync` currently issues four sequential, independent database round trips to compute leaflet feedback statistics. This method is called on every request to `GET /api/leaflet/feedback/list`, so each page load of the feedback list makes 5 DB calls (1 paged query + 4 stats queries) instead of 2. This spec covers rewriting the method to compute all four aggregates in a single query, with no change to its public contract or callers.

## Background
`GetGenerationStatsAsync` (backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs:55-67) is invoked from `GetLeafletFeedbackListHandler.Handle` (backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs:36) alongside `GetGenerationsPagedAsync`, on every call to the feedback list endpoint. The four values it computes — total row count, count with any feedback, average precision score, average style score — are independent aggregates over the same `LeafletGenerations` table with no cross-query dependency forcing sequential execution. This was flagged by the daily architecture review routine (2026-07-11) as an easy, low-risk performance win: three unnecessary round trips per page load, avoidable with a single aggregate query.

This is a targeted performance fix to one repository method. It does not touch `GetGenerationsPagedAsync`, the handler, the DTOs, or the API contract.

## Functional Requirements

### FR-1: Compute all four stats in a single database round trip
Rewrite `GetGenerationStatsAsync` so that `total`, `withFeedback`, `avgPrecision`, and `avgStyle` are obtained via one query against `_context.LeafletGenerations` instead of four separate `await`ed calls.

Acceptable implementation approaches (in order of preference, per project conventions favoring LINQ/EF over raw SQL where feasible):
1. A LINQ `GroupBy(g => 1)` (or equivalent constant-key grouping) projected with `Select` into an anonymous/tuple type, then materialized with `SingleOrDefaultAsync`, handling the empty-table case (see FR-3).
2. If EF Core cannot translate the required aggregate (e.g., the `FILTER`-style conditional count) into a single SQL query via LINQ, use `FromSqlRaw`/`FromSqlInterpolated` with the query given in the brief:
   ```sql
   SELECT
       COUNT(*) AS total,
       COUNT(*) FILTER (WHERE "PrecisionScore" IS NOT NULL OR "StyleScore" IS NOT NULL) AS with_feedback,
       AVG("PrecisionScore") AS avg_precision,
       AVG("StyleScore") AS avg_style
   FROM "LeafletGenerations";
   ```
   projected into a small private DTO/keyless entity type for materialization.

Whichever approach is used, the query must execute exactly once against the database for this method call.

**Acceptance criteria:**
- `GetGenerationStatsAsync` results in exactly one round trip to the database (verifiable via EF Core query logging or a DB-call counter in an integration test).
- The method signature, return type (`Task<LeafletFeedbackStats>`), and the `ILeafletGenerationRepository` interface are unchanged.
- No changes to `GetGenerationsPagedAsync`, `GetLeafletFeedbackListHandler`, or any DTO/contract type.

### FR-2: Preserve exact existing semantics for all four values
The four computed values must have bit-for-bit identical semantics to the current implementation for every input state:
- `TotalGenerations`: count of all rows in `LeafletGenerations`.
- `TotalWithFeedback`: count of rows where `PrecisionScore IS NOT NULL OR StyleScore IS NOT NULL`.
- `AvgPrecisionScore`: average of `PrecisionScore` over rows where `PrecisionScore IS NOT NULL` (nulls excluded from both numerator and denominator, matching current `Where(...).AverageAsync(...)` behavior).
- `AvgStyleScore`: average of `StyleScore` over rows where `StyleScore IS NOT NULL`, same rule.

**Acceptance criteria:**
- Existing unit tests in `GetLeafletFeedbackListHandlerTests` continue to pass unmodified (they mock the repository, so they validate the handler's use of the returned `LeafletFeedbackStats`, not the query itself).
- New/updated repository-level tests (see FR-4) confirm value parity with the pre-change four-query implementation across representative data sets.

### FR-3: Correct handling of the empty-table case
When `LeafletGenerations` has zero rows, the current implementation returns `TotalGenerations = 0`, `TotalWithFeedback = 0`, `AvgPrecisionScore = null`, `AvgStyleScore = null` (EF Core's `AverageAsync` over an empty filtered set with a nullable selector returns `null`, not an exception, and `CountAsync` returns `0`). The consolidated query must reproduce this exact result rather than throwing or returning `0` for the averages.

**Acceptance criteria:**
- A test against an empty `LeafletGenerations` table returns `LeafletFeedbackStats(0, 0, null, null)` and does not throw.
- A test where no rows have any feedback (so `PrecisionScore`/`StyleScore` are all null) returns `TotalWithFeedback = 0` and both averages `null`, without throwing.

### FR-4: Test coverage for the new query path
Add or update tests that exercise the actual database query (not a mocked repository), since the existing handler tests mock `ILeafletGenerationRepository` and would not catch a regression in the SQL/LINQ itself.

**Acceptance criteria:**
- A repository-level test (using the project's existing EF Core test infrastructure — in-memory provider or test PostgreSQL instance, whichever this project's test suite already uses for repository tests) covers: empty table, table with only feedback-less rows, table with a mix of feedback and non-feedback rows, and a table where only one of `PrecisionScore`/`StyleScore` is populated on some rows.
- If `FromSqlRaw` is used, a test confirms it runs correctly against the project's actual database provider (PostgreSQL), not just against the EF in-memory provider (which does not support raw SQL translation the same way).

## Non-Functional Requirements

### NFR-1: Performance
- Reduce database round trips for `GetGenerationStatsAsync` from 4 to 1.
- No regression in query correctness or response time of `GET /api/leaflet/feedback/list` — the change should only reduce latency (fewer round trips), not alter the shape or volume of data returned.
- No new indexes are required; `LeafletGenerations` is expected to remain a single-table, full-scan aggregate at this data volume. If the table is large enough that a full scan becomes a concern, that is out of scope for this fix (see Out of Scope).

### NFR-2: Security
- No change to authentication, authorization, or data sensitivity — this is an internal aggregate query with no new fields or exposure surface.

## Data Model
No schema or entity changes. `LeafletFeedbackStats` (backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs) remains:

```csharp
public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

This is an internal domain type (not a DTO crossing the API boundary directly — `GetLeafletFeedbackListHandler` maps it into `LeafletFeedbackStatsDto`), so per project convention it may remain a record; no conversion to a class is required by this change.

## API / Interface Design
No public API or contract changes. `GET /api/leaflet/feedback/list` continues to return the same `GetLeafletFeedbackListResponse` shape, including the `Stats` field, with identical values. `ILeafletGenerationRepository.GetGenerationStatsAsync(CancellationToken)` keeps its existing signature. This is purely an internal implementation change to the repository method's query strategy.

## Dependencies
- Entity Framework Core (existing `ApplicationDbContext`).
- Npgsql / PostgreSQL, if the `FromSqlRaw` fallback path is used.
- No new libraries or services.

## Out of Scope
- Any change to `GetGenerationsPagedAsync` or the overall query count for the feedback list endpoint (still 2 queries: paged + stats).
- Caching of stats results across requests.
- Adding indexes or otherwise optimizing the underlying table scan for very large `LeafletGenerations` tables.
- Changes to `GetLeafletFeedbackListHandler`, `LeafletFeedbackStatsDto`, or any frontend consumer of the feedback list endpoint.
- Broader refactors of `LeafletGenerationRepository` beyond this one method.

## Open Questions
None.

## Status: COMPLETE
