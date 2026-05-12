# Architecture Review: Photobank GetTags Performance Fix

## Skip Design: true

Backend-only performance fix. The `GetTagsResponse` / `TagWithCountDto` contract is preserved verbatim; the only change at the API boundary is faster responses. No new UI surfaces, no visual changes, no design system implications.

## Architectural Fit Assessment

The proposal lands cleanly on existing conventions in `backend/src/Anela.Heblo.Application/Features/Photobank/`:

- **Vertical Slice + MediatR**: `GetTagsHandler` retains its shape; mutating handlers get one new dependency.
- **Repository pattern**: query rewrite stays inside `PhotobankRepository.GetTagsWithCountsAsync`. Signature already returns `IReadOnlyList<TagCount>` (a domain record at `Domain/Features/Photobank/TagCount.cs`), which keeps the cache payload free of tracked EF entities.
- **`IMemoryCache` precedent**: `Application/Features/Catalog/Cache/SalesCostCache` and `MaterialCostCache` follow the same passive Get/Set/Invalidate wrapper shape that is in place at `Services/PhotobankTagsCache.cs`.
- **Manual migration workflow**: matches `CLAUDE.md` (`Database migrations are manual`). Photobank already has precedent under `Persistence/Migrations/`.

Current state inspection (worktree HEAD `9dfba28f`):

- ✅ `IPhotobankTagsCache` / `PhotobankTagsCache` implemented and DI-registered (`PhotobankModule.cs:35-38`).
- ✅ `PhotobankTagsCacheOptions` bound from configuration section `Photobank:TagsCache`.
- ✅ `GetTagsHandler` reads through cache, logs `TagCount`/`ElapsedMs` on miss, `Debug` on hit.
- ✅ Cache invalidation wired into `CreateTagHandler`, `DeleteTagHandler`, `AddPhotoTagHandler`, `RemovePhotoTagHandler`, `BulkAddPhotoTagHandler`, `BulkAddPhotoTagByIdsHandler`, `ReapplyRulesHandler`, `RetagPhotosHandler`, `PhotobankAutoTagJob` (line 127).
- ✅ Test scaffold present: `PhotobankTagsCacheTests.cs`, `PhotobankTagsCacheInvalidationTests.cs`, `PhotobankRepositoryGetTagsTests.cs`, `GetTagsHandlerTests.cs`.
- ❌ **`PhotobankRepository.GetTagsWithCountsAsync` still uses `t.PhotoTags.Count`** (`PhotobankRepository.cs:141-148`) — EF Core 8 emits this as a correlated subquery per row, not the targeted `LEFT JOIN`/`GROUP BY` shape FR-1 mandates.
- ❌ **No migration `IX_PhotoTags_TagId`** in `Persistence/Migrations/` (most recent is `20260508110506_AddArticleGenerationSteps`).
- ❌ **No `PhotoTagConfiguration.HasIndex(x => x.TagId)`** in `Persistence/Photobank/PhotoTagConfiguration.cs` — without it, the model snapshot will diverge from the migration.
- ❌ **No `AsNoTracking()`** on the read query — FR-4 requires `ChangeTracker.Entries<Tag>()` to be empty after the handler runs.
- ❌ **`appsettings.json` lacks `Photobank:TagsCache:TtlSeconds`** — current behavior relies on the `init` default of 60s in `PhotobankTagsCacheOptions`, which works but leaves the spec's "tunable via configuration" requirement undocumented in environment files.

Integration points: read path is `PhotobankController → GetTagsHandler → IPhotobankTagsCache → IPhotobankRepository`. Invalidation is per-handler, after `SaveChangesAsync` succeeds. No cross-module coupling.

## Proposed Architecture

### Component Overview

```
                         ┌──────────────────────────┐
  GET /api/photobank/    │   PhotobankController    │
  tags                ──►│   .GetTags()             │
                         └────────────┬─────────────┘
                                      │ MediatR
                                      ▼
                         ┌──────────────────────────┐
                         │   GetTagsHandler         │
                         │   - IPhotobankTagsCache  │
                         │   - IPhotobankRepository │
                         │   - ILogger              │
                         └────┬───────────────┬─────┘
                              │ TryGet        │ on miss
                              ▼               ▼
              ┌────────────────────┐   ┌────────────────────────────────┐
              │ IPhotobankTagsCache│   │ PhotobankRepository            │
              │ (scoped wrapper    │   │ .GetTagsWithCountsAsync()      │
              │  over IMemoryCache │   │ single GROUP BY  ➜  no entity  │
              │  singleton)        │   │ tracking, returns TagCount[]   │
              │  Get/Set/Invalidate│   └────────────┬───────────────────┘
              └─────────▲──────────┘                │
                        │                           ▼
                        │            ┌──────────────────────────────┐
                        │            │  PostgreSQL                  │
                        │            │  PhotobankTags ⋈ PhotoTags   │
                        │            │  IX_PhotoTags_TagId (NEW)    │
                        │            └──────────────────────────────┘
                        │
       Invalidate() after successful SaveChangesAsync:
       • CreateTagHandler, DeleteTagHandler
       • AddPhotoTagHandler, RemovePhotoTagHandler
       • BulkAddPhotoTagHandler, BulkAddPhotoTagByIdsHandler
       • ReapplyRulesHandler, RetagPhotosHandler
       • PhotobankAutoTagJob.ProcessBatchAsync (post-batch)
```

### Key Design Decisions

#### Decision 1: Cache wrapper lifetime — scoped, not singleton
**Options considered:**
- (a) Singleton wrapper as the spec suggests.
- (b) Scoped wrapper, with `IMemoryCache` (singleton) holding the actual entries.

**Chosen approach:** (b) — scoped. This is already what `PhotobankModule.cs:38` does.

**Rationale:** `IMemoryCache` is singleton by ASP.NET Core convention, so cached entries survive across requests regardless of the wrapper's lifetime. Scoped registration matches `SalesCostCache` / `MaterialCostCache` precedent and removes the DI-lifetime footgun if the wrapper ever gains scoped dependencies. This is a deliberate deviation from the spec — flagged below as an amendment.

#### Decision 2: Query rewrite — `GroupJoin` projecting directly to a domain record
**Options considered:**
- (a) Keep `t.PhotoTags.Count` (current code) and trust EF Core to optimize.
- (b) Explicit `GroupJoin` with `Count()`, projecting into `TagCount(Id, Name, Count)`.
- (c) Raw SQL via `FromSqlInterpolated`.

**Chosen approach:** (b).

**Rationale:** EF Core 8 emits `t.PhotoTags.Count` as a correlated `SELECT COUNT(*) ... WHERE TagId = t.Id` subquery — the exact N+1-like shape FR-1 wants to eliminate. `GroupJoin` translates to a single `LEFT JOIN` + `GROUP BY` against Npgsql. Raw SQL is unnecessary; we stay inside the repository abstraction and EF parameterizes for free. Add `.AsNoTracking()` to satisfy FR-4 (`ChangeTracker.Entries<Tag>()` empty).

#### Decision 3: Invalidation strategy — explicit, per-handler, post-`SaveChangesAsync`
**Options considered:**
- (a) Explicit `_cache.Invalidate()` call in each mutating handler — already implemented.
- (b) `SaveChangesInterceptor` on `ApplicationDbContext` watching `ChangeTracker` for `Tag`/`PhotoTag`.
- (c) MediatR pipeline behavior keyed on a marker interface.

**Chosen approach:** (a).

**Rationale:** Explicit is auditable and grep-able. An interceptor on `ApplicationDbContext` would couple the Persistence layer to a feature-specific cache, violating module isolation. The mutator set is bounded and the test matrix in `PhotobankTagsCacheInvalidationTests.cs` covers each call site. Critical contract: invalidate **only after** `SaveChangesAsync` returns successfully — failure leaves cache untouched (no stale risk because nothing changed in the DB).

#### Decision 4: Index creation strategy — `CREATE INDEX CONCURRENTLY` in production
**Options considered:**
- (a) Apply the EF migration as-generated (blocking `CREATE INDEX`).
- (b) Script the migration, replace DDL with `CREATE INDEX CONCURRENTLY`, run manually.

**Chosen approach:** (b) for production rollout; the EF migration itself uses standard `CreateIndex` so the model snapshot stays in sync.

**Rationale:** `PhotoTags` is mutated by the auto-tag job and bulk handlers. A blocking `CREATE INDEX` takes a write-blocking lock for the duration. With tens of thousands of rows the window is short, but per `CLAUDE.md` the project uses the manual migration workflow — fits naturally with the override. EF Core's `migrationBuilder.CreateIndex` does **not** emit `CONCURRENTLY`; the override is intentional and must be applied outside the 04:00 UTC auto-tag job window.

#### Decision 5: Background job reads through repository, not cache
**Options considered:**
- (a) `PhotobankAutoTagJob` reads tags via the cache.
- (b) Job bypasses the cache; invalidates after each batch save.

**Chosen approach:** (b). Already implemented at `PhotobankAutoTagJob.cs:127`.

**Rationale:** The job needs current vocabulary to feed the LLM. A stale 60s view risks dropping a freshly-created tag. Bypass cost is one query per batch (cron-scheduled). Invalidation after `ProcessBatchAsync.SaveChangesAsync` keeps API readers' counts fresh.

## Implementation Guidance

### Directory / Module Structure

What remains (in implementation order):

```
backend/src/Anela.Heblo.Application/Features/Photobank/
└── PhotobankRepository.cs                                EDIT — rewrite GetTagsWithCountsAsync (GroupJoin + AsNoTracking)

backend/src/Anela.Heblo.Persistence/
├── Photobank/PhotoTagConfiguration.cs                    EDIT — add HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId")
└── Migrations/
    ├── {timestamp}_AddPhotoTagsTagIdIndex.cs             NEW — IX_PhotoTags_TagId migration
    ├── {timestamp}_AddPhotoTagsTagIdIndex.Designer.cs    NEW (generated)
    └── ApplicationDbContextModelSnapshot.cs              EDIT (regenerated)

backend/src/Anela.Heblo.API/appsettings.json             EDIT — explicit Photobank:TagsCache:TtlSeconds: 60
                                                                (and the staging / production overlays if present)

docs/integrations/ or release notes                      EDIT — document the manual migration + CONCURRENTLY note
```

All other files (cache wrapper, options, handler edits, mutating handler invalidations, tests) are **already in place** and verified at HEAD. The remaining work is a focused four-file change plus tests.

### Interfaces and Contracts

The interface already exists and is correct. No changes to public surface.

```csharp
// Application/Features/Photobank/Services/IPhotobankTagsCache.cs (current)
public interface IPhotobankTagsCache
{
    bool TryGet([NotNullWhen(true)] out IReadOnlyList<TagWithCountDto>? tags);
    void Set(IReadOnlyList<TagWithCountDto> tags);
    void Invalidate();
}
```

```csharp
// Domain/Features/Photobank/IPhotobankRepository.cs (signature unchanged — return type already IReadOnlyList<TagCount>)
Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
```

`TagCount` lives in `Domain/Features/Photobank/TagCount.cs` (domain record, no EF coupling). Handler maps to `TagWithCountDto` before caching — keeps the cached payload as the contract type.

### EF Core Query Shape (the one change with real complexity)

```csharp
// PhotobankRepository.GetTagsWithCountsAsync
public async Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken)
{
    return await _context.PhotobankTags
        .GroupJoin(
            _context.PhotoTags,
            t => t.Id,
            pt => pt.TagId,
            (t, pts) => new TagCount(t.Id, t.Name, pts.Count()))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Name)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
}
```

Expected SQL (one statement):

```sql
SELECT t."Id", t."Name", COUNT(pt."TagId") AS "Count"
FROM public."PhotobankTags" AS t
LEFT JOIN public."PhotoTags" AS pt ON pt."TagId" = t."Id"
GROUP BY t."Id", t."Name"
ORDER BY COUNT(pt."TagId") DESC, t."Name" ASC;
```

Validation criterion: an integration test using `IDbCommandInterceptor` asserts that exactly one SQL command is emitted during `GetTagsHandler.Handle`.

### Migration

```csharp
// Persistence/Migrations/{timestamp}_AddPhotoTagsTagIdIndex.cs
public partial class AddPhotoTagsTagIdIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_PhotoTags_TagId",
            schema: "public",
            table: "PhotoTags",
            column: "TagId");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_PhotoTags_TagId",
            schema: "public",
            table: "PhotoTags");
}
```

Mirror in `PhotoTagConfiguration.cs` so the snapshot matches:

```csharp
builder.HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId");
```

For production application, generate the SQL via `dotnet ef migrations script` and replace `CREATE INDEX` with `CREATE INDEX CONCURRENTLY` (must run outside an explicit transaction).

### Data Flow

**Cache miss (first request / post-invalidation / post-TTL):**
1. `GetTagsHandler.Handle` → `_cache.TryGet(out cached)` returns false.
2. Stopwatch start.
3. `_repository.GetTagsWithCountsAsync(ct)` — one SQL statement, indexed plan via `IX_PhotoTags_TagId`.
4. Map `TagCount` → `TagWithCountDto`, stopwatch stop.
5. `_cache.Set(dtos)` with `AbsoluteExpirationRelativeToNow = TtlSeconds`.
6. `Logger.LogInformation("Fetched {TagCount} photobank tags in {ElapsedMs} ms", ...)`.
7. Return `GetTagsResponse { Tags = dtos.ToList() }`.

**Cache hit:**
1. `_cache.TryGet(out cached)` returns true.
2. Optional `Debug` log.
3. Return response from cached list — no DB round trip, no allocation in the cache path beyond the response wrapper.

**Mutation + invalidation:**
1. Mutating handler runs business logic, calls `_repository.SaveChangesAsync(ct)`.
2. After successful return, calls `_cache.Invalidate()`.
3. Next read repopulates from the rewritten query.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `t.PhotoTags.Count` left in place — query still emits correlated subqueries; SLO not met after deploy | HIGH | Rewrite to `GroupJoin` + `AsNoTracking`; add integration test asserting exactly one SQL statement via `IDbCommandInterceptor` |
| Migration missing in PR; index never reaches production; rewritten query plan-scans `PhotoTags` | HIGH | Migration is a hard gate — include in same PR; manual application checklist tracked in release notes |
| `CREATE INDEX` migration applied as blocking DDL during auto-tag job → write stalls | MEDIUM | Apply manually with `CREATE INDEX CONCURRENTLY`, outside 04:00 UTC job window |
| Model snapshot diverges from migration because `PhotoTagConfiguration` not updated | MEDIUM | Add `HasIndex(x => x.TagId)` in the same change; verify `ApplicationDbContextModelSnapshot.cs` regenerates cleanly via `dotnet ef migrations add` |
| Forgotten invalidation on a future mutator (new handler added later) | MEDIUM | `PhotobankTagsCacheInvalidationTests.cs` already covers each current handler; add a checklist comment at `IPhotobankTagsCache` declaration listing call sites; PR review checks new `PhotoTag`/`Tag` writes |
| Cached `TagWithCountDto` mutated by a caller via shared reference | LOW | Already returned as `IReadOnlyList<TagWithCountDto>`. Recommend tightening DTO properties to `init` setters in this change to prevent accidental mutation |
| `IMemoryCache` key collision with another module | LOW | Key is `"Photobank:Tags:WithCounts"` — fully qualified, distinct from existing keys (`"SalesCostCache_Data"`, etc.) |
| 60s TTL surprises users who expect instant count updates | LOW | Mutating user's own action invalidates immediately; only cross-user observation is bounded by TTL. Documented in spec |

## Specification Amendments

1. **Cache lifetime is scoped, not singleton.** The implementation registers `PhotobankTagsCache` as scoped (`PhotobankModule.cs:38`). This is correct and matches `SalesCostCache` convention — `IMemoryCache` is singleton internally, so entries persist regardless. The spec text under "Internal changes" should be updated from `singleton` to `scoped`.
2. **Repository return type is `IReadOnlyList<TagCount>`, not `List<TagWithCountDto>`.** Current implementation uses the domain record `TagCount` (`Domain/Features/Photobank/TagCount.cs`) — keeps the Domain layer free of Application contracts. Handler maps to `TagWithCountDto` before caching. Spec should reflect this layering.
3. **`AsNoTracking()` required.** FR-4 demands `ChangeTracker.Entries<Tag>()` is empty. The rewritten query must include `.AsNoTracking()` explicitly — add to acceptance criteria.
4. **Production index creation uses `CREATE INDEX CONCURRENTLY`.** Spec mentions reversible migration but not the concurrency guidance for an actively-written table. Add to FR-2 acceptance criteria.
5. **`PhotoTagConfiguration` must declare the new index** so the EF model snapshot stays consistent with the migration. Spec mentions the migration but not the configuration update.
6. **`RetagPhotosHandler` invalidates after `ExecuteUpdateAsync`/`ExecuteDeleteAsync`**, which commit immediately (no `SaveChangesAsync` involved). Spec lists this handler in FR-3 but doesn't address the timing nuance — clarify so future maintainers don't move the invalidation call into a misleading spot.
7. **Explicit `appsettings.json` entry**, even though `PhotobankTagsCacheOptions.TtlSeconds` defaults to 60. Spec requires "tunable via `appsettings.json`" — currently the section is absent, so the documentation contract isn't satisfied.

## Prerequisites

- **No code prerequisites.** All scaffolding (cache wrapper, options, DI, handler/job invalidation) is already in place at HEAD.
- **Configuration:** add `"Photobank": { "TagsCache": { "TtlSeconds": 60 } }` to `backend/src/Anela.Heblo.API/appsettings.json` (and environment overlays as needed) to make the tunable explicit.
- **Database (production rollout):** apply the new migration manually with `CREATE INDEX CONCURRENTLY` on `public."PhotoTags"."TagId"` to staging then production, outside the 04:00 UTC auto-tag job window. Per `CLAUDE.md` manual migration workflow.
- **Observability:** none beyond the existing structured log line. App Insights nightly analysis already covers regression detection.
- **No infrastructure or external service changes.** Single Azure Web App instance — in-memory cache remains correct (distributed caching explicitly out of scope).