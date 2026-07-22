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

