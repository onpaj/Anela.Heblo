### task: add-bulk-tag-lookup-repository-method

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`

This task adds the one new repository primitive the fix needs. It does **not** touch `PhotobankIndexJob` yet — that is Task 2. `GetOccupiedTagPairsAsync` is reused completely unmodified (per architect Decision 1 discussion): the existing signature `GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken)` already returns `HashSet<(int PhotoId, int TagId)>` for the *entire* non-Rule-tag table (or a tag-name-scoped subset). No photo-ID-scoped overload is added — the batch job will call it exactly like `ReapplyRulesHandler` already does (unscoped by photo ID) and rely on `IN`-clause-free full retrieval; this keeps the change purely additive to the interface (Decision 2 in `arch-review.r1.md`) and avoids widening this task's blast radius. If a future profiling pass shows this is too costly at current `PhotoTags` table size, that is out of scope here (see spec's "Out of Scope" section).

- [ ] **Step 1: Write the failing repository test**

Add to `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`, right after `GetOccupiedTagPairsAsync_scoped_filtersByTagName` (after line 179, before `AddPhotoTagsAsync_stagesRows_persistedAfterSave`):

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_multiplePhotos_returnsOnlyMatchingSourceGroupedByPhotoId()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "P", ModifiedAt = DateTime.UtcNow });
        _context.PhotobankTags.AddRange(
            new Tag { Id = 10, Name = "products" },
            new Tag { Id = 11, Name = "events" },
            new Tag { Id = 12, Name = "manualtag" });
        _context.PhotoTags.AddRange(
            new PhotoTag { PhotoId = 1, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 1, TagId = 11, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow },
            new PhotoTag { PhotoId = 2, TagId = 12, Source = PhotoTagSource.Manual, CreatedAt = DateTime.UtcNow },
            // Photo 3 has no Rule tags at all, and is NOT in the requested photoIds set below.
            new PhotoTag { PhotoId = 3, TagId = 10, Source = PhotoTagSource.Rule, CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — only ask for photos 1 and 2; photo 3's Rule tag must not leak in even though
        // it matches the source filter, because it is outside the requested photo-ID set.
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            new[] { 1, 2 }, PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[1].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10, 11 });
        result[2].Select(pt => pt.TagId).Should().BeEquivalentTo(new[] { 10 }); // Manual tag excluded
        result.Should().NotContainKey(3);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoTagsByPhotosAndSourceAsync_emptyPhotoIds_returnsEmptyDictionaryWithoutQuerying()
    {
        // Act
        var result = await _photoTagRepository.GetPhotoTagsByPhotosAndSourceAsync(
            Array.Empty<int>(), PhotoTagSource.Rule, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail (method does not exist yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
Expected: build FAILS — `PhotobankPhotoTagRepository` has no method `GetPhotoTagsByPhotosAndSourceAsync`.

- [ ] **Step 3: Add the method to the interface**

In `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs`, add a new line inside the interface body (after the existing `GetPhotoTagsByPhotoAndSourceAsync` line, i.e. after current line 15):

```csharp
        Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);
```

The full interface file becomes:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Domain.Features.Photobank
{
    public interface IPhotobankPhotoTagRepository
    {
        Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
        Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
        Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken cancellationToken);
        Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
        Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);
        Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
        Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
```

`PhotoTagExistsAsync` and the singular `GetPhotoTagsByPhotoAndSourceAsync` are left in place untouched — they are not removed by this task (see architect Decision 2: additive-only interface change; `PhotoTagExistsAsync`'s removal from the job's call path happens in Task 2 but the interface method itself is not deleted since removing an interface member is a wider-blast-radius change than this fix needs, and no other repository implementation exists to worry about — grep confirms `PhotobankPhotoTagRepository` is the only implementer).

- [ ] **Step 4: Implement the method**

In `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`, add this method (after `GetPhotoTagsByPhotoAndSourceAsync`, i.e. after current line 75, before `RemovePhotoTagsAsync`):

```csharp
    public async Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(
        IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken)
    {
        if (photoIds.Count == 0)
            return new Dictionary<int, List<PhotoTag>>();

        var rows = await _context.PhotoTags
            .Where(pt => photoIds.Contains(pt.PhotoId) && pt.Source == source)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(pt => pt.PhotoId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"`
Expected: PASS — all tests in the file, including the two new ones.

- [ ] **Step 6: Build the whole solution to confirm the new interface member doesn't break any other implementer**

Run: `dotnet build Anela.Heblo.sln`
Expected: Build succeeded, 0 errors. (`PhotobankPhotoTagRepository` is the only implementer of `IPhotobankPhotoTagRepository`, so no other class needs updating — confirmed by the architect review's grep.)

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "feat(photobank): add batch-scoped GetPhotoTagsByPhotosAndSourceAsync repository method"
```

---
