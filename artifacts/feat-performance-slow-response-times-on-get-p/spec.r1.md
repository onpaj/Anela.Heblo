# Specification: Photobank GetTags performance fix

## Summary
The `GET /api/photobank/tags` endpoint averaged **11 065 ms** in the last 24 hours, breaching the 10 000 ms SLO threshold. This feature reduces tag-listing latency to a sub-second target by rewriting the EF Core query, adding the missing index that supports the per-tag photo count, and putting the result behind a short-lived in-memory cache that is invalidated on tag / photo-tag mutations.

## Background
`GetTagsHandler` returns every tag in the Photobank along with the number of photos that carry each tag. The result feeds tag filter dropdowns and admin screens on the photobank UI.

Current implementation (`PhotobankRepository.GetTagsWithCountsAsync`):

```csharp
var results = await _context.PhotobankTags
    .Select(t => new { Tag = t, Count = t.PhotoTags.Count })
    .OrderByDescending(x => x.Count)
    .ToListAsync(cancellationToken);
```

Observed issues:

1. **Correlated sub-query per tag.** EF Core translates `t.PhotoTags.Count` to a correlated `SELECT COUNT(*) FROM "PhotoTags" WHERE "TagId" = t."Id"` for every tag row. This is the classic N+1-ish shape on the SQL side.
2. **Missing supporting index.** `PhotoTags` has a composite PK `(PhotoId, TagId)` only. There is no standalone index on `TagId`, which is the column the correlated count filters on. Each per-tag count therefore scans the index for `PhotoId` order, not for `TagId`.
3. **No pagination, no caching.** The handler always returns the full tag set and recomputes it on every request, even though tags change rarely compared to read traffic.
4. **No projection of only required columns.** EF tracks full `Tag` entities even though only `Id`, `Name`, and the computed `Count` reach the DTO.

App Insights evidence (2026-05-09 UTC nightly analysis):

- Occurrences in last 24 h: 1
- Avg response time: 11 065 ms
- Max response time: 11 065 ms

The single-occurrence sample size is low signal on its own, but the structural problems in the query are sufficient justification to fix it now rather than wait for repeat incidents.

## Functional Requirements

### FR-1: Single-statement tag count query
Rewrite `GetTagsWithCountsAsync` so that all tag rows and their photo counts are produced by a single SQL statement using a `LEFT JOIN` + `GROUP BY` (or equivalent EF Core grouping) instead of a per-tag correlated sub-query.

**Acceptance criteria:**
- Executing the endpoint produces exactly **one** SQL statement against `PhotobankTags` / `PhotoTags` (verifiable via EF Core query logging or `MiniProfiler`).
- Tags with zero associated photos are returned with `Count = 0` (current behavior must be preserved).
- The ordering contract is unchanged: results are sorted by `Count` descending; ties are broken by `Tag.Name` ascending (newly clarified — see Open Questions resolution below).
- The handler still returns the same `GetTagsResponse` shape (`Tags: List<TagWithCountDto>`).

### FR-2: Database index on `PhotoTags.TagId`
Add a non-clustered index on `PhotoTags.TagId` to support the grouped count query and any future per-tag filters.

**Acceptance criteria:**
- A new EF Core migration adds `IX_PhotoTags_TagId` on the `PhotoTags` table.
- The migration is reversible (`Down` removes the index).
- `dotnet ef migrations script` produces a valid `CREATE INDEX` statement against PostgreSQL.
- Migration is documented in `docs/integrations/` or release notes per the manual-migration workflow described in `CLAUDE.md`.

### FR-3: Cached tag list with explicit invalidation
Wrap `GetTagsWithCountsAsync` in an in-memory cache (`IMemoryCache`) with a short TTL so repeat reads do not re-execute the query.

**Acceptance criteria:**
- First call after process start (or after invalidation) hits the database; subsequent calls within the TTL window return the cached payload without a DB round-trip (verifiable in integration test via `IDbCommandInterceptor`).
- Cache key is constant (`photobank:tags:with-counts`) — the endpoint has no inputs that affect the result.
- TTL: **60 seconds** absolute expiration (rationale in NFR-1; tunable via `appsettings.json`).
- Cache is invalidated immediately on the following handlers/operations:
  - `CreateTagHandler`
  - `DeleteTagHandler`
  - `AddPhotoTagHandler`
  - `RemovePhotoTagHandler`
  - `BulkAddPhotoTagHandler`
  - `BulkAddPhotoTagByIdsHandler`
  - `ReapplyRulesHandler`
  - `RetagPhotosHandler`
  - `PhotobankAutoTagJob` (after each batch that mutates tags)
- Invalidation removes the cache entry; it does not pre-warm. The next read populates the cache.

### FR-4: Projection to DTO inside the query
Project directly to `TagWithCountDto` (or an internal record) in the EF Core query so EF Core does not materialize tracked `Tag` entities.

**Acceptance criteria:**
- The generated SQL selects only `Id`, `Name`, and `COUNT(...)` — no other tag columns.
- `ChangeTracker.Entries<Tag>()` is empty after the handler executes (verifiable in test).
- Existing unit/integration tests for `GetTagsHandler` still pass.

### FR-5: Logging and observability
Emit a structured log entry on every cache miss containing the row count returned and the elapsed milliseconds, so future regressions are easy to attribute.

**Acceptance criteria:**
- Log entry uses `ILogger<GetTagsHandler>` at `LogLevel.Information` with structured fields `TagCount` and `ElapsedMs`.
- Cache hits do **not** log at `Information` level (avoid log spam); a `Debug` level entry is acceptable.
- No PII or tag names are logged — only counts.

## Non-Functional Requirements

### NFR-1: Performance
- p95 latency for `GET /api/photobank/tags` ≤ **500 ms** measured against staging with realistic data volumes.
- p99 latency ≤ **1 000 ms**.
- A cached response p95 ≤ **50 ms**.
- The new query must execute in ≤ 200 ms on the production database at current data volume (tag count expected in the low hundreds; `PhotoTags` row count expected in the tens of thousands — confirm before rollout).
- 60-second cache TTL chosen to balance UI freshness (users tagging photos see counts update within ~1 min) against load reduction. Tunable via configuration.

### NFR-2: Security
- Endpoint remains behind `[Authorize]` — unchanged.
- No new PII or sensitive data is introduced; tag names are non-sensitive identifiers.
- Query uses EF Core parameterization throughout — no string concatenation into SQL.
- Cache scope is per-process; no cross-tenant concerns (single-tenant application).

### NFR-3: Backward compatibility
- Response contract (`GetTagsResponse`, `TagWithCountDto`) is unchanged. No frontend or OpenAPI client regeneration required for callers.
- Migration is additive (new index only). No data migration needed.

### NFR-4: Testability
- The query rewrite has unit/integration coverage that asserts:
  - Tags with zero photos return `Count = 0`.
  - Tags are ordered by `Count` desc then `Name` asc.
  - Only one SQL statement is issued.
- The cache layer has integration tests covering: cache hit, cache miss after TTL, invalidation on each mutating handler listed in FR-3.
- Coverage on changed files remains ≥ 80% per the global testing rule.

## Data Model
No schema changes other than the new index.

Relevant existing tables (PostgreSQL, schema `public`):

- `PhotobankTags` — `Id` (PK), `Name` (unique, max 100). Index: `IX_PhotobankTags_Name`.
- `PhotoTags` — `(PhotoId, TagId)` composite PK, `Source` (enum string, max 20), `CreatedAt` (UTC timestamp).
  - Existing index: composite PK on `(PhotoId, TagId)`.
  - **New:** `IX_PhotoTags_TagId` non-clustered index on `(TagId)`.

Domain types remain unchanged (`Tag`, `PhotoTag`).

## API / Interface Design

### Endpoint (unchanged)
```
GET /api/photobank/tags
Authorization: Bearer <token>
```

### Response (unchanged)
```json
{
  "tags": [
    { "id": 12, "name": "summer", "count": 1843 },
    { "id": 7,  "name": "products", "count": 1201 }
  ],
  "success": true,
  "error": null
}
```

### Internal changes
- `IPhotobankRepository.GetTagsWithCountsAsync` rewritten to use group-by/projection. Signature unchanged.
- `GetTagsHandler` gains an `IMemoryCache` dependency and an `IPhotobankTagsCache` (thin wrapper around `IMemoryCache` with a typed `Get`/`Set`/`Invalidate` interface). The wrapper lives in `Anela.Heblo.Application/Features/Photobank/Services/` and is registered as a singleton.
- All mutating handlers listed in FR-3 take `IPhotobankTagsCache` as a constructor dependency and call `Invalidate()` after a successful `SaveChangesAsync`.

### Configuration
`appsettings.json` (and environment overrides):

```json
{
  "Photobank": {
    "TagsCache": {
      "TtlSeconds": 60
    }
  }
}
```

Bound via the Options pattern (`PhotobankTagsCacheOptions`).

## Dependencies
- **EF Core / Npgsql** — already in use; no new packages.
- **`Microsoft.Extensions.Caching.Memory`** — already transitively available in ASP.NET Core; register `IMemoryCache` via `services.AddMemoryCache()` if not already registered (verify in `Program.cs`).
- **PostgreSQL** — target index supports the rewritten query.
- No external services involved.

## Out of Scope
- Distributed caching (Redis / SQL distributed cache). The application runs as a single Azure Web App for Containers instance; in-memory caching is sufficient. Revisit when horizontal scale-out is on the roadmap.
- Pagination for the tag list. Tag counts are expected in the low hundreds; pagination is unnecessary and would complicate the UI's filter dropdowns.
- Changes to other Photobank endpoints (`GET /api/photobank/photos`, `GET /api/photobank/roots`, etc.). Their performance is not flagged in the brief.
- Frontend changes. The response contract is preserved; no UI work is required.
- Telemetry dashboards or alert thresholds. Existing nightly App Insights analysis already covers regression detection.
- Removal of the existing `GetTagByIdAsync` `.Include(t => t.PhotoTags)` call — this is a separate handler path that is not implicated in the slow endpoint.

## Open Questions
None.

## Status: COMPLETE
