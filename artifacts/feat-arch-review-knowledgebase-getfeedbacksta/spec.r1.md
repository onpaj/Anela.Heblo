# Specification: Replace In-Memory Aggregation in `GetFeedbackStatsAsync` with SQL-Side Aggregation

## Summary
`KnowledgeBaseRepository.GetFeedbackStatsAsync` currently materialises every feedback-bearing row into application memory before averaging two scalar values in C#. This spec replaces the in-memory aggregation with EF Core SQL-side `Count`/`Average` queries to eliminate an unbounded data transfer that grows with every `/ask` call and is invoked each time the Feedback page loads its stats header.

## Background
The KnowledgeBase module logs every question/answer pair to `KnowledgeBaseQuestionLogs`, including full `Question` and `Answer` text columns. Users can score responses with `PrecisionScore` and `StyleScore` (1–5). The Feedback page displays a stats header summarising:

- Total number of questions asked
- Total number of questions with any user feedback
- Average precision score
- Average style score

Current implementation at `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs:299-322`:

```csharp
var withFeedback = await _context.KnowledgeBaseQuestionLogs
    .Where(l => l.PrecisionScore != null || l.StyleScore != null)
    .ToListAsync(ct);

double? avgPrecision = withFeedback.Count > 0
    ? withFeedback.Where(l => l.PrecisionScore != null).Average(l => (double?)l.PrecisionScore)
    : null;
```

The `.ToListAsync(ct)` materialises full rows — including large `Question` and `Answer` text columns — for the sole purpose of computing two scalar averages. As `KnowledgeBaseQuestionLogs` grows monotonically with usage, the cost of every Feedback page load grows linearly with the entire history of feedback rows. The SQL engine can compute these aggregates in a single index scan; the cost should be effectively constant relative to result size.

This is a concrete performance regression path identified by the arch-review routine on 2026-05-13.

## Functional Requirements

### FR-1: Replace in-memory aggregation with SQL-side aggregation
`GetFeedbackStatsAsync` MUST compute all four scalar values (`TotalQuestions`, `TotalWithFeedback`, `AvgPrecisionScore`, `AvgStyleScore`) via EF Core aggregation methods (`CountAsync`, `AverageAsync`) that translate to SQL `COUNT`/`AVG`. No code path may materialise log rows for the purpose of computing these statistics.

**Acceptance criteria:**
- No call to `.ToListAsync(...)`, `.ToArrayAsync(...)`, `.AsEnumerable()`, or equivalent appears in `GetFeedbackStatsAsync`.
- EF Core query log (verified at debug level with a SQL provider, or via an integration test capturing executed SQL) shows only aggregate queries — no `SELECT *` or column projection that includes `Question`/`Answer` columns from `KnowledgeBaseQuestionLogs`.
- Each query passes the `CancellationToken` provided to the method.

### FR-2: Preserve existing return contract
The returned `FeedbackAggregateStats` MUST be functionally identical to the current implementation for every input state.

**Acceptance criteria:**
- `TotalQuestions` = total row count of `KnowledgeBaseQuestionLogs`.
- `TotalWithFeedback` = count of rows where `PrecisionScore IS NOT NULL OR StyleScore IS NOT NULL`.
- `AvgPrecisionScore` = average of non-null `PrecisionScore`, rounded to 1 decimal place; `null` when no precision scores exist.
- `AvgStyleScore` = average of non-null `StyleScore`, rounded to 1 decimal place; `null` when no style scores exist.
- Rounding uses `Math.Round(value, 1)` (default banker's rounding — same as today) to preserve existing UI numbers exactly.
- The DTO shape `FeedbackAggregateStats` is unchanged. No new properties added, none removed.

### FR-3: Empty-table behaviour
When the table is empty, or when no rows have scores, the method MUST NOT throw.

**Acceptance criteria:**
- Empty table → returns `{ TotalQuestions: 0, TotalWithFeedback: 0, AvgPrecisionScore: null, AvgStyleScore: null }`.
- No precision scores anywhere → `AvgPrecisionScore: null` (other fields computed normally).
- No style scores anywhere → `AvgStyleScore: null` (other fields computed normally).
- This requires using the nullable overload of `AverageAsync` (`Average(l => (double?)l.PrecisionScore)`) so an empty filter set returns `null` rather than throwing `InvalidOperationException`.

### FR-4: Test coverage
Add or update tests proving the new behaviour.

**Acceptance criteria:**
- Unit/integration test for empty table → all-zero/null result, no exception.
- Test for table with rows but zero feedback → `TotalQuestions > 0`, `TotalWithFeedback = 0`, both averages `null`.
- Test for table with mixed feedback (some precision-only, some style-only, some both, some none) → correct counts and averages, both rounded to 1 decimal.
- Test asserts rounding behaviour matches the prior implementation for a known input.
- Existing tests touching `GetFeedbackStatsAsync` continue to pass without semantic changes.

## Non-Functional Requirements

### NFR-1: Performance
- Time complexity for the stats endpoint MUST be O(log n) or better given an index on the relevant columns, and MUST NOT scale linearly with row count in materialised bytes transferred.
- Bytes transferred from database to application per call MUST be constant (four scalar results), independent of `KnowledgeBaseQuestionLogs` size.
- Acceptable latency target for the stats endpoint: < 100 ms p95 on the production dataset.

### NFR-2: Security
- No change to authorization model — the method is already only reachable through the existing Feedback page handler. No new endpoints introduced.
- Cancellation token propagation MUST be preserved on every async DB call to allow request cancellation.
- No raw user input is composed into SQL; all queries are parameterised through EF Core's expression translation.

### NFR-3: Backward compatibility
- Public method signature `Task<FeedbackAggregateStats> GetFeedbackStatsAsync(CancellationToken ct = default)` is unchanged.
- `FeedbackAggregateStats` DTO shape is unchanged.
- No database schema migration is required.
- No API contract change visible to the frontend client.

## Data Model
No schema changes. The implementation reads existing columns on `KnowledgeBaseQuestionLogs`:

| Column           | Type     | Used for                                           |
|------------------|----------|----------------------------------------------------|
| `Id`             | PK       | (Implicit row count.)                              |
| `PrecisionScore` | int?     | Filter, average.                                   |
| `StyleScore`     | int?     | Filter, average.                                   |

A composite or filtered index on `(PrecisionScore, StyleScore)` would further improve aggregation performance, but is **out of scope** for this change (see Out of Scope). If profiling after this change shows the aggregation queries are still slow on production volume, an index can be added in a follow-up.

## API / Interface Design
No public API changes. Internal repository method body is rewritten.

Proposed implementation (illustrative — final shape may use a single `GroupBy(_ => 1)` projection or four separate calls; either is acceptable provided FR-1 through FR-3 are met):

```csharp
public async Task<FeedbackAggregateStats> GetFeedbackStatsAsync(CancellationToken ct = default)
{
    var totalQuestions = await _context.KnowledgeBaseQuestionLogs
        .CountAsync(ct);

    var totalWithFeedback = await _context.KnowledgeBaseQuestionLogs
        .CountAsync(l => l.PrecisionScore != null || l.StyleScore != null, ct);

    var avgPrecision = await _context.KnowledgeBaseQuestionLogs
        .Where(l => l.PrecisionScore != null)
        .AverageAsync(l => (double?)l.PrecisionScore, ct);

    var avgStyle = await _context.KnowledgeBaseQuestionLogs
        .Where(l => l.StyleScore != null)
        .AverageAsync(l => (double?)l.StyleScore, ct);

    return new FeedbackAggregateStats
    {
        TotalQuestions = totalQuestions,
        TotalWithFeedback = totalWithFeedback,
        AvgPrecisionScore = avgPrecision.HasValue ? Math.Round(avgPrecision.Value, 1) : null,
        AvgStyleScore = avgStyle.HasValue ? Math.Round(avgStyle.Value, 1) : null,
    };
}
```

Implementer may consolidate into a single round-trip via a `GroupBy(_ => 1).Select(...)` projection if EF Core translates it cleanly for the configured provider; otherwise the four-call form above is acceptable since each call is a constant-size aggregate and the round-trip cost is dominated by the network, not query cost.

## Dependencies
- EF Core (already in use by the repository) — relies on its translation of `CountAsync` and `AverageAsync` with predicate lambdas.
- Existing `KnowledgeBaseQuestionLogs` `DbSet<>` registration in `AppDbContext` — no change.
- Existing test harness for repository tests (xUnit + the project's standard EF Core test setup).

No new packages, services, or configuration entries are introduced.

## Out of Scope
- Adding new database indexes on `PrecisionScore` / `StyleScore`. This is a follow-up optimisation if performance is still insufficient after this change.
- Changes to the Feedback page UI, the MediatR handler, or the API controller surface.
- Caching the stats result (e.g. in-memory or distributed cache). The point of this fix is that the underlying DB query becomes cheap enough not to need caching.
- Schema changes or DTO additions (e.g. p50/p95 score, score distribution histogram).
- Other unrelated performance issues elsewhere in `KnowledgeBaseRepository` or the module.
- Migrating other repositories away from in-memory aggregation patterns. Only the method named in the brief is changed.

## Open Questions
None.

## Status: COMPLETE