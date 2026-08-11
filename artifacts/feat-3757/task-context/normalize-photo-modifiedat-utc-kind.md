### task: normalize-photo-modifiedat-utc-kind

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs:181`
- Test: `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `PhotobankIndexJobTests.cs` (same file, same mocking pattern as the existing tests
in that class — copy the Arrange block from `ExecuteAsync_InsertsNewPhoto_WithRuleTagsApplied` and
adjust only what's shown):

```csharp
[Fact]
public async Task UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc()
{
    // Arrange — simulate System.Text.Json handing back a DateTime with Kind=Unspecified for
    // the Graph delta item's lastModifiedDateTime (the one Photobank DateTime value sourced
    // from something other than DateTime.UtcNow). Photo.ModifiedAt must never inherit that
    // Kind as-is: PhotobankRootRepository/PhotobankPhotoRepository share one ApplicationDbContext
    // whose global convention strips Kind before every write, so the column-type mapping is
    // what actually determines success/failure — but this test only needs to prove the
    // application-layer contract: the assigned Kind is always Utc, regardless of the source's Kind.
    var unspecifiedInstant = new DateTime(2026, 7, 27, 1, 28, 0, DateTimeKind.Unspecified);

    var root = new PhotobankIndexRoot
    {
        Id = 1,
        SharePointPath = "/sites/test/photos",
        DriveId = "drive-1",
        RootItemId = "root-item-1",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    var photoItem = new GraphPhotoItem
    {
        ItemId = "file-kind-test",
        Name = "photo.jpg",
        FolderPath = "Fotky/Produkty",
        WebUrl = "https://sharepoint.example.com/photo.jpg",
        FileSizeBytes = 1024,
        LastModifiedAt = unspecifiedInstant,
        DriveId = "drive-1",
        IsDeleted = false,
    };

    _rootRepoMock
        .Setup(r => r.GetActiveRootsWithDriveAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync([root]);

    _tagRuleRepoMock
        .Setup(r => r.GetActiveTagRulesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TagRule>());

    _photoRepoMock
        .Setup(r => r.GetPhotoBySharePointFileIdAsync("file-kind-test", It.IsAny<CancellationToken>()))
        .ReturnsAsync((Photo?)null);

    Photo? capturedPhoto = null;
    _photoRepoMock
        .Setup(r => r.AddPhotoAsync(It.IsAny<Photo>(), It.IsAny<CancellationToken>()))
        .Callback<Photo, CancellationToken>((p, _) => capturedPhoto = p)
        .Returns(Task.CompletedTask);

    _photoTagRepoMock
        .Setup(r => r.GetPhotoTagsByPhotoAndSourceAsync(It.IsAny<int>(), PhotoTagSource.Rule, It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);

    _photoTagRepoMock
        .Setup(r => r.RemovePhotoTagsAsync(It.IsAny<IEnumerable<PhotoTag>>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _photoRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _photoTagRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _rootRepoMock
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    _graphServiceMock
        .Setup(g => g.GetDeltaAsync("drive-1", "root-item-1", null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GraphDeltaResult
        {
            Items = [photoItem],
            NewDeltaLink = "https://graph.microsoft.com/v1.0/drives/drive-1/items/root-item-1/delta?token=abc",
        });

    // Act
    await _job.ExecuteAsync();

    // Assert
    capturedPhoto.Should().NotBeNull();
    capturedPhoto!.ModifiedAt.Kind.Should().Be(DateTimeKind.Utc);
    capturedPhoto.ModifiedAt.Should().Be(DateTime.SpecifyKind(unspecifiedInstant, DateTimeKind.Utc));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests.UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc"`

Expected: FAIL — `capturedPhoto.ModifiedAt.Kind` is `DateTimeKind.Unspecified` (the current code
assigns `item.LastModifiedAt` as-is).

- [ ] **Step 3: Write the minimal implementation**

In `PhotobankIndexJob.cs`, replace line 181:

```csharp
photo.ModifiedAt = item.LastModifiedAt ?? DateTime.UtcNow;
```

with:

```csharp
photo.ModifiedAt = item.LastModifiedAt.HasValue
    ? DateTime.SpecifyKind(item.LastModifiedAt.Value, DateTimeKind.Utc)
    : DateTime.UtcNow;
```

Do not change any other line in `UpsertPhotoBatchAsync` or elsewhere in the file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests.UpsertPhotoBatch_GraphItemLastModifiedAtHasUnspecifiedKind_PhotoModifiedAtIsStampedUtc"`

Expected: PASS

- [ ] **Step 5: Run the full PhotobankIndexJobTests fixture to confirm no regression**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankIndexJobTests"`

Expected: PASS (all existing tests in this file continue to pass — this change only affects the
value assigned to `ModifiedAt.Kind`, not any field value asserted by existing tests, since existing
tests use `DateTime.UtcNow`, which is already `Kind=Utc` and unaffected by `SpecifyKind`).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs
git commit -m "fix(photobank): stamp Photo.ModifiedAt as Kind=Utc when sourced from Graph delta items"
```

---
