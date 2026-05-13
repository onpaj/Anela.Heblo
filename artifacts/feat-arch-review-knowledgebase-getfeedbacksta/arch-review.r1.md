# Architecture Review: SQL-Side Aggregation for `GetFeedbackStatsAsync`

## Skip Design: true

## Architectural Fit Assessment

The change lives entirely inside one method of one repository (`KnowledgeBaseRepository.GetFeedbackStatsAsync`) and is a textbook fit for the project's existing Clean Architecture layering:

- **Domain layer** (`Anela.Heblo.Domain/Features/KnowledgeBase`): no change — `IKnowledgeBaseRepository` contract and `FeedbackAggregateStats` DTO are stable.
- **Persistence layer** (`Anela.Heblo.Persistence/KnowledgeBase`): only the body of `GetFeedbackStatsAsync` is rewritten.
- **Application layer** (`GetFeedbackListHandler`): completely unaffected.
- **Frontend / OpenAPI client**: no contract surface change, so no client regeneration.

Integration points consist of two EF Core entity sets that already exist (`KnowledgeBaseQuestionLogs`) and the configured Npgsql provider, which translates `CountAsync` and `AverageAsync(l => (double?)x, ct)` to native `COUNT(*)` and `AVG(x)` SQL. The pattern is already used by other repository methods in the same file (`CountAsync` at lines 91 and 290), so there is no novel infrastructure.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Application layer                                           │
│   GetFeedbackListHandler — unchanged                        │
│     └── calls IKnowledgeBaseRepository.GetFeedbackStatsAsync│
└─────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│ Domain layer (contracts only)                               │
│   IKnowledgeBaseRepository    — unchanged signature         │
│   FeedbackAggregateStats DTO  — unchanged shape             │
└─────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│ Persistence layer                                           │
│   KnowledgeBaseRepository.GetFeedbackStatsAsync             │
│     │ 1. CountAsync(all)            → SELECT COUNT(*)       │
│     │ 2. CountAsync(predicate)      → SELECT COUNT(*) WHERE │
│     │ 3. AverageAsync(double?)      → SELECT AVG(col)::dbl  │
│     │ 4. AverageAsync(double?)      → SELECT AVG(col)::dbl  │
│     └ rounding/null handling done in C# from 4 scalars      │
└─────────────────────────────────────────────────────────────┘
                                │
                                ▼
                       PostgreSQL (Npgsql)
```

### Key Design Decisions

#### Decision 1: Four sequential aggregate queries vs. a single `GroupBy(_ => 1)` projection
**Options considered:**
- A) Four sequential `CountAsync` / `AverageAsync` calls.
- B) A single `_context.KnowledgeBaseQuestionLogs.GroupBy(_ => 1).Select(g => new { ... })` projection that pulls all four scalars in one round-trip.

**Chosen approach:** A — four sequential calls.

**Rationale:** Both translate to constant-size aggregate scans, so the dominant cost is fixed-overhead round-trips; against the project's PostgreSQL database four short round-trips are negligible and well within the < 100 ms p95 budget. Option B is harder to read, harder to test (one composite query is more brittle to EF Core translation changes), and provides no measurable benefit at this table size and call frequency. KISS wins — pick the readable form. If profiling later shows the round-trips dominate, consolidating into a `GroupBy(_=>1)` projection is a one-method refactor with no contract impact.

#### Decision 2: Nullable `AverageAsync(l => (double?)l.PrecisionScore, ct)` vs. guarded `AverageAsync(double)`
**Options considered:**
- A) `AverageAsync(l => (double?)l.PrecisionScore, ct)` over a filtered set — returns `null` for empty filter.
- B) `AnyAsync` guard followed by `AverageAsync(l => (double)l.PrecisionScore.Value, ct)`.

**Chosen approach:** A.

**Rationale:** Matches the existing in-memory code's null-on-empty semantics exactly, requires one round-trip instead of two, and avoids `InvalidOperationException` from `AverageAsync` on an empty sequence (a known EF Core gotcha). The nullable-double cast is the canonical EF Core idiom for this case.

#### Decision 3: Keep the four DB calls outside a transaction
**Options considered:**
- A) Read each scalar without a transaction (current style for this repository).
- B) Wrap the four reads in an explicit serializable transaction for a consistent snapshot.

**Chosen approach:** A.

**Rationale:** This is a stats header — a row inserted between calls produces at most one off-by-one in `TotalQuestions` vs. `TotalWithFeedback`, which is acceptable for an informational summary that is recomputed every page load. A transaction adds locking cost and complexity for no user-visible benefit.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in place:

- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — replace lines 299–322 (`GetFeedbackStatsAsync` body).

Add or extend tests:

- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs` — extend `SetupSchemaAsync` to also create the `KnowledgeBaseQuestionLogs` table (the existing schema-setup script only creates `KnowledgeBaseDocuments` and `KnowledgeBaseChunks` — this is a real gap that blocks adding the spec's required tests). Then add a `Feedback` region with the four scenarios listed in FR-4.

No new files in `Anela.Heblo.Tests/KnowledgeBase/Repository/`. Co-locating with the existing integration test class keeps the Testcontainers Postgres fixture shared, which dominates runtime — splitting would double container boot time for ~four new tests.

### Interfaces and Contracts

Unchanged:

```csharp
// Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs
Task<FeedbackAggregateStats> GetFeedbackStatsAsync(CancellationToken ct = default);

// Anela.Heblo.Domain/Features/KnowledgeBase/FeedbackAggregateStats.cs — unchanged class shape
public class FeedbackAggregateStats
{
    public int TotalQuestions { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
```

Implementation contract for `GetFeedbackStatsAsync` body:

1. Must call exactly four async EF Core aggregation methods.
2. Each call must pass the `CancellationToken` parameter.
3. No `ToListAsync`, `ToArrayAsync`, `AsEnumerable`, or row materialisation.
4. Average calls must use the nullable double cast pattern: `AverageAsync(l => (double?)l.PrecisionScore, ct)`.
5. Round the average using `Math.Round(value, 1)` (default `MidpointRounding.ToEven`) — do not change to `AwayFromZero`, as the spec requires exact UI parity.

### Data Flow

For the Feedback page stats header:

```
HTTP GET /knowledge-base/feedback?...
  ▼
GetFeedbackListHandler.Handle()
  ├─ await _repository.GetFeedbackLogsPagedAsync(...)   ← unchanged
  └─ await _repository.GetFeedbackStatsAsync(ct)        ← rewritten body
        │
        ├─ Round-trip 1: SELECT COUNT(*) FROM "KnowledgeBaseQuestionLogs"
        ├─ Round-trip 2: SELECT COUNT(*) FROM ... WHERE "PrecisionScore" IS NOT NULL OR "StyleScore" IS NOT NULL
        ├─ Round-trip 3: SELECT AVG(CAST("PrecisionScore" AS double precision)) FROM ... WHERE "PrecisionScore" IS NOT NULL
        └─ Round-trip 4: SELECT AVG(CAST("StyleScore" AS double precision)) FROM ... WHERE "StyleScore" IS NOT NULL
        │
        └─ build FeedbackAggregateStats with Math.Round(_, 1) on the two averages
  ▼
maps stats into FeedbackStatsDto (unchanged shape)
  ▼
JSON response to frontend
```

Bytes over the wire: four scalar results, regardless of table size.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF Core / Npgsql translation of `AverageAsync(l => (double?)l.PrecisionScore, ct)` returns `numeric` boxed differently across providers and produces a runtime cast error. | Low | Already used by EF Core community as a standard pattern with Npgsql; the integration test added per FR-4 against the real PostgreSQL Testcontainer catches any provider translation regression at build-time. |
| Test gap is hidden because the existing `KnowledgeBaseRepositoryIntegrationTests.SetupSchemaAsync` does not create the `KnowledgeBaseQuestionLogs` table. Writing the new tests against an in-memory or mocked context would not exercise the actual SQL translation. | High | Extend `SetupSchemaAsync` to also create `KnowledgeBaseQuestionLogs` (id PK, Question text, Answer text, PrecisionScore int null, StyleScore int null, and other columns needed by the entity mapping). Tests for FR-4 must run inside `KnowledgeBaseRepositoryIntegrationTests` against the real Postgres container, not against `UseInMemoryDatabase`, since InMemory does not enforce server-side aggregation semantics. |
| Rounding behaviour change if the implementer reaches for `MidpointRounding.AwayFromZero` "to be safe", silently changing user-facing numbers (e.g. an average of 3.55 currently rounds to 3.6 with banker's rounding when the prior tail is exactly .55 — but to 3.6 also with AwayFromZero; the divergence shows up at values like 2.5 → 2 vs 3). | Medium | Acceptance test in FR-4 includes a known-input rounding assertion comparing against the prior implementation's exact behaviour (`Math.Round(x, 1)` with default `ToEven`). Reviewer must reject any explicit `MidpointRounding` argument. |
| Future schema or column-rename change breaks a column-name-dependent test without an obvious failure signal. | Low | Test asserts on the public DTO values, not on captured SQL strings — schema renames trigger compile errors in the LINQ expressions, not silent test passes. |
| Race window between the four round-trips inflates one count over the other (e.g., row inserted between read 1 and read 2 makes `TotalWithFeedback > TotalQuestions` impossible but skews other comparisons). | Negligible | Accepted by Decision 3. Stats are advisory, recomputed on every page load. |

## Specification Amendments

The spec is implementable as written, with one addition:

1. **Integration test schema setup is a prerequisite, not an afterthought.** The spec's FR-4 silently assumes a test harness exists for `KnowledgeBaseQuestionLogs`. It does not. Add an explicit sub-task to FR-4: *"Extend `KnowledgeBaseRepositoryIntegrationTests.SetupSchemaAsync` to create the `KnowledgeBaseQuestionLogs` table with the columns needed by the `KnowledgeBaseQuestionLog` entity mapping (`Id`, `Question`, `Answer`, `TopK`, `SourceCount`, `DurationMs`, `CreatedAt`, `UserId`, `PrecisionScore`, `StyleScore`, `FeedbackComment`). Without this, the new tests cannot run against PostgreSQL and FR-1's 'no `SELECT *`' assertion is unverifiable."*

2. **Pin the test harness explicitly.** Update FR-4 to state that the tests live in `KnowledgeBaseRepositoryIntegrationTests` (Testcontainers PostgreSQL), not in a Moq-only unit test against the repository. EF Core InMemory provider does not translate `AverageAsync` to SQL the same way Npgsql does and would defeat the purpose of FR-1's SQL-translation acceptance criterion.

3. **No need for the `GroupBy(_=>1)` alternative.** Decision 1 above resolves this. Strike the "Implementer may consolidate into a single round-trip via a `GroupBy(_ => 1).Select(...)` projection" sentence from the spec to avoid implementer ambiguity.

No other amendments. No new properties, no caching, no index changes.

## Prerequisites

- No database migration. The `KnowledgeBaseQuestionLogs` table already exists with the required columns (`PrecisionScore int?`, `StyleScore int?`).
- No configuration or DI changes.
- No frontend OpenAPI client regeneration (`FeedbackAggregateStats` shape is unchanged).
- **Test infrastructure prerequisite:** `KnowledgeBaseRepositoryIntegrationTests.SetupSchemaAsync` must be extended to create the `KnowledgeBaseQuestionLogs` table before any FR-4 tests are written. This is the single non-trivial groundwork item in the change.