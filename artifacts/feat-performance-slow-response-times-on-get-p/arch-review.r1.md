Got enough context. Writing the architecture review now.

# Architecture Review: Photobank GetTags Performance Fix

## Architectural Fit Assessment

The proposal aligns cleanly with established conventions:

- **Vertical Slice + MediatR**: feature lives entirely under `backend/src/Anela.Heblo.Application/Features/Photobank/`; `GetTagsHandler` is unchanged in shape, only gains a cache dependency. Matches the pattern in `docs/architecture/development_guidelines.md` ADR-003.
- **Repository pattern**: `IPhotobankRepository` already centralizes data access; the query rewrite stays inside `PhotobankRepository.GetTagsWithCountsAsync`. No new interface surface is required.
- **`IMemoryCache`**: already used in `Application/Features/Catalog/Cache/` (`SalesCostCache`, `MaterialCostCache`, …) and registered idempotently via `services.AddMemoryCache()` in `CatalogModule`, `FinancialOverviewModule`, `KnowledgeBaseModule`, plus two adapter projects. Adding it to `PhotobankModule` is consistent.
- **Manual migrations**: matches the project fact in `CLAUDE.md` ("Database migrations are manual"). Existing Photobank migrations live in `backend/src/Anela.Heblo.Persistence/Migrations/` with `public` schema (`20260507145218_AddPhotobankAutoTagState.cs`).
- **Existing cache wrapper precedent (`SalesCostCache`)** is a *passive* storage cache: handler/source owns the load logic, wrapper only stores/retrieves. The spec's `IPhotobankTagsCache` should follow that shape, not become a "loader".

Integration points: `GetTagsHandler` (read path), 8 mutating handlers + 1 background job (invalidation), `PhotobankModule.AddPhotobankModule` (DI), `ApplicationDbContext` (migration), `appsettings.json` (TTL).

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
                         │   (scoped)               │
                         │   - ILogger              │
                         │   - IPhotobankTagsCache  │
                         │   - IPhotobankRepository │
                         └────┬───────────────┬─────┘
                              │ TryGet        │ on miss
                              ▼               ▼
              ┌────────────────────┐   ┌────────────────────────────┐
              │ IPhotobankTagsCache│   │ IPhotobankRepository       │
              │ (scoped wrapper    │   │ .GetTagsWithCountsAsync()  │
              │  over IMemoryCache)│   │ single GROUP BY query      │
              │  - Get/Set/        │   │ projects to DTO directly   │
              │    Invalidate      │   └────────────┬───────────────┘
              └─────────▲──────────┘                │
                        │                           ▼
                        │             ┌──────────────────────────┐
                        │             │  PostgreSQL              │
                        │             │  PhotobankTags ⋈ PhotoTags│
                        │             │  IX_PhotoTags_TagId (new)│
                        │             └──────────────────────────┘
                        │
       Invalidate() from:
       • CreateTagHandler, DeleteTagHandler
       • AddPhotoTagHandler, RemovePhotoTagHandler
       • BulkAddPhotoTagHandler, BulkAddPhotoTagByIdsHandler
       • ReapplyRulesHandler, RetagPhotosHandler
       • PhotobankAutoTagJob.ProcessBatchAsync
       (after successful SaveChangesAsync)
```

### Key Design Decisions

#### Decision 1: Cache wrapper shape and lifetime
**Options considered:**
- **(a)** Singleton "smart" cache that holds a loader delegate and re-fetches itself on miss.
- **(b)** Scoped passive cache (Get / Set / Invalidate only); handler decides what to load. Matches `SalesCostCache`.
- **(c)** Inject `IMemoryCache` directly into the handler with no wrapper.

**Chosen approach:** **(b)** — scoped passive wrapper.

**Rationale:**
- The spec's "singleton" recommendation creates a DI lifetime trap if the wrapper ever gains scoped dependencies (DbContext, repository). `IMemoryCache` is already a singleton internally, so the wrapper's lifetime does not affect cache lifetime.
- A typed wrapper (vs. raw `IMemoryCache`) gives a single grep-able invalidation surface across 9 mutators, lets us strongly type the cache key and the payload, and is easy to mock in tests.
- Matches the existing `SalesCostCache` convention in this codebase (`services.AddScoped<ISalesCostCache, SalesCostCache>()`). Deviating would be friction without benefit.

#### Decision 2: Repository return type — drop the `Tag` entity from the contract
**Options considered:**
- **(a)** Keep `List<(Tag, int Count)>`; project to DTO in handler (current shape).
- **(b)** Change signature to `List<TagWithCountDto>` and project inside the EF query.

**Chosen approach:** **(b)**.

**Rationale:**
- FR-4 requires that EF Core does not materialize tracked `Tag` entities and the generated SQL selects only `Id`, `Name`, `COUNT(*)`. Returning `Tag` from the repository forces consumers (and the cache layer) to handle entity objects, which leaks the persistence model into the cached payload. A cached `Tag` graph crossing scope boundaries (singleton `IMemoryCache`) is a footgun.
- DTOs as classes (per `CLAUDE.md` and `docs/architecture/development_guidelines.md`) are already the contract type. Reusing `TagWithCountDto` for the repository return removes the `.Select(...)` step in the handler entirely.
- One caller — `PhotobankAutoTagJob` — currently consumes `(Tag, int Count)` and uses only `Tag.Name` and `Tag.Id` to build a `Dictionary<string, Tag>`. Switching its dictionary value type to the DTO (or its `Id`) is a small, safe edit covered by spec FR-4's "no tracked entities" requirement.

#### Decision 3: Invalidation strategy — explicit per-handler call after SaveChanges
**Options considered:**
- **(a)** Explicit `_cache.Invalidate()` call in each mutating handler after `SaveChangesAsync`.
- **(b)** `SaveChangesInterceptor` on `ApplicationDbContext` that watches `ChangeTracker` for `Tag` / `PhotoTag` entries.
- **(c)** MediatR pipeline behavior keyed on a marker interface.

**Chosen approach:** **(a)** — matches spec.

**Rationale:**
- Explicit calls are auditable: a future reader of `CreateTagHandler` sees the dependency without chasing a hidden interceptor.
- **(b)** is clever but introduces a cross-cutting coupling between `ApplicationDbContext` (Persistence layer) and a feature-specific cache, violating the module-isolation guideline.
- The number of mutators is bounded (9, fully enumerated in the spec). Risk of forgetting one is mitigated by the integration test requirement in FR-3/NFR-4.
- Critical contract: invalidation must run **after** `SaveChangesAsync` returns successfully. If `SaveChangesAsync` throws, the cache must remain untouched (no stale-state risk because nothing changed in the DB).

#### Decision 4: Cache stampede protection
**Options considered:**
- **(a)** None (let multiple concurrent misses hit the DB).
- **(b)** Per-key `SemaphoreSlim` lock around the load.

**Chosen approach:** **(a)** — no stampede protection.

**Rationale:** App Insights shows 1 occurrence per 24h. Even after fix, traffic is dropdown-population, not high-fanout. The post-rewrite query at ~200ms will not overwhelm PostgreSQL with a handful of concurrent misses. Adding locking is premature optimization (KISS).

#### Decision 5: Background job behavior
**Options considered:**
- **(a)** `PhotobankAutoTagJob` reads through the cache.
- **(b)** Job reads directly from the repository, bypassing the cache, and invalidates after each batch save.

**Chosen approach:** **(b)**.

**Rationale:** The job needs the *current* tag vocabulary to feed the LLM; a stale 60s view risks dropping a just-created tag from prompts. Cost of bypass is one query per job run (cron-scheduled, not hot path). Invalidation after `ProcessBatchAsync`'s `SaveChangesAsync` is still required because the job may have added new `PhotoTag` rows that change counts visible to API readers.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Photobank/
├── Services/
│   ├── IPhotobankTagsCache.cs          NEW — interface
│   └── PhotobankTagsCache.cs           NEW — IMemoryCache wrapper
├── Configuration/
│   └── PhotobankTagsCacheOptions.cs    NEW — Options pattern
├── UseCases/GetTags/
│   └── GetTagsHandler.cs               EDIT — inject cache + logger, project DTO directly
├── UseCases/CreateTag/CreateTagHandler.cs              EDIT — inject + invalidate
├── UseCases/DeleteTag/DeleteTagHandler.cs              EDIT
├── UseCases/AddPhotoTag/AddPhotoTagHandler.cs          EDIT
├── UseCases/RemovePhotoTag/RemovePhotoTagHandler.cs    EDIT
├── UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs  EDIT
├── UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs  EDIT
├── UseCases/ReapplyRules/ReapplyRulesHandler.cs        EDIT
├── UseCases/RetagPhotos/RetagPhotosHandler.cs          EDIT — see note below
├── Infrastructure/Jobs/PhotobankAutoTagJob.cs          EDIT — invalidate after each ProcessBatchAsync
├── PhotobankRepository.cs                              EDIT — rewrite GetTagsWithCountsAsync
└── PhotobankModule.cs                                  EDIT — AddMemoryCache, register cache + options

backend/src/Anela.Heblo.Domain/Features/Photobank/
└── IPhotobankRepository.cs                             EDIT — change return type of GetTagsWithCountsAsync

backend/src/Anela.Heblo.Persistence/Migrations/
└── {timestamp}_AddPhotoTagsTagIdIndex.cs               NEW — IX_PhotoTags_TagId
└── {timestamp}_AddPhotoTagsTagIdIndex.Designer.cs      NEW (generated)

backend/test/Anela.Heblo.Tests/Features/Photobank/
├── GetTagsHandlerTests.cs                              NEW — unit + integration
└── PhotobankTagsCacheTests.cs                          NEW — wrapper + invalidation matrix

appsettings.json (and appsettings.{Env}.json as needed)  EDIT — Photobank:TagsCache:TtlSeconds
```

**Note on `RetagPhotosHandler`**: it does not call `SaveChangesAsync` itself; it issues `ResetAutoTaggedAtAsync` / `RemovePhotoTagsBySourceAsync` (both `ExecuteDeleteAsync` / `ExecuteUpdateAsync`, which commit immediately) and then enqueues a background job. Invalidate *after* the executed deletes/updates return, since they bypass the EF change tracker but do mutate `PhotoTags`.

### Interfaces and Contracts

```csharp
// Application/Features/Photobank/Services/IPhotobankTagsCache.cs
namespace Anela.Heblo.Application.Features.Photobank.Services;

public interface IPhotobankTagsCache
{
    bool TryGet(out IReadOnlyList<TagWithCountDto> tags);
    void Set(IReadOnlyList<TagWithCountDto> tags);
    void Invalidate();
}
```

```csharp
// Application/Features/Photobank/Configuration/PhotobankTagsCacheOptions.cs
public sealed class PhotobankTagsCacheOptions
{
    public const string SectionName = "Photobank:TagsCache";
    public int TtlSeconds { get; init; } = 60;
}
```

```csharp
// Domain/Features/Photobank/IPhotobankRepository.cs (signature change)
Task<IReadOnlyList<TagWithCountDto>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
```

> `TagWithCountDto` already exists at `Application/Features/Photobank/Contracts/TagDto.cs`. The domain `IPhotobankRepository` referencing an Application-layer DTO is acceptable here because `TagWithCountDto` is *the* contract; we are not introducing a new dependency direction beyond what already exists across this codebase. If a stricter layering boundary is preferred, introduce a sibling record `TagWithCountReadModel` in `Domain/Features/Photobank/` and map in the handler — but that adds a duplicate type for no behavioral gain. **Recommendation:** introduce a domain-side `record TagCount(int Id, string Name, int Count)` in `Domain/Features/Photobank/` and have the handler map to `TagWithCountDto`. This keeps Domain free of Application contracts while preserving the "no Tag entity leak" goal.

### Data Flow

**Cache miss (read path):**
1. Request hits `GET /api/photobank/tags`.
2. `GetTagsHandler.Handle` calls `_cache.TryGet(out var cached)` → false.
3. Handler calls `_repository.GetTagsWithCountsAsync(ct)` — single SQL: `SELECT t.Id, t.Name, COUNT(pt.TagId) FROM "PhotobankTags" t LEFT JOIN "PhotoTags" pt ON pt.TagId = t.Id GROUP BY t.Id, t.Name ORDER BY COUNT(pt.TagId) DESC, t.Name ASC`. Index `IX_PhotoTags_TagId` supports the COUNT/GROUP BY.
4. Handler maps to DTO list, calls `_cache.Set(dtos)` with absolute expiration `TtlSeconds`.
5. Logger.LogInformation with `TagCount`, `ElapsedMs`.
6. Returns `GetTagsResponse { Tags = dtos.ToList() }`.

**Cache hit:**
1. `_cache.TryGet(out var cached)` → true.
2. Optional `Debug` log.
3. Returns response from cached list (no DB round trip).

**Mutation + invalidation:**
1. Mutating handler runs business logic, calls `_repository.SaveChangesAsync(ct)`.
2. After successful return, calls `_cache.Invalidate()`.
3. Next read repopulates.

### EF Core Query Shape

```csharp
// PhotobankRepository.GetTagsWithCountsAsync
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
```

Equivalent group-by form (either is acceptable; Npgsql produces a single statement for both):

```csharp
from t in _context.PhotobankTags
join pt in _context.PhotoTags on t.Id equals pt.TagId into ptg
select new TagCount(t.Id, t.Name, ptg.Count())
```

`AsNoTracking()` is required to satisfy FR-4 (`ChangeTracker.Entries<Tag>()` empty).

### Module Registration (`PhotobankModule.cs` additions)

```csharp
services.AddMemoryCache(); // idempotent if other modules already added it
services.Configure<PhotobankTagsCacheOptions>(
    configuration.GetSection(PhotobankTagsCacheOptions.SectionName));
services.AddScoped<IPhotobankTagsCache, PhotobankTagsCache>();
```

### Migration

```csharp
// {timestamp}_AddPhotoTagsTagIdIndex.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateIndex(
        name: "IX_PhotoTags_TagId",
        schema: "public",
        table: "PhotoTags",
        column: "TagId");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_PhotoTags_TagId",
        schema: "public",
        table: "PhotoTags");
}
```

Mirror by adding `t.HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId");` to `PhotoTagConfiguration.cs` so the model snapshot stays in sync.

**Concurrency note for production rollout:** generate the migration with `dotnet ef migrations script` and, before applying it, replace the generated DDL with `CREATE INDEX CONCURRENTLY "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");` to avoid taking an `ACCESS EXCLUSIVE`-equivalent lock on `PhotoTags`. EF Core's `migrationBuilder.CreateIndex` does **not** support `CONCURRENTLY`. With tens of thousands of rows the lock is brief, but `PhotoTags` is mutated by the auto-tag job and the bulk-tag handlers — the safer pattern is to script the migration and run the SQL manually per the project's manual-migration workflow.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgotten invalidation in one of the 9 mutating call sites → stale cache up to 60s | HIGH | Integration test matrix in NFR-4 covers every handler + the job; add a checklist comment at the cache interface; PR review checks for any new `PhotoTag` / `Tag` writes |
| `CREATE INDEX` migration locks `PhotoTags` table during heavy auto-tag job run | MEDIUM | Apply manually outside auto-tag job window (job cron `0 4 * * *`); use `CREATE INDEX CONCURRENTLY` |
| `IMemoryCache` shared across modules — key collision | LOW | Use a fully qualified, namespaced key constant `"Photobank:Tags:WithCounts"` (matches spec text) — distinct from `"SalesCostCache_Data"` and other existing keys |
| DI lifetime mismatch if cache wrapper grows scoped deps later | LOW | Register wrapper as scoped (Decision 1); never inject `ApplicationDbContext` into it |
| EF Core grouping translation falls back to client-side on older Npgsql | MEDIUM | Project's EF Core version is current (per recent migrations); add explicit `.AsNoTracking()` and an integration test asserting exactly one SQL command via `IDbCommandInterceptor` |
| Repository signature change ripples through `PhotobankAutoTagJob` | LOW | Single call site; the job consumes only `Tag.Name` and `Tag.Id` — both available on the DTO |
| Cached payload mutated by a caller via shared reference | LOW | Expose cache type as `IReadOnlyList<TagWithCountDto>` and clone-on-store, or document that callers must treat the result as immutable. `TagWithCountDto` properties are settable today — recommend tightening to `init` setters in this same change |
| 60s TTL surprises users tagging photos who expect instant count refresh | LOW | Spec already discusses; invalidation on mutating paths ensures the *acting* user sees fresh counts; only cross-user delay is bounded by TTL |

## Specification Amendments

1. **Repository signature change.** Spec FR-4 implies it but does not state it. Change `IPhotobankRepository.GetTagsWithCountsAsync` to return `IReadOnlyList<TagWithCountDto>` (or a new `Domain/Features/Photobank/TagCount` record mapped in the handler) instead of `List<(Tag Tag, int Count)>`. Update the one external caller — `PhotobankAutoTagJob` — accordingly.
2. **Cache lifetime: scoped, not singleton.** Spec recommends singleton. Recommend scoped to match existing `SalesCostCache` / `MaterialCostCache` convention and to keep DI lifetime safety as the wrapper evolves. (`IMemoryCache` itself is singleton, so cache contents survive across requests regardless.)
3. **`RetagPhotosHandler` invalidation timing.** Spec lists it among invalidating handlers. Clarify: invalidate after the synchronous `ResetAutoTaggedAtAsync` / `RemovePhotoTagsBySourceAsync` calls (which use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` and commit immediately), **not** after the enqueued background job — the job invalidates itself on each batch.
4. **`PhotobankAutoTagJob` vocabulary read.** Add explicit statement: the job reads through the repository (bypasses the cache) to guarantee fresh vocabulary; invalidation happens at the end of each `ProcessBatchAsync` after `SaveChangesAsync`.
5. **Index creation strategy.** Add manual `CREATE INDEX CONCURRENTLY` guidance for production application — EF migration template uses blocking `CREATE INDEX`.
6. **DTO immutability.** `TagWithCountDto` currently has settable properties. Recommend converting `Id`, `Name`, `Count` to `init` setters as part of this change so cached payloads cannot be mutated by callers. (Still a class per project DTO rule.)
7. **Model snapshot update.** Mirror the new index in `PhotoTagConfiguration.cs` (`HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId")`) so the EF model snapshot matches the migration. Spec mentions the migration but not the configuration update.

## Prerequisites

- **No code prerequisites.** `IMemoryCache` is already transitively available; `services.AddMemoryCache()` is registered idempotently by several modules and PhotobankModule will add it as well.
- **Configuration:** add `"Photobank": { "TagsCache": { "TtlSeconds": 60 } }` to `appsettings.json`. No environment-specific override required for initial rollout.
- **Database:** the `IX_PhotoTags_TagId` migration must be applied to staging and production via the manual migration workflow before the rewritten query ships (otherwise the rewrite gets the wrong plan against an unindexed `PhotoTags.TagId`). Apply with `CREATE INDEX CONCURRENTLY` outside the 04:00 UTC auto-tag job window.
- **Observability:** none required beyond the structured log line in FR-5. Existing App Insights nightly analysis will detect regression.
- **No infrastructure / external service changes.** Single-instance deployment makes in-memory caching sufficient (per spec's "Out of Scope" on distributed caching).