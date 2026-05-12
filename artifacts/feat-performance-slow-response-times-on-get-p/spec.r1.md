# Specification: Optimize `GET /api/photobank/photos` response time

## Summary
The paginated photo-listing endpoint `GET /api/photobank/photos` currently averages 13.5 s per call, exceeding the 10 s alerting threshold. This feature optimizes the underlying query, EF Core configuration, and database indexes so the endpoint completes well under 1 s for typical requests without changing its public contract.

## Background
Application Insights (24 h window, 2026-05-09 UTC) flagged `GET Photobank/GetPhotos` with an average and maximum response time of 13 528 ms (>10 000 ms threshold). The endpoint is used by the Photobank gallery in the marketing module to browse, filter, and paginate indexed SharePoint photos. As the photo index grows (tens of thousands of rows), the current query plan degrades sharply.

Review of `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs::GetPhotosAsync` and `backend/src/Anela.Heblo.Persistence/Photobank/PhotoConfiguration.cs` confirms the suspected root causes from the brief:

1. **Non-sargable text search.** Substring search is expressed as
   `(p.FolderPath + "/" + p.FileName).ToLower().Contains(term)`.
   EF Core translates this to `LOWER(folder_path || '/' || file_name) LIKE '%term%'`, which prevents the existing B-tree index on `FolderPath` from being used and forces a sequential scan with a per-row computed expression.
2. **Cartesian eager load.** The query applies `.Include(p => p.Tags).ThenInclude(pt => pt.Tag)` before pagination and is run as a single query. Combined with `OrderByDescending(ModifiedAt).Skip(...).Take(...)`, EF Core 8/9 emits a query that materializes the photo×tag join, then paginates client-side over the root entity — but the underlying SQL still returns many rows per photo, multiplying transfer and parse cost.
3. **Per-tag `EXISTS` subqueries.** Multi-tag filtering loops `query = query.Where(p => p.Tags.Any(pt => pt.Tag.Name == t))`, producing one correlated `EXISTS` per tag. Cost grows multiplicatively with the number of tag filters.
4. **Missing index on `ModifiedAt`.** Sorting requires a full scan + sort of the filtered result set; there is no index that supports `ORDER BY ModifiedAt DESC`.
5. **Change tracking enabled on a read-only path.** The query does not call `AsNoTracking()`, so EF Core builds entity-tracking state for every returned photo and every tag join row.
6. **Two round-trips with the same predicate.** `CountAsync` and the page query each re-evaluate the non-sargable filter.

The fix is mechanical: rewrite the filtering, projection, and pagination to be sargable and tracking-free, add the required indexes, and verify with `EXPLAIN ANALYZE`.

## Functional Requirements

### FR-1: Preserve the existing public contract
The endpoint signature, query parameters, response shape, validation rules, and error codes (`PhotobankInvalidRegexPattern` for SQLSTATE `2201B`) must remain unchanged. Frontend (`frontend/src/api/hooks/usePhotobank.ts`, `PhotobankPage.tsx`) and the generated TypeScript client must not require any modification.

**Acceptance criteria:**
- `GET /api/photobank/photos` accepts the same query parameters: `tags` (repeated), `search`, `useRegex`, `withoutTags`, `page`, `pageSize`.
- `GetPhotosResponse` returns the same fields: `items` (`PhotoDto[]`), `total`, `page`, `pageSize`, plus the standard `BaseResponse` envelope.
- `PhotoDto` fields and `TagDto` source values are identical to current behavior.
- The OpenAPI document (and regenerated TypeScript client) shows no diff against `main` for this endpoint.
- All existing tests in `backend/test/Anela.Heblo.Tests/Features/Photobank/GetPhotosHandlerTests.cs` and `PhotobankRepositoryFilterTests.cs` pass without modification of their assertions.

### FR-2: Make substring search sargable
Replace the `(FolderPath + "/" + FileName).ToLower().Contains(term)` predicate with a query form that PostgreSQL can serve using an index.

**Acceptance criteria:**
- Substring (`useRegex=false`) search is implemented using `EF.Functions.ILike(...)` or equivalent against `FolderPath`/`FileName` (separately or via a generated/expression index).
- The chosen approach matches the same set of photos as today: case-insensitive substring match against the virtual path `"{FolderPath}/{FileName}"`. The matcher tests in `PhotobankRepositoryFilterTests.cs` continue to pass.
- A PostgreSQL `pg_trgm` GIN index supports the substring lookup (preferred), OR the SQL plan demonstrably uses an index for typical search terms (verified via `EXPLAIN ANALYZE`).
- Special-character handling: any LIKE wildcards (`%`, `_`) and the escape character in user-supplied search terms are escaped before being interpolated into the `ILike` pattern.

### FR-3: Eliminate the eager-load cartesian explosion
The repository query must not materialize a cartesian join over `Tags` when paginating.

**Acceptance criteria:**
- The paginated page query uses `AsSplitQuery()` (or projects tag data via a separate query/lookup), so the SQL for the page selects at most `pageSize` photo rows and loads tags in a second statement keyed by the returned photo IDs.
- `EXPLAIN ANALYZE` on a realistic dataset shows no plan node that produces > `pageSize × (avg tags per photo) × <filter set size>` rows.

### FR-4: Optimize multi-tag filtering
A single SQL predicate must enforce the AND-of-tags semantics rather than N stacked `EXISTS` subqueries.

**Acceptance criteria:**
- The filter for `N` tags executes with at most one `EXISTS`/`JOIN ... GROUP BY ... HAVING COUNT(DISTINCT TagId) = N` against `PhotoTags`/`PhotobankTags` (case-insensitive on tag name, normalized as today).
- A photo is returned only if it carries **every** tag in the filter (AND semantics preserved).
- The query plan for a 3-tag filter has no more nested-loop semi-joins than the 1-tag case.

### FR-5: Apply `AsNoTracking()` on read-only paths
`GetPhotosAsync` is a read-only handler; entities should not be tracked.

**Acceptance criteria:**
- The paginated photo query and its associated tag-loading query use `AsNoTracking()` (or `AsNoTrackingWithIdentityResolution()` if needed for tag de-duplication).
- No code path that depends on entity tracking is affected (verified by tests passing).

### FR-6: Add supporting database indexes
New indexes must support sort-by-modified-date and substring search.

**Acceptance criteria:**
- A migration adds an index on `Photos.ModifiedAt DESC` (composite with `Id` for deterministic pagination if needed).
- A migration adds a `pg_trgm` GIN index on `LOWER(FolderPath || '/' || FileName)` (or equivalent expression index) — gated behind `CREATE EXTENSION IF NOT EXISTS pg_trgm`.
- A migration adds an index on `PhotoTags(TagId, PhotoId)` if not already present (verify by inspecting `ApplicationDbContextModelSnapshot.cs`).
- Migration follows the existing project pattern (manual deployment per CLAUDE.md project facts), is reversible, and uses `CREATE INDEX CONCURRENTLY` semantics is **not** required (single-developer project, low write rate on photo table).

### FR-7: Avoid duplicate predicate evaluation between count and page
The total count and the paged select should not evaluate the expensive search/tag predicate twice in incompatible ways.

**Acceptance criteria:**
- The handler executes both statements against the same filtered base query.
- For the common case (no search, no tag filter), `Total` is computed cheaply (e.g., via the index/statistics-friendly `COUNT(*)`).

### FR-8: Regression test for performance
A repeatable benchmark or test guards against future regressions.

**Acceptance criteria:**
- A new test (xUnit, against PostgreSQL via Testcontainers or the existing integration test fixture) seeds at least 10 000 photos with ~30 000 photo-tag rows, then asserts that `GetPhotosAsync` returns within a generous CI-safe bound (e.g., < 1 500 ms) for the default request and for a representative `search`+`tags` request.
- The test is opt-in/marked appropriately if Testcontainers is unavailable locally, but runs in the standard `dotnet test` command on the CI runner.

## Non-Functional Requirements

### NFR-1: Performance
- p50 response time for `GET /api/photobank/photos` (default `pageSize=48`, no filters) ≤ **200 ms** on the production dataset.
- p95 response time across all parameter combinations (search, tags, withoutTags, regex) ≤ **800 ms**.
- The 10 s App Insights alert must not fire for this endpoint for at least 7 consecutive days after deploy.
- Index size growth ≤ 2× current size of the `Photos` table on disk (sanity bound).

### NFR-2: Security
- User-supplied `search` continues to be safely parameterized — no raw SQL concatenation. LIKE wildcards in `search` must be escaped before substitution.
- Regex path (`useRegex=true`) continues to surface invalid patterns as `PhotobankInvalidRegexPattern` rather than 500s. ReDoS risk is mitigated by PostgreSQL's regex engine; no additional client-side regex evaluation is introduced.
- `[Authorize]` remains on the controller action; no auth changes.

### NFR-3: Backwards compatibility
- No breaking changes to the API contract, response codes, or error payloads.
- No frontend changes required.

### NFR-4: Observability
- The handler logs nothing new at info level. On the slow path, a Serilog/`ILogger` debug entry with the elapsed milliseconds and parameter shape (no `search` value) is acceptable but not required.
- Existing Application Insights instrumentation continues to capture duration metrics for the endpoint.

### NFR-5: Code quality
- `dotnet build` passes with no new warnings.
- `dotnet format` passes.
- New code follows project C# conventions (nullable enabled, async with `CancellationToken`, repository interface respected).

## Data Model
No new entities. Existing entities and relationships are unchanged:

- `Photo` (`Photos` table) — has many `PhotoTag`.
- `PhotoTag` (`PhotoTags` join table) — `(PhotoId, TagId)` composite key with `Source` enum.
- `Tag` (`PhotobankTags`).

**Index changes only:**

| Table | Index | Type | Purpose |
| --- | --- | --- | --- |
| `Photos` | `IX_Photos_ModifiedAt` on `(ModifiedAt DESC, Id DESC)` | B-tree | Support `ORDER BY ModifiedAt DESC` pagination. |
| `Photos` | `IX_Photos_PathTrgm` on `LOWER(FolderPath \|\| '/' \|\| FileName) gin_trgm_ops` | GIN (pg_trgm) | Support case-insensitive `ILIKE '%term%'`. |
| `PhotoTags` | `IX_PhotoTags_TagId_PhotoId` on `(TagId, PhotoId)` if absent | B-tree | Support multi-tag `EXISTS`/`GROUP BY` filter. |

Migration must idempotently `CREATE EXTENSION IF NOT EXISTS pg_trgm` before creating the GIN index.

## API / Interface Design

No interface changes. The contract continues to be:

```
GET /api/photobank/photos
  ?tags=foo&tags=bar     // optional, repeatable; AND semantics
  &search=substring      // optional
  &useRegex=false        // default false
  &withoutTags=false     // default false; matches photos with zero tags
  &page=1                // default 1
  &pageSize=48           // default 48

200 OK
{
  "success": true,
  "items": [ { "id": ..., "name": ..., "folderPath": ..., "tags": [...] }, ... ],
  "total": <int>,
  "page": <int>,
  "pageSize": <int>
}

400 Bad Request (validator failure)
200 OK with success=false, errorCode=PhotobankInvalidRegexPattern (invalid regex)
```

Internal refactor surface area:

- `Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — `BuildFilterQuery`, `GetPhotosAsync` (and any other path that uses `BuildFilterQuery` if its semantics for substring search are shared; preserve behavior).
- `Anela.Heblo.Persistence/Photobank/PhotoConfiguration.cs` — new index declarations (or raw-SQL index in migration for the expression/GIN index that EF Core cannot model declaratively).
- New EF Core migration under `Anela.Heblo.Persistence/Migrations/`.

## Dependencies
- **PostgreSQL `pg_trgm` extension.** Must be available on the target database (Azure Database for PostgreSQL allows enabling via `azure.extensions` server parameter — confirm before deploy).
- **EF Core 8+ `AsSplitQuery`** (already in use across the codebase).
- **Npgsql.EntityFrameworkCore.PostgreSQL** (already a dependency).
- No new NuGet packages.

## Out of Scope
- Adding caching (Redis or in-memory) for the photo list — defer; not needed to meet the p95 target.
- Adding cursor-based pagination — `Skip/Take` is acceptable once the sort column is indexed.
- Changing the response shape, adding new query parameters, or adding sort options.
- Frontend changes (e.g., infinite scroll, prefetch).
- Optimizing other Photobank endpoints (`GetTags`, `GetThumbnail`, bulk-tag operations) — those are not flagged by App Insights.
- Backfilling `LastAutoTaggedAt` or any other column.
- Migrating away from `Include`/EF Core to raw SQL or Dapper.

## Open Questions
None.

## Status: COMPLETE