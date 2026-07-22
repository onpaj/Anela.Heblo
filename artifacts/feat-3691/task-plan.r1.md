# Paginate photo loading in ReapplyRulesHandler Implementation Plan

**Goal:** Replace `ReapplyRulesHandler`'s single unbounded `GetAllPhotosAsync()` load of the entire `Photos` table with a paginated, projected fetch, so peak memory during a rule-reapply request scales with page size, not with total photo count.

**Architecture:** `IPhotobankRepository` gains `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken)`, implemented in `PhotobankRepository` as an `AsNoTracking().OrderBy(p => p.Id).Skip(offset).Take(pageSize).Select(...)` query projecting into the existing `PhotoAutoTagCandidate(int Id, string FolderPath, string FileName)` record — mirroring the already-proven `GetPhotosPendingAutoTagAsync` pattern used by the auto-tag job. `ReapplyRulesHandler.Handle` replaces its single bulk load + `foreach` with a `while` loop that pages through results (page size 2000, private const) until a page returns fewer rows than the page size. The now-unused `GetAllPhotosAsync` is deleted from the interface, implementation, and its test coverage once the handler no longer calls it.

**Tech Stack:** .NET 8, Entity Framework Core (InMemory provider for repository tests), MediatR, xUnit, Moq, FluentAssertions.

---

### task: add-photo-rule-candidates-page-method

**Scope:** Add the new paginated repository method (interface + implementation) and its tests. `GetAllPhotosAsync` is left in place and untouched in this task — the handler still calls it, so the build and all existing tests stay green throughout. This task only adds new, additive surface.

#### Step 1: Read the existing repository test file to confirm exact conventions

Read `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`. Note the test class uses an EF Core InMemory `ApplicationDbContext` per test (fresh `Guid.NewGuid()` database name in the constructor), constructs a real `PhotobankRepository` against it, and disposes the context in `Dispose()`. New tests must follow this exact pattern (no mocking — this is a repository-level test against a real in-memory `DbContext`).

#### Step 2: Write the failing tests for the new method

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`, add two new test methods immediately after the existing `GetAllPhotosAsync_returnsAllPhotos` test (do not remove that test yet — it still passes against the untouched `GetAllPhotosAsync`):

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetPhotoRuleCandidatesPageAsync_firstPage_returnsProjectionOrderedById()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var page = await _repository.GetPhotoRuleCandidatesPageAsync(pageSize: 2, offset: 0, CancellationToken.None);

        // Assert
        page.Should().HaveCount(2);
        page.Select(p => p.Id).Should().Equal(new[] { 1, 2 }); // ordered by Id, not insertion order
        page[0].FolderPath.Should().Be("Products");
        page[0].FileName.Should().Be("a.jpg");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPhotoRuleCandidatesPageAsync_secondPage_returnsRemainingRowsViaOffset()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 3, SharePointFileId = "sp-3", FileName = "c.jpg", FolderPath = "Archive", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act — page size 2: first page has 2 rows, second page (offset 2) has the remaining 1
        var secondPage = await _repository.GetPhotoRuleCandidatesPageAsync(pageSize: 2, offset: 2, CancellationToken.None);

        // Assert
        secondPage.Should().ContainSingle();
        secondPage[0].Id.Should().Be(3);
        secondPage[0].FolderPath.Should().Be("Archive");
        secondPage[0].FileName.Should().Be("c.jpg");
    }
```

These reference `PhotoAutoTagCandidate`'s properties (`Id`, `FolderPath`, `FileName`), already imported via the file's existing `using Anela.Heblo.Domain.Features.Photobank;`.

#### Step 3: Run the tests and confirm they fail to compile (red)

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"
```

Expected: build error — `'PhotobankRepository' does not contain a definition for 'GetPhotoRuleCandidatesPageAsync'`. This confirms the test is exercising code that doesn't exist yet.

#### Step 4: Add the method to `IPhotobankRepository`

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`, add the new method signature directly below the existing `// Auto-tagging` section's `GetPhotosPendingAutoTagAsync` line (line 69), under a new comment block:

```csharp
        // Auto-tagging
        Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken cancellationToken);
        Task StampAutoTaggedAtAsync(IReadOnlyList<int> photoIds, DateTime timestamp, CancellationToken cancellationToken);
        Task ResetAutoTaggedAtAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
        Task<List<Photo>> GetPhotosByIdsAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
        Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

        // Rule reapply
        Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken);

        Task SaveChangesAsync(CancellationToken cancellationToken);
```

(This replaces the block from `// Auto-tagging` through the final `Task SaveChangesAsync(...)` line, inserting the new `// Rule reapply` section between them. `GetAllPhotosAsync` on line 28 of the `// Photos` region is left untouched in this task.)

#### Step 5: Implement the method in `PhotobankRepository`

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`, add the implementation immediately after `GetPhotosPendingAutoTagAsync` (currently ending at line 397) and before `StampAutoTaggedAtAsync`:

```csharp
    public async Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(
        int pageSize, int offset, CancellationToken cancellationToken)
    {
        return await _context.Photos
            .Where(p => p.LastAutoTaggedAt == null)
            .OrderBy(p => p.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(
        int pageSize, int offset, CancellationToken cancellationToken)
    {
        return await _context.Photos
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
            .ToListAsync(cancellationToken);
    }

    public async Task StampAutoTaggedAtAsync(
```

(Only the new `GetPhotoRuleCandidatesPageAsync` method is inserted; `GetPhotosPendingAutoTagAsync` and `StampAutoTaggedAtAsync` shown above for exact placement context, unchanged.)

#### Step 6: Run the tests and confirm they pass (green)

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PhotobankRepositoryReapplyPrimitivesTests"
```

Expected: all tests in `PhotobankRepositoryReapplyPrimitivesTests` pass, including the two new ones and the still-untouched `GetAllPhotosAsync_returnsAllPhotos`.

#### Step 7: Build the whole solution

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.` No other type implements `IPhotobankRepository` besides `PhotobankRepository`, so no other production code needs updating for this additive interface change.

#### Step 8: Commit

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "Add paginated GetPhotoRuleCandidatesPageAsync to IPhotobankRepository"
```

---

### task: migrate-reapplyrules-handler-to-paginated-fetch

**Scope:** Switch `ReapplyRulesHandler.Handle` from the single `GetAllPhotosAsync` bulk load to the paginated `GetPhotoRuleCandidatesPageAsync` loop added in the previous task, and update `ReapplyRulesHandlerTests.cs`'s mocks accordingly. `GetAllPhotosAsync` itself is still not removed in this task (that's the next task) — it simply becomes unused by production code.

#### Step 1: Read the current handler and its test file

Read `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` and `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` to confirm current line numbers before editing (line numbers below match the versions read during planning; re-verify before editing since earlier tasks do not touch these files).

#### Step 2: Update the handler test mocks and fixture to use the new method (write first — red)

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs`:

Replace the constructor's default `GetAllPhotosAsync` mock setup:

```csharp
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Photo>());
```

with:

```csharp
        _repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<PhotoAutoTagCandidate>());
```

Replace the `PhotoAt` helper with a `CandidateAt` helper that builds the projection type directly (the handler now only ever sees `PhotoAutoTagCandidate`, so tests no longer need full `Photo` entities for this purpose — remove `PhotoAt` since nothing else in this file uses it):

```csharp
    private static PhotoAutoTagCandidate CandidateAt(int id, string folder, string file) =>
        new(id, folder, file);
```

In `HappyPath_AddsRuleTags_CountsPhotos_InvalidatesOnce`, replace:

```csharp
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"),
            PhotoAt(2, "Products/B", "b.jpg"),
            PhotoAt(3, "Events/C", "c.jpg"), // no match
        });
```

with:

```csharp
        _repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"),
            CandidateAt(2, "Products/B", "b.jpg"),
            CandidateAt(3, "Events/C", "c.jpg"), // no match
        });
```

In `ManualAiPrecedence_OccupiedPairNotAdded`, replace:

```csharp
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"),
        });
```

with:

```csharp
        _repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"),
        });
```

In `DuplicateMatch_CountedOnce`, replace:

```csharp
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/A", "a.jpg"), // matches both rules → still one (1,10) pair
        });
```

with:

```csharp
        _repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/A", "a.jpg"), // matches both rules → still one (1,10) pair
        });
```

In `SingleRule_ScopesEveryStepToTagName`, replace:

```csharp
        _repo.Setup(r => r.GetAllPhotosAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Photo>
        {
            PhotoAt(1, "Products/Events", "a.jpg"), // matches both rules' patterns
        });
```

with:

```csharp
        _repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<PhotoAutoTagCandidate>
        {
            CandidateAt(1, "Products/Events", "a.jpg"), // matches both rules' patterns
        });
```

#### Step 3: Run the handler tests and confirm they fail (red)

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesHandlerTests"
```

Expected: compile succeeds (the interface method already exists from the previous task), but tests fail at runtime — the handler still calls `GetAllPhotosAsync`, which is now unmocked by these tests (Moq's loose mock returns `null` for unconfigured calls returning a `Task<List<Photo>>`... actually it will throw a `NullReferenceException` when the handler does `foreach (var photo in photos)` over `photos == null`, or similar). Confirm the run reports failures for the four updated tests (the two tests that don't touch photo data — `RuleNotFound_ReturnsError...` and `NoActiveRuleTagNames_...` — remain unaffected either way since they short-circuit before reaching the photo loop).

#### Step 4: Update the handler implementation

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`, add the page size constant to the class:

```csharp
    public class ReapplyRulesHandler : IRequestHandler<ReapplyRulesRequest, ReapplyRulesResponse>
    {
        private const int PageSize = 2000;

        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;
```

Replace the single bulk load:

```csharp
            var photos = await _repository.GetAllPhotosAsync(cancellationToken);

            var addedPairs = new HashSet<(int PhotoId, int TagId)>();
            var newPhotoTags = new List<PhotoTag>();
            var now = DateTime.UtcNow;
            var photosUpdated = 0;

            foreach (var photo in photos)
            {
                var allMatchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules);
                var matchingTagNames = scopeToTagName != null
                    ? (IReadOnlyList<string>)allMatchingTagNames.Where(n => n == scopeToTagName).ToList()
                    : allMatchingTagNames;

                if (matchingTagNames.Count == 0)
                    continue;

                var tagsUpdated = false;
                foreach (var tagName in matchingTagNames)
                {
                    if (!tagIdsByName.TryGetValue(tagName, out var tagId))
                        continue;

                    var pair = (photo.Id, tagId);
                    if (!addedPairs.Add(pair))
                        continue;

                    if (occupied.Contains(pair))
                        continue;

                    newPhotoTags.Add(new PhotoTag
                    {
                        PhotoId = photo.Id,
                        TagId = tagId,
                        Source = PhotoTagSource.Rule,
                        CreatedAt = now,
                    });
                    tagsUpdated = true;
                }

                if (tagsUpdated)
                    photosUpdated++;
            }
```

with the paginated loop (per-photo matching body unchanged):

```csharp
            var addedPairs = new HashSet<(int PhotoId, int TagId)>();
            var newPhotoTags = new List<PhotoTag>();
            var now = DateTime.UtcNow;
            var photosUpdated = 0;

            var offset = 0;
            while (true)
            {
                var page = await _repository.GetPhotoRuleCandidatesPageAsync(PageSize, offset, cancellationToken);

                foreach (var photo in page)
                {
                    var allMatchingTagNames = TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules);
                    var matchingTagNames = scopeToTagName != null
                        ? (IReadOnlyList<string>)allMatchingTagNames.Where(n => n == scopeToTagName).ToList()
                        : allMatchingTagNames;

                    if (matchingTagNames.Count == 0)
                        continue;

                    var tagsUpdated = false;
                    foreach (var tagName in matchingTagNames)
                    {
                        if (!tagIdsByName.TryGetValue(tagName, out var tagId))
                            continue;

                        var pair = (photo.Id, tagId);
                        if (!addedPairs.Add(pair))
                            continue;

                        if (occupied.Contains(pair))
                            continue;

                        newPhotoTags.Add(new PhotoTag
                        {
                            PhotoId = photo.Id,
                            TagId = tagId,
                            Source = PhotoTagSource.Rule,
                            CreatedAt = now,
                        });
                        tagsUpdated = true;
                    }

                    if (tagsUpdated)
                        photosUpdated++;
                }

                offset += page.Count;
                if (page.Count < PageSize)
                    break;
            }
```

The full method body around this block (unchanged lines shown for placement context — `occupied` and `tagIdsByName` are fetched just above, `AddPhotoTagsAsync` + final `SaveChangesAsync` follow just below):

```csharp
            var occupied = await _repository.GetOccupiedTagPairsAsync(scopeToTagName, cancellationToken);
            var tagIdsByName = await _repository.GetOrCreateTagsAsync(ruleTagNames, cancellationToken);

            var addedPairs = new HashSet<(int PhotoId, int TagId)>();
            var newPhotoTags = new List<PhotoTag>();
            var now = DateTime.UtcNow;
            var photosUpdated = 0;

            var offset = 0;
            while (true)
            {
                var page = await _repository.GetPhotoRuleCandidatesPageAsync(PageSize, offset, cancellationToken);

                foreach (var photo in page)
                {
                    // ... matching body as above, unchanged ...
                }

                offset += page.Count;
                if (page.Count < PageSize)
                    break;
            }

            await _repository.AddPhotoTagsAsync(newPhotoTags, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();

            return new ReapplyRulesResponse { PhotosUpdated = photosUpdated };
```

Note the `var photos = await _repository.GetAllPhotosAsync(cancellationToken);` line is deleted entirely (its previous responsibility — providing `photos` for the `foreach` — is now fulfilled per-page inside the `while` loop).

#### Step 5: Run the handler tests and confirm they pass (green)

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesHandlerTests"
```

Expected: all 6 tests in `ReapplyRulesHandlerTests` pass.

#### Step 6: Run the behavior-preservation tests (real repository, no mocks) to confirm end-to-end equivalence

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReapplyRulesBehaviorPreservationTests"
```

Expected: all 5 tests in `ReapplyRulesBehaviorPreservationTests` (`ManualTagWins_RuleTagNotInsertedOverSharedPk`, `DuplicateMatch_AddsOneRow_PhotosUpdatedCountsPhotosNotTags`, `EmptyActiveRules_RemovesAllRuleTags_AndReturnsZero`, `ScopedReapply_OnlyTouchesTargetRuleTag`, `DoubleApply_NoNewTags_IsIdempotent_AndDoesNotThrow`) pass unchanged — this test file constructs a real `PhotobankRepository` against an EF Core InMemory `ApplicationDbContext` and calls `_handler.Handle(...)` directly, so it exercises the new paginated loop end-to-end without any test edits, proving output equivalence (NFR-2) against real repository behavior.

#### Step 7: Build the whole solution

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet build Anela.Heblo.sln
```

Expected: `Build succeeded.`

#### Step 8: Commit

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
git add backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs
git commit -m "Migrate ReapplyRulesHandler to paginated photo fetch"
```

---

### task: remove-dead-getallphotosasync

**Scope:** `GetAllPhotosAsync` now has zero production callers (confirmed: the only caller was `ReapplyRulesHandler`, migrated in the previous task). Delete it from the interface, the implementation, and its now-orphaned test, per FR-4.

#### Step 1: Confirm no remaining references

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
grep -rn "GetAllPhotosAsync" backend/src backend/test
```

Expected output: exactly three matches remaining at this point —
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` (the interface declaration)
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs` (the implementation)
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` (the `GetAllPhotosAsync_returnsAllPhotos` test)

If any other match appears (e.g. a new caller added by unrelated concurrent work), stop and investigate before proceeding — do not delete a method with a live caller.

#### Step 2: Remove the test for the deleted method

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs`, delete the `GetAllPhotosAsync_returnsAllPhotos` test method entirely:

```csharp
    [Fact]
    public async System.Threading.Tasks.Task GetAllPhotosAsync_returnsAllPhotos()
    {
        // Arrange
        _context.Photos.AddRange(
            new Photo { Id = 1, SharePointFileId = "sp-1", FileName = "a.jpg", FolderPath = "Products", ModifiedAt = DateTime.UtcNow },
            new Photo { Id = 2, SharePointFileId = "sp-2", FileName = "b.jpg", FolderPath = "Events", ModifiedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync(CancellationToken.None);

        // Act
        var photos = await _repository.GetAllPhotosAsync(CancellationToken.None);

        // Assert
        photos.Should().HaveCount(2);
        photos.Select(p => p.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }
```

(Equivalent coverage for the paginated replacement already exists from the first task: `GetPhotoRuleCandidatesPageAsync_firstPage_returnsProjectionOrderedById` and `GetPhotoRuleCandidatesPageAsync_secondPage_returnsRemainingRowsViaOffset` in the same file.)

#### Step 3: Remove the method from the interface

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`, delete this line from the `// Photos` region:

```csharp
        Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken);
```

#### Step 4: Remove the method from the repository implementation

In `/home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En/backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`, delete:

```csharp
    public async Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken)
    {
        return await _context.Photos.ToListAsync(cancellationToken);
    }

```

(the blank line immediately following it should also be removed so exactly one blank line separates `GetLocatorAsync` above from `GetPhotoBySharePointFileIdAsync` below, matching the file's existing spacing convention).

#### Step 5: Run the full Photobank test suite

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank"
```

Expected: all tests under the `Anela.Heblo.Tests.Features.Photobank` namespace pass — including `PhotobankRepositoryReapplyPrimitivesTests`, `ReapplyRulesHandlerTests`, and `ReapplyRulesBehaviorPreservationTests`.

#### Step 6: Build the whole solution and confirm no remaining references

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet build Anela.Heblo.sln
grep -rn "GetAllPhotosAsync" backend/src backend/test
```

Expected: `Build succeeded.` and the `grep` returns no matches (empty output), confirming FR-4's acceptance criterion — "`dotnet build` succeeds with no remaining references to the removed method."

#### Step 7: Run the full backend test suite

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet test Anela.Heblo.sln
```

Expected: all tests pass, confirming this surgical removal did not regress anything outside the Photobank module.

#### Step 8: Run `dotnet format` per project validation requirements

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
dotnet format Anela.Heblo.sln --verify-no-changes
```

If this reports formatting diffs, run `dotnet format Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-run the build and Photobank tests from Steps 5–6 to confirm nothing broke.

#### Step 9: Commit

```bash
cd /home/user/worktrees/feature-3691-Arch-Review-Photobank-Reapplyruleshandler-Loads-En
git add backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs \
        backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs
git commit -m "Remove unused GetAllPhotosAsync now that ReapplyRulesHandler is paginated"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (paginated, projected fetch replaces `GetAllPhotosAsync`; `AsNoTracking`; `Id`/`FolderPath`/`FileName` projection only; ordered by `Id`; matches `GetPhotosPendingAutoTagAsync`'s `(pageSize, offset, ct)` signature convention) → `add-photo-rule-candidates-page-method` (Steps 4–5) and `migrate-reapplyrules-handler-to-paginated-fetch` (Step 4).
- FR-1's behavioral-equivalence acceptance criterion and test-update requirement → `migrate-reapplyrules-handler-to-paginated-fetch` (Steps 2, 6) plus `remove-dead-getallphotosasync` (Step 2).
- FR-2 (fixed page size, named constant, not part of public signature) → `PageSize = 2000` constant added in `migrate-reapplyrules-handler-to-paginated-fetch` Step 4; the constant lives on the handler, not the repository method's signature.
- FR-3 (unchanged scoping, unchanged two-phase save shape, unchanged `RuleId`-not-found / no-active-rules behavior) → explicitly preserved by only replacing the fetch+loop block in `migrate-reapplyrules-handler-to-paginated-fetch` Step 4, leaving all surrounding code (rule loading, `RemoveRuleTagsAsync` + first `SaveChangesAsync`, `AddPhotoTagsAsync` + final `SaveChangesAsync`, cache invalidation) untouched; verified by the two short-circuit tests (`RuleNotFound_ReturnsError...`, `NoActiveRuleTagNames_...`) continuing to pass unmodified and by the full `ReapplyRulesBehaviorPreservationTests` suite passing unchanged.
- FR-4 (remove `GetAllPhotosAsync` from interface + implementation + test, `dotnet build` clean) → `remove-dead-getallphotosasync`, all steps.
- NFR-1 (memory bound scales with page size) → structural consequence of the `while` loop only ever holding one `List<PhotoAutoTagCandidate>` page at a time; verified indirectly by the page-boundary test in `add-photo-rule-candidates-page-method` Step 2.
- NFR-2 (behavioral equivalence, bit-for-bit identical output) → verified by `ReapplyRulesBehaviorPreservationTests` (real repository, no mocks) passing unchanged in `migrate-reapplyrules-handler-to-paginated-fetch` Step 6, and all pre-existing `ReapplyRulesHandlerTests` assertions on `PhotosUpdated` and added tag rows preserved verbatim (only the mock setup source changed, not the assertions).
- NFR-3 (no new public API surface) → confirmed no changes to `ReapplyRulesRequest`/`ReapplyRulesResponse`/controller anywhere in the plan.
- Data Model (reuse `PhotoAutoTagCandidate`, do not name it `PhotoLocator`) → `add-photo-rule-candidates-page-method` Step 5 reuses the existing record; `PhotoLocator` is never touched.
- API/Interface Design (exact method name and Skip/Take/Select shape) → matches Step 5's implementation exactly.

**Placeholder scan:** No "TBD"/"handle appropriately"/"similar to task N" phrasing anywhere in the plan; every code block is complete, copy-pasteable C#; every command is a literal `dotnet`/`git`/`grep` invocation with a stated expected result.

**Type consistency:** `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken)` returning `Task<List<PhotoAutoTagCandidate>>` is identical across the interface declaration (task 1, Step 4), the implementation (task 1, Step 5), the repository tests (task 1, Step 2), and the handler call site (task 2, Step 4). `PhotoAutoTagCandidate(int Id, string FolderPath, string FileName)` property names (`Id`, `FolderPath`, `FileName`) are used consistently in the repository query projection and in every test assertion and mock fixture. `PageSize` is a `private const int` on `ReapplyRulesHandler` only, never duplicated elsewhere.
