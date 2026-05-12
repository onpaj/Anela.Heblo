# Design: Photobank GetTags Performance Fix

## Component Design

### `IPhotobankTagsCache` / `PhotobankTagsCache`

A scoped, passive cache wrapper around `IMemoryCache`. Passive means it stores and retrieves a pre-computed result; it never loads data itself. Mirrors the `SalesCostCache` convention already used in this codebase.

**Interface** — `Application/Features/Photobank/Services/IPhotobankTagsCache.cs`:
```csharp
public interface IPhotobankTagsCache
{
    bool TryGet(out IReadOnlyList<TagWithCountDto> tags);
    void Set(IReadOnlyList<TagWithCountDto> tags);
    void Invalidate();
}
```

**Implementation** — `Application/Features/Photobank/Services/PhotobankTagsCache.cs`:

- Constructor-injects `IMemoryCache` and `IOptions<PhotobankTagsCacheOptions>`.
- `Set` stores with `MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(TtlSeconds) }`.
- `Invalidate` calls `_cache.Remove(CacheKey)`.
- `CacheKey` is a `private const string` = `"Photobank:Tags:WithCounts"`.
- Registered as `services.AddScoped<IPhotobankTagsCache, PhotobankTagsCache>()`.

---

### `PhotobankTagsCacheOptions`

**File** — `Application/Features/Photobank/Configuration/PhotobankTagsCacheOptions.cs`:
```csharp
public sealed class PhotobankTagsCacheOptions
{
    public const string SectionName = "Photobank:TagsCache";
    public int TtlSeconds { get; init; } = 60;
}
```

Registered via `services.Configure<PhotobankTagsCacheOptions>(configuration.GetSection(PhotobankTagsCacheOptions.SectionName))`.

---

### `GetTagsHandler` (modified)

Gains two new constructor dependencies: `IPhotobankTagsCache` and `ILogger<GetTagsHandler>`.

**Read path logic:**
1. `_cache.TryGet(out var cached)` → if true, log at `Debug`, return cached response.
2. On miss: start `Stopwatch`, call `_repository.GetTagsWithCountsAsync(ct)`.
3. Map `IReadOnlyList<TagCount>` → `List<TagWithCountDto>`.
4. Call `_cache.Set(dtos)`.
5. Log at `Information` with structured fields `{TagCount}` and `{ElapsedMs}`. No tag names or other PII.
6. Return `GetTagsResponse { Tags = dtos }`.

---

### `IPhotobankRepository` / `PhotobankRepository` (modified)

**Signature change:**
```csharp
// Domain/Features/Photobank/IPhotobankRepository.cs
Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
```

`TagCount` is a new domain record (see Data Schemas). The handler maps it to `TagWithCountDto`; the cache stores `TagWithCountDto` so the domain record never crosses the cache boundary.

**Rewritten query** — single SQL statement using `GroupJoin`:
```csharp
return await _context.PhotobankTags
    .GroupJoin(
        _context.PhotoTags,
        t  => t.Id,
        pt => pt.TagId,
        (t, pts) => new TagCount(t.Id, t.Name, pts.Count()))
    .OrderByDescending(x => x.Count)
    .ThenBy(x => x.Name)
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

`AsNoTracking()` is required. The generated SQL must be a single `LEFT JOIN … GROUP BY` statement. `IX_PhotoTags_TagId` (new index) supports the `COUNT`/`GROUP BY` on `TagId`.

---

### Mutating handlers (9 handlers + job, all modified)

Each gains `IPhotobankTagsCache` as a constructor dependency and calls `_cache.Invalidate()` immediately after a successful write:

| Handler / Job | When to invalidate |
|---|---|
| `CreateTagHandler` | After `SaveChangesAsync` succeeds |
| `DeleteTagHandler` | After `SaveChangesAsync` succeeds |
| `AddPhotoTagHandler` | After `SaveChangesAsync` succeeds |
| `RemovePhotoTagHandler` | After `SaveChangesAsync` succeeds |
| `BulkAddPhotoTagHandler` | After `SaveChangesAsync` succeeds |
| `BulkAddPhotoTagByIdsHandler` | After `SaveChangesAsync` succeeds |
| `ReapplyRulesHandler` | After `SaveChangesAsync` succeeds |
| `RetagPhotosHandler` | After `ResetAutoTaggedAtAsync` / `RemovePhotoTagsBySourceAsync` return (these use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` and commit immediately — no `SaveChangesAsync`) |
| `PhotobankAutoTagJob` | After each `ProcessBatchAsync`'s `SaveChangesAsync` succeeds (job reads vocabulary directly from repository, bypassing cache) |

Invalidation must not fire if the write fails — the exception propagates before the `Invalidate()` call.

---

### `PhotobankModule` (modified)

Additions:
```csharp
services.AddMemoryCache(); // idempotent
services.Configure<PhotobankTagsCacheOptions>(
    configuration.GetSection(PhotobankTagsCacheOptions.SectionName));
services.AddScoped<IPhotobankTagsCache, PhotobankTagsCache>();
```

---

### EF Core migration

**File** — `Persistence/Migrations/{timestamp}_AddPhotoTagsTagIdIndex.cs`:
```csharp
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

**Model snapshot sync** — add to `PhotoTagConfiguration.cs`:
```csharp
builder.HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId");
```

**Production application:** generate the migration script with `dotnet ef migrations script`, then replace the DDL with `CREATE INDEX CONCURRENTLY "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");`. Apply outside the 04:00 UTC auto-tag job window via the manual-migration workflow.

---

## Data Schemas

### Domain record `TagCount`

**File** — `Domain/Features/Photobank/TagCount.cs`:
```csharp
public record TagCount(int Id, string Name, int Count);
```

Used only inside the repository and handler mapping step. Never stored in the cache (cache uses `TagWithCountDto`).

---

### `TagWithCountDto` (updated)

**File** — `Application/Features/Photobank/Contracts/TagDto.cs` (existing class, properties tightened to `init`):
```csharp
public class TagWithCountDto
{
    public int Id { get; init; }
    public string Name { get; init; } = null!;
    public int Count { get; init; }
}
```

`init` setters prevent callers from mutating a cached payload through a shared reference. `TagDto` is unchanged.

---

### API response (unchanged)

```
GET /api/photobank/tags
Authorization: Bearer <token>
```

```json
{
  "tags": [
    { "id": 12, "name": "summer",   "count": 1843 },
    { "id": 7,  "name": "products", "count": 1201 }
  ],
  "success": true,
  "error": null
}
```

`GetTagsResponse` and `TagWithCountDto` shapes are backward-compatible. No OpenAPI client regeneration required.

---

### Configuration schema

`appsettings.json`:
```json
{
  "Photobank": {
    "TagsCache": {
      "TtlSeconds": 60
    }
  }
}
```

---

### Database index

```sql
-- Applied manually with CONCURRENTLY in production
CREATE INDEX CONCURRENTLY "IX_PhotoTags_TagId"
  ON public."PhotoTags" ("TagId");
```

No other schema changes. `PhotobankTags` and `PhotoTags` table structures are unmodified.

---

### Cache payload

The value stored under key `"Photobank:Tags:WithCounts"` is `IReadOnlyList<TagWithCountDto>` — the final API response shape — so `GetTagsHandler` returns it directly on a cache hit without remapping.

---

### Structured log entry (cache miss only)

```
level:   Information
message: "Fetched {TagCount} photobank tags in {ElapsedMs} ms"
fields:
  TagCount  int  — number of tags returned
  ElapsedMs int  — wall-clock time of the repository call
```

No tag names, IDs, or other PII are emitted.
