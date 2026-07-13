# Architecture Review: Consolidate `GetGenerationStatsAsync` into a single database query

## Skip Design: true

## Architectural Fit Assessment

This is a self-contained performance fix to a single repository method inside the `Leaflet` vertical slice. It touches no module boundary, no contract/DTO surface, and no other handler. `LeafletGenerationRepository` sits in `Anela.Heblo.Persistence`, implements `ILeafletGenerationRepository` (defined in `Anela.Heblo.Domain`), and is consumed only by `GetLeafletFeedbackListHandler`. The spec's own scoping (no interface change, no DTO change, no handler change) is correct and should be preserved exactly.

More importantly, this is not a novel pattern for the codebase — it is a repeat of a problem already solved once. `IssuedInvoiceRepository.GetSyncStatsAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:35-73`) computes a near-identical shape of stats (a total count, two conditional counts, and a max) over one table, using:

```csharp
var stats = await query
    .GroupBy(x => 1)
    .Select(g => new
    {
        Total = g.Count(),
        Synced = g.Count(x => x.IsSynced),
        WithErrors = g.Count(x => x.ErrorType.HasValue),
        Critical = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired),
        LastSyncTime = g.Max(x => (DateTime?)x.LastSyncTime)
    })
    .FirstOrDefaultAsync(cancellationToken);

if (stats == null) { /* return zeroed struct */ }
```

This is verified as a single SQL round trip by `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` (`backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`), which runs against a real Postgres container (via `PostgresSharedContainerFixture` + `Testcontainers.PostgreSql`) with a `DbCommandInterceptor` counting emitted SQL commands. There is **no** existing use of `FromSqlRaw`/`FromSqlInterpolated` anywhere in the codebase — I grepped the whole `backend/` tree and found zero matches. Introducing raw SQL here would be a new pattern with no precedent, extra maintenance surface (hand-written SQL bypassing the EF change-tracking/translation pipeline), and no compensating benefit, since the LINQ `GroupBy(g => 1)` approach already proven in `IssuedInvoiceRepository` handles exactly this class of problem (multiple independent aggregates + a conditional count) as a single query against Npgsql/PostgreSQL.

**Verdict: adopt the `GroupBy(g => 1)` LINQ projection, mirroring `IssuedInvoiceRepository.GetSyncStatsAsync` verbatim in structure.** Do not use raw SQL — the brief's `FromSqlRaw` suggestion was speculative ("if EF Core cannot translate..."), and the sibling repository proves it can.

## Proposed Architecture

### Component Overview

No new components. One method body changes:

- `Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — `GetGenerationStatsAsync` rewritten to a single `GroupBy(g => 1).Select(...).FirstOrDefaultAsync(...)` query.

No changes to `ILeafletGenerationRepository`, `LeafletFeedbackStats`, `GetLeafletFeedbackListHandler`, or any contract/DTO type.

### Key Design Decisions

#### Decision 1: LINQ `GroupBy(g => 1)` projection vs. `FromSqlRaw`

**Options considered:**
1. LINQ `GroupBy(g => 1)` with an anonymous-type `Select` (counts + conditional counts + averages), materialized via `FirstOrDefaultAsync`.
2. `FromSqlRaw`/`FromSqlInterpolated` with the hand-written `FILTER (WHERE ...)` query from the brief, projected into a private keyless entity/DTO.

**Chosen approach:** Option 1 — LINQ `GroupBy(g => 1)`.

**Rationale:**
- Direct precedent in this codebase (`IssuedInvoiceRepository.GetSyncStatsAsync`) proves EF Core + Npgsql translates `GroupBy(x => 1)` with mixed `Count()`, conditional `Count(predicate)`, and nullable aggregate selectors into a single SQL statement — no `FromSqlRaw` needed.
- `Count(predicate)` inside a `GroupBy` projection is the LINQ equivalent of the brief's `COUNT(*) FILTER (WHERE ...)` — EF Core's Npgsql provider translates `g.Count(x => cond)` to a `count(...) FILTER (WHERE ...)` or `SUM(CASE WHEN ...)` expression itself; no manual SQL needed to get that behavior.
- Zero existing `FromSqlRaw`/`FromSqlInterpolated` usage in `backend/` — introducing it here would add a new, unprecedented pattern (raw SQL string vs. LINQ) for no measurable benefit, and raw SQL cannot be exercised at all against the EF In-Memory provider used by the existing `LeafletGenerationRepositoryTests`, forcing every test of this method onto the (slower, container-based) Postgres integration path with no LINQ fallback for quick unit coverage.
- Keeps the method typed and refactor-safe (column renames caught by the compiler / EF model, not a string).

#### Decision 2: Averages via nullable-selector `Average`, not `Where(...).Average(...)`

**Chosen approach:** Inside the single `GroupBy(g => 1)` group (which contains *all* rows, not a filtered subset), use `g.Average(x => (double?)x.PrecisionScore)` and `g.Average(x => (double?)x.StyleScore)`. LINQ's `Average` over a nullable selector already ignores `null` values in both numerator and denominator, and returns `null` when every value in the group is `null` (or the group is empty) — this reproduces the current `Where(g => g.PrecisionScore != null).AverageAsync(...)` semantics exactly (FR-2, FR-3) without a second filtered subquery. This mirrors how the sibling `GetSyncStatsAsync` already uses `g.Max(x => (DateTime?)x.LastSyncTime)` on a nullable column within the same all-rows group and lets `Max` naturally skip nulls.

#### Decision 3: Empty-table handling via `FirstOrDefaultAsync` null check

**Chosen approach:** `GroupBy(g => 1)` produces zero groups when the underlying query set is empty, so `FirstOrDefaultAsync` returns `null` in that case — exactly the same shape `IssuedInvoiceRepository.GetSyncStatsAsync` already handles (see its `if (stats == null) { return zeroed; }` branch). Copy that guard: if `stats == null`, return `new LeafletFeedbackStats(0, 0, null, null)` directly rather than trying to make the aggregate query itself produce a synthetic zero row.

## Implementation Guidance

### Directory / Module Structure

No new files/folders required. Single-method edit in place:

```
backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs
```

### Interfaces and Contracts

Unchanged. `ILeafletGenerationRepository.GetGenerationStatsAsync(CancellationToken)` keeps its exact signature and return type (`Task<LeafletFeedbackStats>`). `LeafletFeedbackStats` (`backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs`) is an internal domain record consumed only inside the handler that maps it to `LeafletFeedbackStatsDto` — per `docs/architecture/development_guidelines.md`'s DTO rules, the "DTOs are classes, never records" rule applies to contract types in `contracts/`/`Response`/`Request` objects crossing the API boundary, not to this internal domain type, so no conversion is needed (the spec is correct on this point).

### Data Flow

Unchanged: `GET /api/leaflet/feedback/list` → `GetLeafletFeedbackListHandler.Handle` → `GetGenerationsPagedAsync` (1 query) + `GetGenerationStatsAsync` (now 1 query instead of 4) → response mapping. Total DB round trips per request: 5 → 2.

Suggested replacement body (structure only — mirror `IssuedInvoiceRepository.GetSyncStatsAsync`'s shape):

```csharp
public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
{
    var stats = await _context.LeafletGenerations
        .GroupBy(g => 1)
        .Select(g => new
        {
            Total = g.Count(),
            WithFeedback = g.Count(x => x.PrecisionScore != null || x.StyleScore != null),
            AvgPrecision = g.Average(x => (double?)x.PrecisionScore),
            AvgStyle = g.Average(x => (double?)x.StyleScore),
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (stats is null)
        return new LeafletFeedbackStats(0, 0, null, null);

    return new LeafletFeedbackStats(stats.Total, stats.WithFeedback, stats.AvgPrecision, stats.AvgStyle);
}
```

### Test Guidance

- **Existing unit tests** (`backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletGenerationRepositoryTests.cs`, EF In-Memory provider) are unaffected — they don't currently cover `GetGenerationStatsAsync`. `GroupBy(g => 1)` translates fine against the In-Memory provider (it evaluates client-side), so basic value-correctness tests (empty table, mixed feedback, one-sided feedback) can be added there using the existing `IDisposable` + `UseInMemoryDatabase` pattern already in that file — no new test infrastructure needed for value-correctness coverage (FR-2, FR-3).
- **Round-trip-count verification** (FR-1, FR-4's "confirms it runs correctly against the project's actual database provider") requires the real Npgsql/PostgreSQL path, since In-Memory doesn't exercise SQL translation or round-trip counting meaningfully. Follow the established convention exactly: add a `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests` class modeled on `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` —
  - `[Collection("PostgresIntegration")]`, `IAsyncLifetime`, injecting `PostgresSharedContainerFixture` (`backend/test/Anela.Heblo.Tests/Common/PostgresSharedContainerFixture.cs` — shared Testcontainers Postgres 16 instance, one fresh database per test class via `CreateDatabaseAsync`).
  - A minimal `CREATE TABLE public."LeafletGenerations" (...)` with only the columns the method touches (`Id`, `PrecisionScore`, `StyleScore`), matching how the invoice/photobank sibling tests scope their schema.
  - The same `CapturingCommandInterceptor : DbCommandInterceptor` pattern (or extract a shared one if repetition across 4+ SqlShapeTests classes starts to bother you — optional, not required by this spec) registered via `.AddInterceptors(...)` on the `DbContextOptionsBuilder<ApplicationDbContext>.UseNpgsql(...)`.
  - One fact asserting `interceptor.Commands.Should().HaveCount(1)` after calling `GetGenerationStatsAsync`.
  - One fact asserting correct values across a representative data set (empty table, all-null-feedback rows, mixed rows, one-sided PrecisionScore/StyleScore-only rows) — covers FR-4's four scenarios in one seeded table plus an empty-table variant.

This satisfies FR-4 without inventing new test infrastructure — it is a direct copy of a pattern already used four times in this test suite (`Invoices`, `Photobank`, `Purchase`, `GridLayouts`/`Smartsupp` variants under the `PostgresIntegration` collection).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Average` inside `GroupBy(g => 1)` doesn't skip nulls the way `Where(...).AverageAsync(...)` does, subtly changing computed averages | Medium | Covered by Decision 2's reasoning (nullable-selector `Average` semantics are defined to skip nulls) and by FR-2/FR-4 test coverage comparing against representative mixed data sets before/after. |
| EF Core/Npgsql fails to translate `Count(predicate)` combined with `Average(nullable selector)` in the same `GroupBy` projection into one SQL statement, silently falling back to client-side evaluation (pulling all rows) | Low | Directly mitigated by the SqlShapeTest's `Commands.Should().HaveCount(1)` assertion — if translation fails, that test fails loudly rather than the regression going unnoticed. The sibling `IssuedInvoiceRepository` already proves this combination (`Count()`, `Count(predicate)`, `Max(nullable selector)`) translates as one query; `Average` is the same family of aggregate. |
| Empty-table / all-null-feedback edge cases regress silently since they're not exercised by current handler-level mocked tests | Low | FR-3's explicit empty-table and all-null test cases, added at the repository level per FR-4, close this gap directly. |

## Specification Amendments

None required — the spec's own "order of preference" (LINQ first, raw SQL only as a fallback) already points at the right answer; this review just resolves the choice concretely in favor of LINQ `GroupBy(g => 1)`, using `IssuedInvoiceRepository.GetSyncStatsAsync` as the concrete template to copy (both for the query shape and for the corresponding `*SqlShapeTests` test class), rather than leaving that choice open for the implementer to redecide.

## Prerequisites

None. `Testcontainers.PostgreSql`, `PostgresSharedContainerFixture`, and the `PostgresIntegration` xUnit collection are already wired up and used by multiple existing test classes — no new package references or fixture scaffolding needed.
