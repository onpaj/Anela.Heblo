# Architecture Review: Optimize `GET /api/photobank/photos` response time

## Skip Design: true

Backend-only performance fix. No UI/UX work — public contract unchanged, no new visual components.

## Architectural Fit Assessment

The feature aligns cleanly with existing patterns. It is a localized refactor confined to a single Vertical Slice (`Features/Photobank`) plus one EF Core migration. All required primitives already exist in the codebase:

- **`EF.Functions.ILike` / `EF.Functions.Like` with LIKE-wildcard escaping** is already used in `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs:173` and `Invoices/IssuedInvoiceRepository.cs:144`. The escape pattern (`\\` → `\\\\`, `%` → `\\%`, `_` → `\\_`) is established — reuse it verbatim.
- **`AsNoTracking()` on read paths** is the standard convention (`LeafletRepository`, `ArticleRepository`, `KnowledgeBaseRepository`, `ManufactureOrderRepository`).
- **Raw-SQL migration for extension + expression index** has precedent in `Migrations/20260302163014_AddKnowledgeBase.cs:58` (pgvector + HNSW), and `suppressTransaction: true` + `CREATE INDEX CONCURRENTLY` + `IF NOT EXISTS` precedent in `Migrations/20260506145627_AddPartialIndexForActiveStockUpOperations.cs`.
- **Testcontainers-based PostgreSQL integration tests** already exist (`KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`, `Catalog/GetStockUpOperationsSummaryIntegrationTests.cs`).

Integration points:
- `PhotobankRepository.BuildFilterQuery` (also called by `CountFilteredPhotosAsync` and `GetFilteredPhotoIdsMissingTagAsync` — those non-paginated paths must remain behaviorally identical).
- `PhotoConfiguration.cs` (composite/expression indexes that EF can model declaratively).
- New migration in `Anela.Heblo.Persistence/Migrations/`.
- `Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (auto-updated by `dotnet ef`).
- No `PhotobankModule.cs` DI changes (same repository interface, same lifetime).
- No `IPhotobankRepository` signature changes — the contract layer is untouched.

**Conflict surfaced during exploration:** Existing `PhotobankRepositoryFilterTests` (and the two related test classes in the same file) use the **EF Core InMemory provider**, which does not translate `EF.Functions.ILike` / `EF.Functions.Like` and will throw `InvalidOperationException` at runtime once the repository switches. FR-1's "all existing tests pass without modification" cannot hold without an infrastructure change — see Specification Amendments.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ API: PhotobankController.GetPhotos (unchanged)                       │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ MediatR
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ GetPhotosHandler (unchanged contract, unchanged DTO mapping)         │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ IPhotobankRepository.GetPhotosAsync
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ PhotobankRepository (refactored — same interface)                    │
│                                                                      │
│  BuildFilterQuery(tags, search, useRegex)                            │
│    ├─ AsNoTracking()                                                 │
│    ├─ Substring search → EF.Functions.ILike(path, "%escaped%")       │
│    │     with LIKE-wildcard escaping                                 │
│    ├─ Regex search → preserve Regex.IsMatch translation              │
│    └─ Multi-tag AND → single GroupBy/HAVING COUNT(DISTINCT)          │
│                                                                      │
│  GetPhotosAsync                                                      │
│    ├─ Phase 1: SELECT page of Photo IDs (filter + ORDER BY +         │
│    │     OFFSET/LIMIT, no Include)  ─── single PK-only round-trip    │
│    ├─ Phase 2: SELECT photos WHERE Id IN (@ids) with                 │
│    │     AsSplitQuery() Include(Tags).ThenInclude(Tag)               │
│    │     → 2 SQL statements, no cartesian explosion                  │
│    ├─ Phase 3: re-order in-memory by ModifiedAt DESC (IDs already    │
│    │     ordered; preserves pagination order across split query)    │
│    └─ Phase 4: CountAsync on the same filtered base query           │
└────────────────────────────────┬─────────────────────────────────────┘
                                 │ EF Core / Npgsql
                                 ▼
┌──────────────────────────────────────────────────────────────────────┐
│ PostgreSQL                                                            │
│  Photos:    new IX_Photos_ModifiedAt_Id (B-tree, DESC)                │
│             new IX_Photos_PathTrgm     (GIN, pg_trgm)                 │
│  PhotoTags: new IX_PhotoTags_TagId_PhotoId (covering)                │
│  Extension: pg_trgm (CREATE EXTENSION IF NOT EXISTS)                  │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Two-phase ID-then-hydrate query vs `AsSplitQuery` alone

**Options considered:**
- (A) Keep one query, add `.AsSplitQuery()`. EF Core 8 issues a separate query for `Tags`, but the root query still applies `OrderBy`, `Skip`, `Take` to the photo table directly.
- (B) Two phases: first query selects only paged photo IDs ordered by `ModifiedAt DESC`; second query hydrates those photos plus their tags via `AsSplitQuery`.

**Chosen approach:** (A) `.AsSplitQuery()` on the existing query, applied after the filter/order/skip/take chain.

**Rationale:** EF Core 8's split-query semantics already handle pagination correctly: the root query runs `SELECT ... FROM Photos WHERE ... ORDER BY ModifiedAt DESC LIMIT pageSize OFFSET (page-1)*pageSize`, and the tag query runs `SELECT ... FROM PhotoTags JOIN ... WHERE PhotoId IN (<root ids>)`. This eliminates the cartesian without introducing a hand-rolled two-phase pattern. (B) only becomes necessary if profiling shows split-query overhead is still problematic — defer per YAGNI.

#### Decision 2: `pg_trgm` GIN index vs `text_pattern_ops` B-tree

**Options considered:**
- (A) GIN index with `gin_trgm_ops` on `LOWER(FolderPath || '/' || FileName)`.
- (B) B-tree with `text_pattern_ops` on `LOWER(...)` — only accelerates prefix searches.
- (C) FTS (`tsvector`) — better ranked search but a contract change in feel.

**Chosen approach:** (A).

**Rationale:** Users search substrings (`%term%`), not prefixes (`term%`). Only trigram GIN supports unanchored `ILIKE '%term%'`. FTS would mean adding ranking, stemming, and column maintenance — outside the spec's scope. GIN index size is bounded (≤ 2× table footprint per NFR-1).

#### Decision 3: Multi-tag AND via `GROUP BY ... HAVING COUNT` vs stacked `EXISTS`

**Options considered:**
- (A) Single subquery: `WHERE Id IN (SELECT PhotoId FROM PhotoTags pt JOIN PhotobankTags t ON ... WHERE LOWER(t.Name) IN (@tags) GROUP BY pt.PhotoId HAVING COUNT(DISTINCT pt.TagId) = @tagCount)`.
- (B) Loop of `.Where(p => p.Tags.Any(...))` — current code.
- (C) Single `.Where(p => p.Tags.Count(pt => normalizedTags.Contains(pt.Tag.Name)) == N)`.

**Chosen approach:** (A), expressed as a LINQ subquery against `_context.PhotoTags`:

```csharp
var matchingIds = _context.PhotoTags
    .Where(pt => normalizedTags.Contains(pt.Tag.Name))
    .GroupBy(pt => pt.PhotoId)
    .Where(g => g.Select(x => x.TagId).Distinct().Count() == normalizedTags.Count)
    .Select(g => g.Key);

query = query.Where(p => matchingIds.Contains(p.Id));
```

**Rationale:** One JOIN with `GROUP BY ... HAVING COUNT(DISTINCT TagId) = N` is cost-linear in tag count, where stacked `EXISTS` is multiplicative. The composite `(TagId, PhotoId)` index supports an index-only scan for this subquery. (C) is also reasonable but EF's translation of `.Count(predicate)` over a navigation collection has been less reliable across EF Core versions.

#### Decision 4: Index for sort — `(ModifiedAt DESC, Id DESC)` not `ModifiedAt` alone

**Chosen approach:** Composite descending index on `(ModifiedAt DESC, Id DESC)`.

**Rationale:** With ties in `ModifiedAt`, pagination needs a deterministic tiebreaker; otherwise `OFFSET` can return overlapping/missing rows across pages. Adding `Id` as a tiebreaker in both the `ORDER BY` clause **and** the index allows the planner to satisfy pagination with an index-only scan. The repository must also be updated to add `.ThenByDescending(p => p.Id)` to the `OrderBy` chain — this is a behavior change visible to clients in tie cases (mention in spec amendment).

#### Decision 5: Where to declare the new indexes — `PhotoConfiguration.cs` vs migration-only

**Chosen approach:**
- B-tree `IX_Photos_ModifiedAt_Id` → declare in `PhotoConfiguration.cs` (`HasIndex(x => new { x.ModifiedAt, x.Id }).IsDescending().HasDatabaseName(...)`); EF migration generates it.
- Composite `IX_PhotoTags_TagId_PhotoId` → declare in `PhotoTagConfiguration.cs` (`HasIndex(x => new { x.TagId, x.PhotoId })`).
- Trigram GIN `IX_Photos_PathTrgm` → raw `migrationBuilder.Sql` only (EF cannot model GIN with expression key). Keep the model in sync by **not** declaring it in `PhotoConfiguration.cs`.

**Rationale:** Mirrors the precedent set by `AddKnowledgeBase` (vector HNSW index via raw SQL while declarative indexes live in entity configuration).

## Implementation Guidance

### Directory / Module Structure

No new directories. Files touched:

```
backend/src/Anela.Heblo.Application/Features/Photobank/
  PhotobankRepository.cs                              ← refactor BuildFilterQuery, GetPhotosAsync

backend/src/Anela.Heblo.Persistence/Photobank/
  PhotoConfiguration.cs                               ← add IX_Photos_ModifiedAt_Id index
  PhotoTagConfiguration.cs                            ← add IX_PhotoTags_TagId_PhotoId index

backend/src/Anela.Heblo.Persistence/Migrations/
  YYYYMMDDHHMMSS_OptimizePhotobankPhotoQuery.cs       ← new migration (declarative + raw SQL)
  YYYYMMDDHHMMSS_OptimizePhotobankPhotoQuery.Designer.cs   ← auto-generated
  ApplicationDbContextModelSnapshot.cs                ← auto-updated

backend/test/Anela.Heblo.Tests/Features/Photobank/
  PhotobankRepositoryFilterTests.cs                   ← migrate from InMemory to Testcontainers
                                                        (see Specification Amendments)
  PhotobankRepositoryPerformanceTests.cs              ← new (FR-8, Testcontainers, [Trait("Category","Integration")])
```

### Interfaces and Contracts

**Unchanged (public):**
- `IPhotobankRepository` signatures.
- `GetPhotosRequest`, `GetPhotosResponse`, `PhotoDto`, `TagDto`.
- `PhotobankController` action.
- `ErrorCodes.PhotobankInvalidRegexPattern` behavior on SQLSTATE `2201B`.

**Internal helper to introduce** (private inside `PhotobankRepository`):

```csharp
private static string EscapeLikePattern(string input) =>
    input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
```

Reuse identical escape logic from `LeafletRepository.cs:173`. Apply before constructing the `$"%{escaped}%"` pattern.

**Index naming convention** (match existing project style):
- `IX_Photos_ModifiedAt_Id`
- `IX_Photos_PathTrgm`
- `IX_PhotoTags_TagId_PhotoId`

### Data Flow

**Default request (no filter, page 1, pageSize 48):**

```
1. PhotobankController.GetPhotos
2. GetPhotosHandler.Handle
3. PhotobankRepository.GetPhotosAsync
   ├─ base = Photos.AsNoTracking()    (no filter applied)
   ├─ total = base.CountAsync()        → COUNT(*) FROM Photos
                                          (uses table-stats heuristic — fast)
   └─ items = base.OrderByDescending(ModifiedAt).ThenByDescending(Id)
              .Skip(0).Take(48)
              .Include(Tags).ThenInclude(Tag)
              .AsSplitQuery()
              .ToListAsync()
              → SQL #1: page of Photos using IX_Photos_ModifiedAt_Id
              → SQL #2: PhotoTags JOIN Tags WHERE PhotoId IN (<48 ids>)
4. Map to DTOs, return.
```

**Filtered request (`search=ruze&tags=featured&tags=hero`):**

```
3. PhotobankRepository.GetPhotosAsync
   ├─ base = Photos.AsNoTracking()
   │     .Where(p => EF.Functions.ILike(p.FolderPath + "/" + p.FileName, $"%{escaped}%", "\\"))
   │            → SQL uses IX_Photos_PathTrgm (GIN)
   │     .Where(p => matchingIdsSubquery.Contains(p.Id))
   │            → SQL uses IX_PhotoTags_TagId_PhotoId with GROUP BY ... HAVING
   ├─ total = base.CountAsync()
   └─ items = base.OrderByDescending(ModifiedAt).ThenByDescending(Id)
              .Skip(...).Take(...)
              .Include(Tags).ThenInclude(Tag)
              .AsSplitQuery()
              .ToListAsync()
```

**Regex request (`useRegex=true`):**

Keep `Regex.IsMatch(p.FolderPath + "/" + p.FileName, pattern, RegexOptions.IgnoreCase)`. Npgsql translates to `~*`. No index supports regex, so this path is necessarily a sequential scan — same as today. Preserve `PostgresException` SQLSTATE `2201B` handling unchanged in `GetPhotosHandler`. Document that regex remains slow (not regressed; only sargable path improved).

### Migration Skeleton

```csharp
public partial class OptimizePhotobankPhotoQuery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Enable pg_trgm (idempotent)
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        // 2. Declarative indexes generated from model snapshot:
        //    IX_Photos_ModifiedAt_Id (B-tree DESC, DESC)
        //    IX_PhotoTags_TagId_PhotoId

        // 3. Trigram GIN index — raw SQL, idempotent
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Photos_PathTrgm"
                ON public."Photos"
                USING GIN (LOWER("FolderPath" || '/' || "FileName") gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS public.\"IX_Photos_PathTrgm\";");
        // Declarative indexes removed by migration generator.
        // Do NOT drop pg_trgm — other features may depend on it later.
    }
}
```

`CREATE INDEX CONCURRENTLY` is **not** required (single-developer project, low write rate on `Photos`); spec FR-6 confirms this.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `PhotobankRepositoryFilterTests` use EF InMemory which does not support `EF.Functions.ILike` — all three test classes in that file will break. | HIGH | Migrate the file to PostgreSQL Testcontainers using the pattern in `KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`. Mark `[Trait("Category", "Integration")]`. Apply migrations to the test container so the GIN index is exercised. (Surfaced as a Specification Amendment below.) |
| `pg_trgm` extension may not be enabled in the Azure target. | HIGH | Pre-deploy check: confirm `pg_trgm` is in the `azure.extensions` allow-list on the Azure Database for PostgreSQL Flexible Server. Document in `docs/integrations/` or a deployment runbook. Migration uses `CREATE EXTENSION IF NOT EXISTS` (requires the running role to have CREATE on the database). If unprivileged, an ops step must be added. |
| `OrderByDescending(ModifiedAt)` without a tiebreaker can yield duplicate/missing rows across pages under ties. | MEDIUM | Add `.ThenByDescending(p => p.Id)` and a composite index covering both. Mention as a (non-breaking) behavior refinement in the spec. |
| GIN index build on a large table (tens of thousands of rows) acquires a `SHARE` lock — blocks writes for the build duration. | LOW | Photo writes happen only via the indexer job. Schedule migration outside indexer-job windows, or accept the brief block. `CREATE INDEX CONCURRENTLY` is available if it becomes problematic; spec explicitly does not require it. |
| `EF.Functions.ILike` on `FolderPath + "/" + FileName` (string concatenation expression) — the planner may not use the expression index unless the predicate matches the indexed expression **byte-for-byte**, including `LOWER(...)`. | MEDIUM | Verify with `EXPLAIN ANALYZE` on a realistic dataset. If the index is not used, switch to `EF.Functions.ILike(EF.Functions.Collate(...))` or use a generated column. Acceptance criterion in FR-2 already requires `EXPLAIN ANALYZE` confirmation. |
| `withoutTags=true` semantics: current code applies it after the tag filter; if both `tags=[..]` and `withoutTags=true` are supplied, the result is always empty. | LOW | Preserve current behavior (no contract change). Add a single test that pins the existing semantics so the refactor cannot silently change them. |
| `BuildFilterQuery` is also called by `CountFilteredPhotosAsync` and `GetFilteredPhotoIdsMissingTagAsync`. Changing its semantics could regress auto-tag flows. | MEDIUM | Refactor must keep the predicate set identical. Existing tests for those paths (`PhotobankAutoTagJobTests`, `RetagPhotosHandlerTests`) must continue to pass without changes. Run them as part of the validation gate. |
| `Total` count uses the same filtered query — the GIN index helps, but `COUNT(*)` on a filtered set is still a scan of matching rows. | LOW | Acceptable. The no-filter case uses index/heap scan and is fast. For filtered cases, the count cost is bounded by the trigram index selectivity. |
| Domain entity `Photo.Tags` is declared `virtual` (suggests lazy loading may be enabled elsewhere). Combined with `AsNoTracking()`, lazy loading on detached entities can throw or silently no-op. | LOW | Verify `ApplicationDbContext` configuration — lazy loading is not enabled in this codebase based on existing repository patterns. If it ever is, `AsNoTrackingWithIdentityResolution()` is the documented mitigation per FR-5. |

## Specification Amendments

1. **FR-1 wording correction.** The spec states existing tests in `PhotobankRepositoryFilterTests.cs` "pass without modification of their assertions." The **assertions** can stay, but the **test infrastructure must change**: those tests use EF Core InMemory, which does not support `EF.Functions.ILike` or `EF.Functions.Like`. Amend FR-1 to:
   > "Existing `PhotobankRepositoryFilterTests` and `PhotobankRepositoryRegexFilterTests` continue to assert the same semantics, but their test infrastructure is migrated to PostgreSQL via Testcontainers following the precedent in `KnowledgeBaseRepositoryIntegrationTests.cs`. Assertions remain identical."

2. **Deterministic pagination tiebreaker.** Add an explicit acceptance criterion under FR-3 (or as new FR-3a):
   > "Pagination is deterministic: `OrderByDescending(p => p.ModifiedAt).ThenByDescending(p => p.Id)`. The supporting B-tree index covers both columns in descending order."

   Today's behavior is non-deterministic on ties — this is a refinement, not a regression of any explicit contract.

3. **Search behavior on empty trimmed string.** Existing test `GetPhotosAsync_emptySearch_doesNotFilter` covers null/empty/whitespace; preserve this in the refactor. Spec implicitly assumes it — make it explicit:
   > "`search` values that are null, empty, or whitespace-only are treated as 'no filter'. Trim before deciding."

4. **pg_trgm prerequisite documentation.** FR-6 mentions the extension must be enabled, but a pre-deploy validation step belongs in Prerequisites (below). Add an explicit acceptance criterion:
   > "Migration verifies extension availability via `CREATE EXTENSION IF NOT EXISTS pg_trgm` and surfaces a clear error if the running role lacks permission. Deployment runbook updated."

5. **`BuildFilterQuery` consumers.** Spec mentions preserving behavior for shared callers; make this an explicit acceptance criterion under FR-1 or FR-2:
   > "All callers of `BuildFilterQuery` (`CountFilteredPhotosAsync`, `GetFilteredPhotoIdsMissingTagAsync`) continue to return identical results before and after the refactor. Verified by existing `PhotobankAutoTagJobTests` and `RetagPhotosHandlerTests` passing without modification."

## Prerequisites

Before implementation can be merged/deployed:

1. **`pg_trgm` extension allowed on Azure PostgreSQL.** Confirm `pg_trgm` is present in the `azure.extensions` server parameter for the target Azure Database for PostgreSQL Flexible Server. If not, request it via Azure portal / IaC and wait for the server reload before deploying the migration.
2. **Database role privileges.** The application's connection-string role must have `CREATE` privilege on the target database to run `CREATE EXTENSION IF NOT EXISTS pg_trgm`. If a separate admin role is required (common on managed PostgreSQL), enable the extension manually as a one-time DBA step and remove `CREATE EXTENSION` from the migration (replace with a `DO` block that no-ops if already present).
3. **Testcontainers prerequisites.** Docker (or Podman with `TestcontainersSettings.ResourceReaperEnabled = false`) must be available on the CI runner that executes integration tests. The existing `KnowledgeBaseRepositoryIntegrationTests` confirms this is already in place.
4. **EF Core tooling.** `dotnet ef migrations add OptimizePhotobankPhotoQuery --project Anela.Heblo.Persistence --startup-project Anela.Heblo.API`. Verify the resulting designer + model snapshot contain the new declarative indexes and that the raw-SQL GIN index is present in the `Up` method.
5. **Baseline benchmark.** Capture current production response time distribution (p50/p95) before deploy so NFR-1's success criteria can be evaluated post-deploy.
6. **Deployment runbook entry.** Per CLAUDE.md project facts ("Database migrations are manual"), the manual migration step must be added to the release checklist: run migration, run `ANALYZE public."Photos"` and `ANALYZE public."PhotoTags"` to refresh planner statistics, then verify `EXPLAIN ANALYZE` shows index usage on a representative query.