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

