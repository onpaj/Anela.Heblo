# Photobank GetTags Performance Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `GET /api/photobank/tags` latency from ~11s to sub-second by rewriting the EF query into a single GROUP BY statement, projecting straight to a DTO, and gating the result behind a 60s in-memory cache invalidated on every tag/photo-tag mutation.

**Architecture:** Vertical Slice + MediatR (matches existing project layout). A scoped passive cache wrapper (`IPhotobankTagsCache`) over `IMemoryCache` mirrors the `SalesCostCache` convention. Repository returns a domain record (`TagCount`) so EF Core entities never leak; handler maps to `TagWithCountDto` and stores that in the cache. Nine mutating handlers + one background job call `Invalidate()` after a successful write.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, MediatR, `Microsoft.Extensions.Caching.Memory`, xUnit + FluentAssertions + Moq.

---

## ⚠️ Critical Pre-Flight Finding

The spec and architecture review both claim `IX_PhotoTags_TagId` is missing. **It already exists.** It was created by `20260424122851_AddPhotobankTables.cs` (line 141-145) because EF Core auto-creates an index on a foreign-key column. Verify this in Task 1 before writing the migration; if the index exists in production, the migration becomes a no-op and the EF model snapshot already records `b.HasIndex("TagId")` (line 2292 of `ApplicationDbContextModelSnapshot.cs`).

Net effect on the plan: the performance win comes from the **query rewrite + DTO projection + cache** (FR-1, FR-3, FR-4, FR-5). The index task (FR-2) is conditional.

---

## File Structure

**New files:**

- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagCount.cs` — domain record returned by the repository.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Configuration/PhotobankTagsCacheOptions.cs` — TTL options.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankTagsCache.cs` — cache interface.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankTagsCache.cs` — `IMemoryCache` wrapper.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheTests.cs` — unit tests for the wrapper.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs` — repository query rewrite tests.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/GetTagsHandlerTests.cs` — handler cache hit/miss + logging tests.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs` — one test per mutating handler/job verifying `Invalidate()` is called after successful write.

**Modified files:**

- `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/TagDto.cs` — `TagWithCountDto` properties tightened to `init` setters.
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — `GetTagsWithCountsAsync` signature changes to return `IReadOnlyList<TagCount>`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — rewrite `GetTagsWithCountsAsync` to a single GROUP BY with DTO projection.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetTags/GetTagsHandler.cs` — gain `IPhotobankTagsCache` + `ILogger<GetTagsHandler>`, cache-aside read path, structured log on miss.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/CreateTag/CreateTagHandler.cs` — inject cache, invalidate after successful create.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteTag/DeleteTagHandler.cs` — invalidate after `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddPhotoTag/AddPhotoTagHandler.cs` — invalidate after `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RemovePhotoTag/RemovePhotoTagHandler.cs` — invalidate after `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs` — invalidate when `photoIds.Count > 0`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs` — invalidate when `toAdd.Count > 0`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — invalidate after `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RetagPhotos/RetagPhotosHandler.cs` — invalidate after `ResetAutoTaggedAtAsync` / `RemovePhotoTagsBySourceAsync` (no `SaveChangesAsync` here).
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` — adapt to new repository return type, invalidate after each `ProcessBatchAsync`'s `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` — `AddMemoryCache`, register options, register scoped cache.
- `backend/src/Anela.Heblo.API/appsettings.json` — add `Photobank:TagsCache:TtlSeconds = 60`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs` — update mocks to the new return type (`IReadOnlyList<TagCount>`).
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotoTagConfiguration.cs` — only if Task 1 shows the index is missing in code; otherwise no edit.

---

## Task 1: Verify index state and decide migration scope

**Files:**
- Read: `backend/src/Anela.Heblo.Persistence/Migrations/20260424122851_AddPhotobankTables.cs:141-145`
- Read: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs:2274-2295`
- Run: psql against staging if available

- [ ] **Step 1: Confirm `IX_PhotoTags_TagId` exists in the EF migration history**

Run:
```bash
grep -n 'IX_PhotoTags_TagId' backend/src/Anela.Heblo.Persistence/Migrations/20260424122851_AddPhotobankTables.cs
```
Expected output: a `CreateIndex` block with `name: "IX_PhotoTags_TagId"` and `column: "TagId"`.

- [ ] **Step 2: Confirm the snapshot already records the index**

Run:
```bash
grep -n 'HasIndex("TagId")' backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
```
Expected: one match near line 2292 (inside the `PhotoTag` entity block).

- [ ] **Step 3: Verify against the deployed database**

If a staging connection is available, run:
```sql
SELECT indexname
FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'PhotoTags';
```
Expected: includes `IX_PhotoTags_TagId`. If not, the production index is genuinely missing and Task 13 (Optional: ship the migration) must run. If yes, skip Task 13.

- [ ] **Step 4: Record the decision in the plan checklist**

Edit the heading of Task 13 to either `Task 13: SKIPPED — index exists` or leave it as-is. No commit needed yet — this is a planning artifact only.

---

## Task 2: Add `TagCount` domain record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Photobank/TagCount.cs`

- [ ] **Step 1: Create the record**

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public sealed record TagCount(int Id, string Name, int Count);
```

- [ ] **Step 2: Verify the build**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/TagCount.cs
git commit -m "feat: add TagCount domain record for photobank tag projection"
```

---

## Task 3: Add `PhotobankTagsCacheOptions`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Configuration/PhotobankTagsCacheOptions.cs`

- [ ] **Step 1: Create the options class**

```csharp
namespace Anela.Heblo.Application.Features.Photobank.Configuration;

public sealed class PhotobankTagsCacheOptions
{
    public const string SectionName = "Photobank:TagsCache";
    public int TtlSeconds { get; init; } = 60;
}
```

- [ ] **Step 2: Verify the build**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Configuration/PhotobankTagsCacheOptions.cs
git commit -m "feat: add PhotobankTagsCacheOptions for tag cache TTL configuration"
```

---

## Task 4: Tighten `TagWithCountDto` to immutable `init` setters

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/TagDto.cs`

Project rule: DTOs are classes (OpenAPI client generators mishandle `record` parameter order). `init` setters preserve the class shape while preventing post-cache mutation.

- [ ] **Step 1: Replace setters with `init`**

Change the file from:
```csharp
public class TagWithCountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int Count { get; set; }
}
```
to:
```csharp
public class TagWithCountDto
{
    public int Id { get; init; }
    public string Name { get; init; } = null!;
    public int Count { get; init; }
}
```
Leave `TagDto` (above it) unchanged.

- [ ] **Step 2: Build the full backend to confirm no caller mutates these properties**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` If any consumer assigns to `Id`/`Name`/`Count` after construction the compiler will fail — fix by switching to object-initializer syntax at the call site.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/TagDto.cs
git commit -m "refactor: tighten TagWithCountDto to init setters for cache safety"
```

---

## Task 5: Add `IPhotobankTagsCache` interface and `PhotobankTagsCache` implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankTagsCache.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankTagsCache.cs`
- Test:   `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Photobank.Configuration;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankTagsCacheTests
{
    private static PhotobankTagsCache CreateCache(int ttlSeconds = 60)
    {
        var memory = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new PhotobankTagsCacheOptions { TtlSeconds = ttlSeconds });
        return new PhotobankTagsCache(memory, options);
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenCacheIsEmpty()
    {
        var cache = CreateCache();

        var hit = cache.TryGet(out var tags);

        hit.Should().BeFalse();
        tags.Should().BeNull();
    }

    [Fact]
    public void TryGet_ReturnsCachedPayload_AfterSet()
    {
        var cache = CreateCache();
        var payload = new List<TagWithCountDto>
        {
            new() { Id = 1, Name = "summer", Count = 10 },
        };

        cache.Set(payload);
        var hit = cache.TryGet(out var tags);

        hit.Should().BeTrue();
        tags.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Invalidate_RemovesCachedPayload()
    {
        var cache = CreateCache();
        cache.Set(new List<TagWithCountDto> { new() { Id = 1, Name = "x", Count = 1 } });

        cache.Invalidate();
        var hit = cache.TryGet(out var tags);

        hit.Should().BeFalse();
        tags.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests, confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankTagsCacheTests"
```
Expected: fails to compile — `IPhotobankTagsCache` and `PhotobankTagsCache` do not exist yet.

- [ ] **Step 3: Create the interface**

Create `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankTagsCache.cs`:
```csharp
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Anela.Heblo.Application.Features.Photobank.Contracts;

namespace Anela.Heblo.Application.Features.Photobank.Services;

public interface IPhotobankTagsCache
{
    bool TryGet([NotNullWhen(true)] out IReadOnlyList<TagWithCountDto>? tags);
    void Set(IReadOnlyList<TagWithCountDto> tags);
    void Invalidate();
}
```

- [ ] **Step 4: Create the implementation**

Create `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankTagsCache.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Anela.Heblo.Application.Features.Photobank.Configuration;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Photobank.Services;

public sealed class PhotobankTagsCache : IPhotobankTagsCache
{
    private const string CacheKey = "Photobank:Tags:WithCounts";

    private readonly IMemoryCache _memoryCache;
    private readonly PhotobankTagsCacheOptions _options;

    public PhotobankTagsCache(IMemoryCache memoryCache, IOptions<PhotobankTagsCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _options = options.Value;
    }

    public bool TryGet([NotNullWhen(true)] out IReadOnlyList<TagWithCountDto>? tags)
    {
        if (_memoryCache.TryGetValue(CacheKey, out IReadOnlyList<TagWithCountDto>? cached) && cached is not null)
        {
            tags = cached;
            return true;
        }

        tags = null;
        return false;
    }

    public void Set(IReadOnlyList<TagWithCountDto> tags)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.TtlSeconds),
        };
        _memoryCache.Set(CacheKey, tags, entryOptions);
    }

    public void Invalidate() => _memoryCache.Remove(CacheKey);
}
```

- [ ] **Step 5: Run tests, confirm pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankTagsCacheTests"
```
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankTagsCache.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankTagsCache.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheTests.cs
git commit -m "feat: add IPhotobankTagsCache wrapper around IMemoryCache"
```

---

## Task 6: Rewrite `GetTagsWithCountsAsync` to a single GROUP BY query

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs:29`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs:141-153`
- Test:   `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Photobank;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankRepositoryGetTagsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PhotobankRepository _repository;

    public PhotobankRepositoryGetTagsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new PhotobankRepository(_context);
        SeedTagsAndPhotoTags();
    }

    public void Dispose() => _context.Dispose();

    private void SeedTagsAndPhotoTags()
    {
        var summer  = new Tag { Id = 1, Name = "summer" };
        var winter  = new Tag { Id = 2, Name = "winter" };
        var product = new Tag { Id = 3, Name = "products" };
        var orphan  = new Tag { Id = 4, Name = "orphan" }; // zero PhotoTags

        _context.Photos.AddRange(
            new Photo { Id = 100, SharePointFileId = "sp-100", FileName = "a.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 101, SharePointFileId = "sp-101", FileName = "b.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 102, SharePointFileId = "sp-102", FileName = "c.jpg", FolderPath = "p", ModifiedAt = DateTime.UtcNow });

        _context.PhotobankTags.AddRange(summer, winter, product, orphan);

        // summer: 3, products: 2, winter: 1, orphan: 0
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 100, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 101, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 102, TagId = 1, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 100, TagId = 3, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 101, TagId = 3, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 100, TagId = 2, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow });
        _context.SaveChanges();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsAllTagsIncludingOrphansWithZeroCount()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().HaveCount(4);
        result.Single(t => t.Name == "orphan").Count.Should().Be(0);
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_OrdersByCountDescThenNameAsc()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Select(t => t.Name).Should().ContainInOrder("summer", "products", "winter", "orphan");
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_DoesNotTrackTagEntities()
    {
        _ = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        _context.ChangeTracker.Entries<Tag>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetTagsWithCountsAsync_ReturnsTagCountRecord()
    {
        var result = await _repository.GetTagsWithCountsAsync(CancellationToken.None);

        result.Should().AllBeOfType<TagCount>();
        result.First().Should().BeOfType<TagCount>();
    }
}
```

- [ ] **Step 2: Run tests, confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryGetTagsTests"
```
Expected: fails to compile — return type is still `List<(Tag Tag, int Count)>`, not `TagCount`.

- [ ] **Step 3: Update the interface signature**

Edit `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` line 29:

Replace:
```csharp
Task<List<(Tag Tag, int Count)>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
```
with:
```csharp
Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
```

- [ ] **Step 4: Rewrite the repository implementation**

Edit `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` lines 141-153:

Replace the existing body of `GetTagsWithCountsAsync` with:
```csharp
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

- [ ] **Step 5: Run repository tests, confirm pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryGetTagsTests"
```
Expected: 4 tests pass.

- [ ] **Step 6: Build the solution to surface other callers**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: two compilation errors — `GetTagsHandler` (Task 7 fixes) and `PhotobankAutoTagJob` (Task 8 fixes). Note the failing files; do **not** commit yet.

- [ ] **Step 7: Commit when callers are fixed**

Deferred to the end of Task 8 once compilation succeeds end-to-end.

---

## Task 7: Update `GetTagsHandler` for cache + logging + direct DTO map

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetTags/GetTagsHandler.cs`
- Test:   `backend/test/Anela.Heblo.Tests/Features/Photobank/GetTagsHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/GetTagsHandlerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetTags;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetTagsHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();

    private GetTagsHandler CreateHandler() =>
        new(_repo.Object, _cache.Object, NullLogger<GetTagsHandler>.Instance);

    [Fact]
    public async Task Handle_OnCacheHit_DoesNotCallRepository()
    {
        IReadOnlyList<TagWithCountDto>? cached = new List<TagWithCountDto>
        {
            new() { Id = 1, Name = "summer", Count = 10 },
        };
        _cache.Setup(c => c.TryGet(out cached)).Returns(true);

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().HaveCount(1);
        response.Tags[0].Name.Should().Be("summer");
        _repo.Verify(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_CallsRepositoryAndStoresInCache()
    {
        IReadOnlyList<TagWithCountDto>? cached = null;
        _cache.Setup(c => c.TryGet(out cached)).Returns(false);

        var fromDb = new List<TagCount>
        {
            new(1, "summer", 10),
            new(2, "winter", 3),
        };
        _repo.Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(fromDb);

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().HaveCount(2);
        response.Tags[0].Should().BeEquivalentTo(new { Id = 1, Name = "summer", Count = 10 });
        _cache.Verify(c => c.Set(It.Is<IReadOnlyList<TagWithCountDto>>(list =>
            list.Count == 2 && list[0].Name == "summer")), Times.Once);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_ProducesResponseTagsAsTagWithCountDto()
    {
        IReadOnlyList<TagWithCountDto>? cached = null;
        _cache.Setup(c => c.TryGet(out cached)).Returns(false);
        _repo.Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<TagCount> { new(7, "products", 1201) });

        var response = await CreateHandler().Handle(new GetTagsRequest(), CancellationToken.None);

        response.Tags.Should().AllBeOfType<TagWithCountDto>();
        response.Tags.Should().ContainSingle(t => t.Id == 7 && t.Name == "products" && t.Count == 1201);
    }
}
```

- [ ] **Step 2: Run tests, confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTagsHandlerTests"
```
Expected: fails to compile — handler still has the old constructor and uses `t.Tag.Id`.

- [ ] **Step 3: Rewrite the handler**

Replace the full contents of `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetTags/GetTagsHandler.cs` with:

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetTags
{
    public class GetTagsHandler : IRequestHandler<GetTagsRequest, GetTagsResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;
        private readonly ILogger<GetTagsHandler> _logger;

        public GetTagsHandler(
            IPhotobankRepository repository,
            IPhotobankTagsCache cache,
            ILogger<GetTagsHandler> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;
        }

        public async Task<GetTagsResponse> Handle(GetTagsRequest request, CancellationToken cancellationToken)
        {
            if (_cache.TryGet(out var cached))
            {
                _logger.LogDebug("Photobank tags cache hit ({TagCount} tags)", cached.Count);
                return new GetTagsResponse { Tags = cached.ToList() };
            }

            var stopwatch = Stopwatch.StartNew();
            var rows = await _repository.GetTagsWithCountsAsync(cancellationToken);
            stopwatch.Stop();

            IReadOnlyList<TagWithCountDto> dtos = rows
                .Select(r => new TagWithCountDto { Id = r.Id, Name = r.Name, Count = r.Count })
                .ToList();

            _cache.Set(dtos);

            _logger.LogInformation(
                "Fetched {TagCount} photobank tags in {ElapsedMs} ms",
                dtos.Count,
                stopwatch.ElapsedMilliseconds);

            return new GetTagsResponse { Tags = dtos.ToList() };
        }
    }
}
```

- [ ] **Step 4: Run handler tests, confirm pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTagsHandlerTests"
```
Expected: 3 tests pass.

---

## Task 8: Adapt `PhotobankAutoTagJob` and its tests to the new return type

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs:47-48, 74-75`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs:31-34, 100-107, 141-150`

The job consumes only `Tag.Name` and `Tag.Id`. The dictionary is `Dictionary<string, Tag>` — switch the value type to `int` (the tag id) since downstream code only reads `tag.Id` to build `PhotoTag` rows.

- [ ] **Step 1: Rewrite the dictionary build sites in the job**

Edit `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`.

At line 47 (inside `ExecuteAsync`), replace:
```csharp
var tagsByName = (await _repo.GetTagsWithCountsAsync(cancellationToken))
    .ToDictionary(t => t.Tag.Name, t => t.Tag, StringComparer.Ordinal);
```
with:
```csharp
var tagsByName = (await _repo.GetTagsWithCountsAsync(cancellationToken))
    .ToDictionary(t => t.Name, t => t.Id, StringComparer.Ordinal);
```

At line 74 (inside `ExecuteForPhotosAsync`), apply the same replacement.

- [ ] **Step 2: Update the method signatures and call sites that consume the dictionary**

The current code calls `ProcessBatchAsync(batch, tagsByName, ...)` and `ApplyTagsForPhotoAsync(result, tagsByName, ...)` with `Dictionary<string, Tag>`. Update both to `Dictionary<string, int>` and adjust the `ApplyTagsForPhotoAsync` body.

Replace the `ProcessBatchAsync` signature at line 85:
```csharp
private async Task ProcessBatchAsync(
    IReadOnlyList<PhotoAutoTagCandidate> batch,
    Dictionary<string, Tag> tagsByName,
    CancellationToken ct)
```
with:
```csharp
private async Task ProcessBatchAsync(
    IReadOnlyList<PhotoAutoTagCandidate> batch,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
```

Replace the `ApplyTagsForPhotoAsync` signature (line 124) with:
```csharp
private async Task ApplyTagsForPhotoAsync(
    AutoTagResult result,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
```

Replace the body's tag lookup (lines 135-149):
```csharp
foreach (var tagName in validTags)
{
    var tag = tagsByName[tagName];

    if (await _repo.PhotoTagExistsAsync(result.Id, tag.Id, ct)) continue;

    await _repo.AddPhotoTagAsync(
        new PhotoTag
        {
            PhotoId = result.Id,
            TagId = tag.Id,
            Source = PhotoTagSource.AI,
            CreatedAt = DateTime.UtcNow,
        },
        ct);
}
```
with:
```csharp
foreach (var tagName in validTags)
{
    var tagId = tagsByName[tagName];

    if (await _repo.PhotoTagExistsAsync(result.Id, tagId, ct)) continue;

    await _repo.AddPhotoTagAsync(
        new PhotoTag
        {
            PhotoId = result.Id,
            TagId = tagId,
            Source = PhotoTagSource.AI,
            CreatedAt = DateTime.UtcNow,
        },
        ct);
}
```

- [ ] **Step 3: Update `PhotobankAutoTagJobTests` mocks to return `IReadOnlyList<TagCount>`**

Edit `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs`.

Replace the helper at lines 31-34:
```csharp
private void SetupEmptyTags() =>
    _repo
        .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<(Tag, int)>());
```
with:
```csharp
private void SetupEmptyTags() =>
    _repo
        .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TagCount>());
```

Replace the seeded list around line 100-107:
```csharp
var tags = new List<(Tag, int)>
{
    (new Tag { Id = 10, Name = "kosmetika" }, 5),
};

_repo
    .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(tags);
```
with:
```csharp
var tags = new List<TagCount>
{
    new(10, "kosmetika", 5),
};

_repo
    .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(tags);
```

Replace the tuple list around lines 141-150:
```csharp
var tags = new List<(Tag Tag, int Count)>
{
    (new Tag { Id = 1, Name = "andy" }, 1),
    (new Tag { Id = 2, Name = "ela" }, 1),
    (new Tag { Id = 3, Name = "peťa" }, 1),
};

_repo
    .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(tags);
```
with:
```csharp
var tags = new List<TagCount>
{
    new(1, "andy", 1),
    new(2, "ela", 1),
    new(3, "peťa", 1),
};

_repo
    .Setup(r => r.GetTagsWithCountsAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(tags);
```

If there are additional occurrences after line 197, repeat the same `(Tag, int)` → `TagCount` shape conversion. After editing, grep to confirm none remain:

```bash
grep -n 'List<(Tag' backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
```
Expected: no output.

- [ ] **Step 4: Verify the full solution builds**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` — no compile errors.

- [ ] **Step 5: Run the photobank test suite**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"
```
Expected: all photobank tests pass (including `PhotobankAutoTagJobTests`, `PhotobankRepositoryGetTagsTests`, `PhotobankTagsCacheTests`, `GetTagsHandlerTests`, plus pre-existing tests).

- [ ] **Step 6: Commit the query rewrite, handler, job, and tests together**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetTags/GetTagsHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryGetTagsTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/GetTagsHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
git commit -m "perf: rewrite photobank GetTags as single GROUP BY with cache-aside read path"
```

---

## Task 9: Wire invalidation into mutating handlers (one test per handler)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/CreateTag/CreateTagHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteTag/DeleteTagHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddPhotoTag/AddPhotoTagHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RemovePhotoTag/RemovePhotoTagHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RetagPhotos/RetagPhotosHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs` (already touched in Task 8)
- Test:   `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs`

This task is one bundled invalidation test file + a series of small handler edits. Steps below are individual edits; commit at the end of the task once all tests pass.

- [ ] **Step 1: Write the failing invalidation test matrix**

Create `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.BulkAddPhotoTagByIds;
using Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules;
using Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag;
using Anela.Heblo.Application.Features.Photobank.UseCases.RetagPhotos;
using Anela.Heblo.Domain.Features.Photobank;
using Anela.Heblo.Xcc.Services;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankTagsCacheInvalidationTests
{
    private readonly Mock<IPhotobankRepository> _repo = new();
    private readonly Mock<IPhotobankTagsCache> _cache = new();

    [Fact]
    public async Task CreateTag_InvalidatesCache_OnNewTag()
    {
        _repo.Setup(r => r.GetTagByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Tag?)null);
        _repo.Setup(r => r.GetOrCreateTagAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 1, Name = "summer" });

        var handler = new CreateTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new CreateTagRequest { Name = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task CreateTag_DoesNotInvalidate_WhenTagAlreadyExisted()
    {
        _repo.Setup(r => r.GetTagByNameAsync("summer", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 1, Name = "summer" });

        var handler = new CreateTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new CreateTagRequest { Name = "summer" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task DeleteTag_InvalidatesCache_AfterSaveChanges()
    {
        var tag = new Tag { Id = 1, Name = "summer" };
        _repo.Setup(r => r.GetTagByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(tag);

        var handler = new DeleteTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new DeleteTagRequest { Id = 1 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task DeleteTag_DoesNotInvalidate_WhenTagNotFound()
    {
        _repo.Setup(r => r.GetTagByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Tag?)null);

        var handler = new DeleteTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new DeleteTagRequest { Id = 99 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task AddPhotoTag_InvalidatesCache_WhenNewPhotoTagAdded()
    {
        _repo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });
        _repo.Setup(r => r.GetOrCreateTagAsync("x", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "x" });
        _repo.Setup(r => r.PhotoTagExistsAsync(1, 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var handler = new AddPhotoTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new AddPhotoTagRequest { PhotoId = 1, TagName = "x" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task AddPhotoTag_DoesNotInvalidate_WhenTagAlreadyAttached()
    {
        _repo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });
        _repo.Setup(r => r.GetOrCreateTagAsync("x", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "x" });
        _repo.Setup(r => r.PhotoTagExistsAsync(1, 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var handler = new AddPhotoTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new AddPhotoTagRequest { PhotoId = 1, TagName = "x" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task RemovePhotoTag_InvalidatesCache_AfterSaveChanges()
    {
        _repo.Setup(r => r.GetPhotoByIdAsync(1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Photo { Id = 1, SharePointFileId = "sp", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow });

        var handler = new RemovePhotoTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new RemovePhotoTagRequest { PhotoId = 1, TagId = 5 }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task BulkAddPhotoTag_InvalidatesCache_WhenSomePhotosTagged()
    {
        _repo.Setup(r => r.CountFilteredPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(10);
        _repo.Setup(r => r.GetOrCreateTagAsync("x", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "x" });
        _repo.Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int> { 1, 2 });

        var handler = new BulkAddPhotoTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new BulkAddPhotoTagRequest { TagName = "x" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task BulkAddPhotoTag_DoesNotInvalidate_WhenNoPhotosNeedTagging()
    {
        _repo.Setup(r => r.CountFilteredPhotosAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(10);
        _repo.Setup(r => r.GetOrCreateTagAsync("x", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "x" });
        _repo.Setup(r => r.GetFilteredPhotoIdsMissingTagAsync(It.IsAny<List<string>?>(), It.IsAny<string?>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int>());

        var handler = new BulkAddPhotoTagHandler(_repo.Object, _cache.Object);
        await handler.Handle(new BulkAddPhotoTagRequest { TagName = "x" }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task BulkAddPhotoTagByIds_InvalidatesCache_WhenSomePhotosTagged()
    {
        _repo.Setup(r => r.GetOrCreateTagAsync("x", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Tag { Id = 5, Name = "x" });
        _repo.Setup(r => r.GetExistingPhotoIdsMissingTagAsync(It.IsAny<IReadOnlyList<int>>(), 5, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<int> { 1, 2 });
        _repo.Setup(r => r.CountExistingPhotosAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(2);

        var handler = new BulkAddPhotoTagByIdsHandler(_repo.Object, _cache.Object);
        await handler.Handle(new BulkAddPhotoTagByIdsRequest { TagName = "x", PhotoIds = new List<int> { 1, 2 } }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task ReapplyRules_InvalidatesCache_AfterSaveChanges()
    {
        _repo.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<TagRule>());
        _repo.Setup(r => r.ReapplyRulesAsync(It.IsAny<List<TagRule>>(), null, It.IsAny<CancellationToken>()))
             .ReturnsAsync(3);

        var handler = new ReapplyRulesHandler(_repo.Object, _cache.Object);
        await handler.Handle(new ReapplyRulesRequest(), CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task RetagPhotos_InvalidatesCache_WhenPhotosFound()
    {
        _repo.Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>
             {
                 new() { Id = 1, SharePointFileId = "sp-1", FileName = "f", FolderPath = "p", ModifiedAt = DateTime.UtcNow },
             });
        var bgWorker = new Mock<IBackgroundWorker>();
        bgWorker.Setup(w => w.Enqueue<Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs.PhotobankAutoTagJob>(It.IsAny<System.Linq.Expressions.Expression<Func<Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs.PhotobankAutoTagJob, Task>>>()))
             .Returns("job-1");

        var handler = new RetagPhotosHandler(_repo.Object, bgWorker.Object, _cache.Object);
        await handler.Handle(new RetagPhotosRequest { PhotoIds = new List<int> { 1 } }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task RetagPhotos_DoesNotInvalidate_WhenNoPhotosFound()
    {
        _repo.Setup(r => r.GetPhotosByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>());
        var bgWorker = new Mock<IBackgroundWorker>();

        var handler = new RetagPhotosHandler(_repo.Object, bgWorker.Object, _cache.Object);
        await handler.Handle(new RetagPhotosRequest { PhotoIds = new List<int> { 1 } }, CancellationToken.None);

        _cache.Verify(c => c.Invalidate(), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests, confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankTagsCacheInvalidationTests"
```
Expected: fails to compile — handlers do not yet accept `IPhotobankTagsCache`.

- [ ] **Step 3: Edit `CreateTagHandler`**

Replace `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/CreateTag/CreateTagHandler.cs` body of the class with:

```csharp
public class CreateTagHandler : IRequestHandler<CreateTagRequest, CreateTagResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IPhotobankTagsCache _cache;

    public CreateTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<CreateTagResponse> Handle(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var normalizedName = request.Name.Trim().ToLowerInvariant();

        var existing = await _repository.GetTagByNameAsync(normalizedName, cancellationToken);
        if (existing != null)
            return new CreateTagResponse { Id = existing.Id, Name = existing.Name, AlreadyExisted = true };

        var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
        if (tag is null)
            throw new InvalidOperationException($"GetOrCreateTagAsync returned null for '{normalizedName}'.");

        _cache.Invalidate();
        return new CreateTagResponse { Id = tag.Id, Name = tag.Name, AlreadyExisted = false };
    }
}
```

Add the `using Anela.Heblo.Application.Features.Photobank.Services;` directive at the top of the file.

- [ ] **Step 4: Edit `DeleteTagHandler`**

In `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteTag/DeleteTagHandler.cs`, add the `IPhotobankTagsCache` dependency and call `_cache.Invalidate()` immediately after `await _repository.SaveChangesAsync(cancellationToken);`. The early-return when the tag is not found must **not** invalidate.

Add `using Anela.Heblo.Application.Features.Photobank.Services;`. Final body:

```csharp
public class DeleteTagHandler : IRequestHandler<DeleteTagRequest, DeleteTagResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IPhotobankTagsCache _cache;

    public DeleteTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<DeleteTagResponse> Handle(DeleteTagRequest request, CancellationToken cancellationToken)
    {
        var tag = await _repository.GetTagByIdAsync(request.Id, cancellationToken);
        if (tag is null)
            return new DeleteTagResponse(ErrorCodes.PhotobankTagNotFound);

        var assignmentCount = tag.PhotoTags.Count;
        await _repository.DeleteTagAsync(tag, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        return new DeleteTagResponse { RemovedAssignmentCount = assignmentCount };
    }
}
```

- [ ] **Step 5: Edit `AddPhotoTagHandler`**

In `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddPhotoTag/AddPhotoTagHandler.cs`, add cache dependency. Invalidate **only** when a new `PhotoTag` was inserted (i.e. after `SaveChangesAsync`, not on the early-return path where the tag was already attached).

```csharp
public class AddPhotoTagHandler : IRequestHandler<AddPhotoTagRequest, AddPhotoTagResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IPhotobankTagsCache _cache;

    public AddPhotoTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<AddPhotoTagResponse> Handle(AddPhotoTagRequest request, CancellationToken cancellationToken)
    {
        var photo = await _repository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
        if (photo == null)
            return new AddPhotoTagResponse(ErrorCodes.PhotoNotFound);

        var normalizedName = request.TagName.Trim().ToLowerInvariant();
        var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
        if (tag == null)
            return new AddPhotoTagResponse(ErrorCodes.PhotoTagCreationFailed);

        if (await _repository.PhotoTagExistsAsync(photo.Id, tag.Id, cancellationToken))
            return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };

        var photoTag = new PhotoTag
        {
            PhotoId = photo.Id,
            TagId = tag.Id,
            Source = PhotoTagSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };

        await _repository.AddPhotoTagAsync(photoTag, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };
    }
}
```

- [ ] **Step 6: Edit `RemovePhotoTagHandler`**

```csharp
public class RemovePhotoTagHandler : IRequestHandler<RemovePhotoTagRequest, RemovePhotoTagResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IPhotobankTagsCache _cache;

    public RemovePhotoTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<RemovePhotoTagResponse> Handle(RemovePhotoTagRequest request, CancellationToken cancellationToken)
    {
        var photo = await _repository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
        if (photo == null)
            return new RemovePhotoTagResponse(ErrorCodes.PhotoNotFound);

        await _repository.RemovePhotoTagAsync(request.PhotoId, request.TagId, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        return new RemovePhotoTagResponse();
    }
}
```

- [ ] **Step 7: Edit `BulkAddPhotoTagHandler`**

Invalidate only when `photoIds.Count > 0` (same gate as `SaveChangesAsync`). Add cache dependency, then change the bottom of `Handle` from:

```csharp
if (photoIds.Count > 0)
    await _repository.SaveChangesAsync(cancellationToken);
```
to:
```csharp
if (photoIds.Count > 0)
{
    await _repository.SaveChangesAsync(cancellationToken);
    _cache.Invalidate();
}
```
And add `IPhotobankTagsCache` to the constructor + field.

- [ ] **Step 8: Edit `BulkAddPhotoTagByIdsHandler`**

Mirror the previous step:

```csharp
if (toAdd.Count > 0)
{
    await _repository.SaveChangesAsync(cancellationToken);
    _cache.Invalidate();
}
```

- [ ] **Step 9: Edit `ReapplyRulesHandler`**

```csharp
public class ReapplyRulesHandler : IRequestHandler<ReapplyRulesRequest, ReapplyRulesResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IPhotobankTagsCache _cache;

    public ReapplyRulesHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ReapplyRulesResponse> Handle(ReapplyRulesRequest request, CancellationToken cancellationToken)
    {
        var allRules = await _repository.GetRulesAsync(cancellationToken);

        string? scopeToTagName = null;
        if (request.RuleId.HasValue)
        {
            var rule = allRules.FirstOrDefault(r => r.Id == request.RuleId.Value);
            if (rule == null)
                return new ReapplyRulesResponse(ErrorCodes.PhotobankRuleNotFound);

            scopeToTagName = rule.TagName.ToLowerInvariant();
        }

        var photosUpdated = await _repository.ReapplyRulesAsync(allRules, scopeToTagName, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
    }
}
```

- [ ] **Step 10: Edit `RetagPhotosHandler`**

Add cache dependency; invalidate only when `photos.Count > 0` (the `ExecuteUpdate` / `ExecuteDelete` calls commit immediately, no `SaveChangesAsync`).

```csharp
public class RetagPhotosHandler : IRequestHandler<RetagPhotosRequest, RetagPhotosResponse>
{
    private readonly IPhotobankRepository _repository;
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IPhotobankTagsCache _cache;

    public RetagPhotosHandler(
        IPhotobankRepository repository,
        IBackgroundWorker backgroundWorker,
        IPhotobankTagsCache cache)
    {
        _repository = repository;
        _backgroundWorker = backgroundWorker;
        _cache = cache;
    }

    public async Task<RetagPhotosResponse> Handle(RetagPhotosRequest request, CancellationToken cancellationToken)
    {
        var photos = await _repository.GetPhotosByIdsAsync(request.PhotoIds, cancellationToken);

        if (photos.Count == 0)
            return new RetagPhotosResponse { JobId = null };

        var foundIds = photos.Select(p => p.Id).ToList();

        await _repository.ResetAutoTaggedAtAsync(foundIds, cancellationToken);

        if (request.ClearExistingAiTags)
            await _repository.RemovePhotoTagsBySourceAsync(foundIds, PhotoTagSource.AI, cancellationToken);

        _cache.Invalidate();

        var candidates = photos
            .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
            .ToList();

        var jobId = _backgroundWorker.Enqueue<PhotobankAutoTagJob>(
            j => j.ExecuteForPhotosAsync(candidates, CancellationToken.None));

        return new RetagPhotosResponse { JobId = jobId };
    }
}
```

Add `using Anela.Heblo.Application.Features.Photobank.Services;` at the top.

- [ ] **Step 11: Edit `PhotobankAutoTagJob` to invalidate after each batch**

In `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`, add a constructor parameter `IPhotobankTagsCache cache` (stored as `_cache`) and call `_cache.Invalidate()` at the end of `ProcessBatchAsync`, after `StampAutoTaggedAtAsync`.

```csharp
public PhotobankAutoTagJob(
    IPhotobankRepository repo,
    IChatClient chat,
    IOptions<AutoTagOptions> options,
    ILogger<PhotobankAutoTagJob> logger,
    IPhotobankTagsCache cache)
{
    _repo = repo;
    _chat = chat;
    _options = options.Value;
    _logger = logger;
    _cache = cache;
}
```

Add the field declaration `private readonly IPhotobankTagsCache _cache;` next to the other fields. Add `using Anela.Heblo.Application.Features.Photobank.Services;`.

At the bottom of `ProcessBatchAsync`, after:
```csharp
await _repo.SaveChangesAsync(ct);
await _repo.StampAutoTaggedAtAsync(batchIds, DateTime.UtcNow, ct);
```
add:
```csharp
_cache.Invalidate();
```

Update `PhotobankAutoTagJobTests.cs`: the `CreateJob` helper needs the new constructor argument. Replace lines 21-29:

```csharp
private PhotobankAutoTagJob CreateJob(AutoTagOptions? options = null)
{
    var opts = options ?? new AutoTagOptions { Enabled = true, BatchSize = 50, MaxPhotosPerRun = 5_000 };
    return new PhotobankAutoTagJob(
        _repo.Object,
        _chat.Object,
        Options.Create(opts),
        NullLogger<PhotobankAutoTagJob>.Instance);
}
```
with:
```csharp
private readonly Mock<IPhotobankTagsCache> _cache = new();

private PhotobankAutoTagJob CreateJob(AutoTagOptions? options = null)
{
    var opts = options ?? new AutoTagOptions { Enabled = true, BatchSize = 50, MaxPhotosPerRun = 5_000 };
    return new PhotobankAutoTagJob(
        _repo.Object,
        _chat.Object,
        Options.Create(opts),
        NullLogger<PhotobankAutoTagJob>.Instance,
        _cache.Object);
}
```
Add `using Anela.Heblo.Application.Features.Photobank.Services;` at the top of the test file.

Also update the standalone `new PhotobankAutoTagJob(...)` call inside the `RespectsMaxTagsPerPhoto_Cap` test (around line 182) to pass `_cache.Object` as the fifth argument.

- [ ] **Step 12: Run the full invalidation matrix**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankTagsCacheInvalidationTests"
```
Expected: all 14 tests pass.

- [ ] **Step 13: Run the full photobank suite to confirm nothing else regressed**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"
```
Expected: all photobank tests pass.

- [ ] **Step 14: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/CreateTag/CreateTagHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteTag/DeleteTagHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddPhotoTag/AddPhotoTagHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RemovePhotoTag/RemovePhotoTagHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTag/BulkAddPhotoTagHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/BulkAddPhotoTagByIds/BulkAddPhotoTagByIdsHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/RetagPhotos/RetagPhotosHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankTagsCacheInvalidationTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankAutoTagJobTests.cs
git commit -m "feat: invalidate photobank tags cache on every tag/photo-tag mutation"
```

---

## Task 10: Wire DI registrations into `PhotobankModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`

- [ ] **Step 1: Add `using` directives**

At the top of the file, add:
```csharp
using Anela.Heblo.Application.Features.Photobank.Configuration;
using Microsoft.Extensions.Caching.Memory;
```

- [ ] **Step 2: Register cache options + memory cache + scoped wrapper**

Inside `AddPhotobankModule`, immediately after the `services.Configure<AutoTagOptions>(...)` line, append:

```csharp
services.AddMemoryCache();
services.Configure<PhotobankTagsCacheOptions>(
    configuration.GetSection(PhotobankTagsCacheOptions.SectionName));
services.AddScoped<IPhotobankTagsCache, PhotobankTagsCache>();
```

- [ ] **Step 3: Verify the build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.`

- [ ] **Step 4: Smoke test — boot the API to confirm DI graph resolves**

Run:
```bash
dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --launch-profile https
```
In another terminal, hit:
```bash
curl -sk https://localhost:5001/health
```
Expected: HTTP 200 (or whatever health endpoint exists). Then `Ctrl+C` to stop. If the host fails to start with `InvalidOperationException: Unable to resolve service ...`, fix the DI registration; do not skip.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs
git commit -m "feat: register PhotobankTagsCache and options in PhotobankModule"
```

---

## Task 11: Add cache TTL to `appsettings.json`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Edit the `Photobank` section**

In `backend/src/Anela.Heblo.API/appsettings.json`, change the `"Photobank"` block from:
```json
"Photobank": {
  "AutoTag": {
    "Enabled": false,
    "BatchSize": 50,
    "MaxPhotosPerRun": 5000,
    "Model": "claude-haiku-4-5-20251001",
    "MaxTagsPerPhoto": 5
  }
}
```
to:
```json
"Photobank": {
  "AutoTag": {
    "Enabled": false,
    "BatchSize": 50,
    "MaxPhotosPerRun": 5000,
    "Model": "claude-haiku-4-5-20251001",
    "MaxTagsPerPhoto": 5
  },
  "TagsCache": {
    "TtlSeconds": 60
  }
}
```

- [ ] **Step 2: Confirm JSON parses**

Run:
```bash
python3 -c "import json; json.load(open('backend/src/Anela.Heblo.API/appsettings.json'))"
```
Expected: no output (i.e. valid JSON).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "chore: add Photobank:TagsCache:TtlSeconds config (60s default)"
```

---

## Task 12: End-to-end validation against the API

**Files:** none (validation only).

- [ ] **Step 1: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Apply formatting**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```
Expected: completes with no required changes (or applies minor changes).

- [ ] **Step 3: Run all backend tests**

Run:
```bash
dotnet test backend/Anela.Heblo.sln
```
Expected: all tests pass.

- [ ] **Step 4: Manual API smoke test**

Boot the API (`dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`) and hit the endpoint twice:

```bash
# First call — cache miss, should hit DB
time curl -sk -H "Authorization: Bearer <token>" https://localhost:5001/api/photobank/tags > /tmp/tags-1.json

# Second call within 60s — cache hit, should be much faster
time curl -sk -H "Authorization: Bearer <token>" https://localhost:5001/api/photobank/tags > /tmp/tags-2.json

diff /tmp/tags-1.json /tmp/tags-2.json
```
Expected: payloads identical; second call's wall-clock time noticeably lower than first. Log output for the first call must contain `Fetched {N} photobank tags in {ElapsedMs} ms` at Information level.

- [ ] **Step 5: Manual invalidation check**

While the API is still running:
```bash
# 1. Read tags (warms cache)
curl -sk -H "Authorization: Bearer <token>" https://localhost:5001/api/photobank/tags > /dev/null

# 2. Create a new tag
curl -sk -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"smoke-test-tag"}' https://localhost:5001/api/photobank/tags

# 3. Read again — must hit DB (cache invalidated). New tag must appear with count 0.
curl -sk -H "Authorization: Bearer <token>" https://localhost:5001/api/photobank/tags \
  | jq '.tags[] | select(.name=="smoke-test-tag")'
```
Expected: third call's log line shows `Fetched {N+1} photobank tags in {ElapsedMs} ms` at Information level, and the new tag is present in the response with `count: 0`.

- [ ] **Step 6: Clean up the smoke-test tag**

```bash
# Identify the tag id then call DELETE /api/photobank/tags/{id}
curl -sk -X DELETE -H "Authorization: Bearer <token>" https://localhost:5001/api/photobank/tags/<id>
```

---

## Task 13: (Conditional) Add `IX_PhotoTags_TagId` migration

**Run this task only if Task 1, Step 3 showed the index is missing from the deployed database.** Otherwise this task is `SKIPPED — index already exists`.

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddPhotoTagsTagIdIndex.cs` (generated)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddPhotoTagsTagIdIndex.Designer.cs` (generated)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (generated)
- Modify: `backend/src/Anela.Heblo.Persistence/Photobank/PhotoTagConfiguration.cs` (only if the snapshot diff says `HasIndex("TagId")` would otherwise disappear)

- [ ] **Step 1: Generate the migration**

Run:
```bash
dotnet ef migrations add AddPhotoTagsTagIdIndex \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: three new/modified files listed above. Open the generated `Up`/`Down` to confirm:

```csharp
migrationBuilder.CreateIndex(
    name: "IX_PhotoTags_TagId",
    schema: "public",
    table: "PhotoTags",
    column: "TagId");
```

If EF generated nothing (the model snapshot already matches), the index exists in the model and this task is genuinely a no-op. Delete the empty migration with:
```bash
dotnet ef migrations remove --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
and mark this task as SKIPPED.

- [ ] **Step 2: Mirror the index in `PhotoTagConfiguration` (only if the snapshot diff requires it)**

If the snapshot diff *removed* `b.HasIndex("TagId")`, restore it via the configuration to keep the implicit FK index. Add to `backend/src/Anela.Heblo.Persistence/Photobank/PhotoTagConfiguration.cs`:

```csharp
builder.HasIndex(x => x.TagId).HasDatabaseName("IX_PhotoTags_TagId");
```

- [ ] **Step 3: Script the migration for production (CONCURRENTLY)**

Generate the SQL script:
```bash
dotnet ef migrations script {PreviousMigrationName} AddPhotoTagsTagIdIndex \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API \
  --output ./migration-AddPhotoTagsTagIdIndex.sql
```

Open `migration-AddPhotoTagsTagIdIndex.sql` and replace the generated `CREATE INDEX "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");` with:

```sql
CREATE INDEX CONCURRENTLY "IX_PhotoTags_TagId" ON public."PhotoTags" ("TagId");
```

Document the apply window (outside the 04:00 UTC `photobank-auto-tag` job) per the manual-migration workflow in `CLAUDE.md`.

- [ ] **Step 4: Build the solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/*AddPhotoTagsTagIdIndex* \
        backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs \
        backend/src/Anela.Heblo.Persistence/Photobank/PhotoTagConfiguration.cs \
        migration-AddPhotoTagsTagIdIndex.sql
git commit -m "feat: add IX_PhotoTags_TagId index migration (apply manually with CONCURRENTLY)"
```

---

## Self-Review Notes

- **Spec coverage:**
  - FR-1 (single statement) — Task 6.
  - FR-2 (index) — Task 1 verifies; Task 13 ships only if needed.
  - FR-3 (cache + invalidation) — Tasks 3, 5, 9 (invalidation matrix covers all 9 mutators).
  - FR-4 (DTO projection, no tracked entities) — Tasks 2 + 6.
  - FR-5 (logging) — Task 7.
  - NFR-1 (performance) — Task 12 manual validation.
  - NFR-3 (backward compatibility) — response shape preserved (verified in Task 7 tests + Task 12 smoke test).
  - NFR-4 (testability) — full integration matrix in Tasks 5, 6, 7, 9.
- **Placeholders:** none — every code step has a full snippet.
- **Type consistency:** `IPhotobankTagsCache` uses `TryGet(out IReadOnlyList<TagWithCountDto>?)` across interface, implementation, and tests; repository returns `IReadOnlyList<TagCount>` end-to-end; cache payload type is `IReadOnlyList<TagWithCountDto>` everywhere.

---

## Pipeline Note

This plan was produced by the automated pipeline. The plan file itself is the deliverable; no human handoff step is required. Downstream agents pick this up via `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
