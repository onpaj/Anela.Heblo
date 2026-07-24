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
