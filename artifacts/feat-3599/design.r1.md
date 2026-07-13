# Design: Consolidate `GetGenerationStatsAsync` into a single database query

## Component Design

No new components are introduced. This change is scoped to the body of a single existing method; all surrounding boundaries and contracts are unchanged.

- **`LeafletGenerationRepository`** (`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs`) — implements `ILeafletGenerationRepository`. Its `GetGenerationStatsAsync(CancellationToken)` method is rewritten internally to issue one query instead of four. Responsibility, signature, and return type (`Task<LeafletFeedbackStats>`) are unchanged.
  - Internal query strategy: `_context.LeafletGenerations.GroupBy(g => 1).Select(...)` producing a single anonymous-type projection (`Total`, `WithFeedback`, `AvgPrecision`, `AvgStyle`), materialized with `FirstOrDefaultAsync`. This mirrors the existing precedent in `IssuedInvoiceRepository.GetSyncStatsAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:35-73`).
  - Null-group guard: if the grouped query yields no row (empty table), the method returns `new LeafletFeedbackStats(0, 0, null, null)` directly, rather than relying on the query to synthesize a zero row.
  - No raw SQL (`FromSqlRaw`/`FromSqlInterpolated`) is used — the LINQ `GroupBy(g => 1)` approach is adopted per the architecture review, as it has direct precedent in the codebase and keeps the query typed and testable against the EF In-Memory provider.

- **`ILeafletGenerationRepository`** (`Anela.Heblo.Domain`) — unchanged. `GetGenerationStatsAsync(CancellationToken)` keeps its exact signature and return type.

- **`GetLeafletFeedbackListHandler`** (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs`) — unchanged. Continues to call `GetGenerationsPagedAsync` and `GetGenerationStatsAsync` and map the result into `LeafletFeedbackStatsDto` exactly as before. Total DB round trips per request to `GET /api/leaflet/feedback/list` drop from 5 to 2 (1 paged query + 1 stats query, versus 1 paged query + 4 stats queries previously).

- **Test additions (no new production components):**
  - Value-correctness tests added to the existing `LeafletGenerationRepositoryTests` (EF In-Memory provider) covering: empty table, all-null-feedback rows, mixed feedback/non-feedback rows, and one-sided (`PrecisionScore`-only or `StyleScore`-only) rows.
  - A new `LeafletGenerationRepositoryGetGenerationStatsSqlShapeTests` class, modeled directly on `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests`, using `PostgresSharedContainerFixture` and a `DbCommandInterceptor` to assert exactly one SQL command is issued, plus value-parity assertions against the same representative data sets on the real Npgsql/PostgreSQL provider.

## Data Schemas

No schema, entity, or contract changes. Included for completeness since the query shape depends on them:

**Underlying table (unchanged):** `LeafletGenerations` — only `Id`, `PrecisionScore` (nullable), and `StyleScore` (nullable) are read by this method.

**Return type (unchanged domain record):**
```csharp
public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

**Internal query projection shape** (not a persisted or contract type — an anonymous type scoped to the method body):
```csharp
new
{
    Total = g.Count(),
    WithFeedback = g.Count(x => x.PrecisionScore != null || x.StyleScore != null),
    AvgPrecision = g.Average(x => (double?)x.PrecisionScore),
    AvgStyle = g.Average(x => (double?)x.StyleScore),
}
```

**Semantics preserved exactly (FR-2/FR-3):**
- `Total`: count of all rows.
- `WithFeedback`: count of rows where `PrecisionScore IS NOT NULL OR StyleScore IS NOT NULL`.
- `AvgPrecision` / `AvgStyle`: average over non-null values only, `null` when the table is empty or all values in the column are null (nullable-selector `Average` semantics), matching the current `Where(...).AverageAsync(...)` behavior bit-for-bit.

**API / contract surface:** No change. `GET /api/leaflet/feedback/list` continues to return the existing `GetLeafletFeedbackListResponse` shape (including `Stats` as `LeafletFeedbackStatsDto`) with identical values, computed via one fewer round trip.
