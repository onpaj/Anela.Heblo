# Design: Paginate photo loading in ReapplyRulesHandler

## Component Design

### `IPhotobankRepository` (Domain — `Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`)
- **Remove:** `Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken)`.
- **Add:** `Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken)`.
  - Placed adjacent to `GetPhotosPendingAutoTagAsync` (or in a `// Rule reapply` comment block), matching that method's `(pageSize, offset, ct)` parameter order for interface consistency.
  - Responsibility: return one page of lightweight photo projections, ordered deterministically by `Id`, for rule-matching consumers — no tracking, no navigation properties.

### `PhotobankRepository` (Persistence — `Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`)
- Implements `GetPhotoRuleCandidatesPageAsync`:
  ```csharp
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
  ```
- Removes the `GetAllPhotosAsync` method entirely (single production caller, migrated below).

### `ReapplyRulesHandler` (Application — `Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`)
- Adds `private const int PageSize = 2000;`.
- Replaces the single `GetAllPhotosAsync` call + `foreach (var photo in photos)` with a paginated loop:
  ```csharp
  var offset = 0;
  while (true)
  {
      var page = await _repository.GetPhotoRuleCandidatesPageAsync(PageSize, offset, cancellationToken);

      foreach (var photo in page)
      {
          // existing per-photo matching body, unchanged — photo.Id / photo.FolderPath / photo.FileName
      }

      offset += page.Count;
      if (page.Count < PageSize)
          break;
  }
  ```
- Everything else in `Handle` is unchanged:
  - `GetRulesAsync`, scope resolution (`RuleId` → `PhotobankRuleNotFound` if missing; early return with `PhotosUpdated = 0` if no active rules match).
  - `RemoveRuleTagsAsync` + first `SaveChangesAsync`, unconditional, before the loop.
  - `GetOccupiedTagPairsAsync` / `GetOrCreateTagsAsync`, bounded reads, before the loop.
  - `newPhotoTags` accumulates in memory across all pages (bounded by match count, not photo count).
  - Single `AddPhotoTagsAsync(newPhotoTags)` + final `SaveChangesAsync` after the loop completes, then cache invalidation.
- No change to constructor dependencies, request/response types, or the controller.

### Projection type: `PhotoAutoTagCandidate` (Domain — `Domain/Features/Photobank/PhotoAutoTagCandidate.cs`)
- Reused as-is (`Id`, `FolderPath`, `FileName`) — no new type introduced. Already used by `GetPhotosPendingAutoTagAsync`; internal projection, never serialized over the wire, so the "DTOs must be classes" rule does not apply.
- Do **not** reuse or rename `PhotoLocator` — that is a distinct, unrelated record `(DriveId, SharePointFileId, ModifiedAt)` used by `GetLocatorAsync` for SharePoint sync.

## Data Schemas

No database schema changes.

### Repository method signature
```csharp
Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(
    int pageSize, int offset, CancellationToken cancellationToken);
```

### Projection shape (unchanged, reused)
```csharp
record PhotoAutoTagCandidate(int Id, string FolderPath, string FileName);
```

### Query shape
- Source: `Photos` table.
- Filter: none (full catalog, paged) — scoping to a `RuleId` remains an in-memory concern via `matchingTagNames`, unaffected by this change.
- Ordering: `Id` ascending (stable key for deterministic `Skip`/`Take` paging).
- Tracking: `AsNoTracking()`.
- Projected columns only: `Id`, `FolderPath`, `FileName` — no `SharePointWebUrl`, `FileSizeBytes`, `DriveId`, `MimeType`, or navigation collections.

### API / MediatR contract
No changes. `ReapplyRulesRequest` / `ReapplyRulesResponse` and the `POST /api/photobank/settings/rules/reapply` endpoint signature are unaffected — this is an internal data-access change only.

### Test data shapes (for updated tests)
- `PhotobankRepositoryReapplyPrimitivesTests.cs`: replace `GetAllPhotosAsync_returnsAllPhotos` with a test against `GetPhotoRuleCandidatesPageAsync`, asserting page-boundary behavior (e.g. 3 photos, `pageSize: 2` → page 1 returns 2 rows ordered by `Id`; `offset: 2` → page 2 returns the remaining 1 row).
- `ReapplyRulesHandlerTests.cs`: mock setups on `GetAllPhotosAsync` become `_repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))` returning `List<PhotoAutoTagCandidate>` built from existing `Photo` fixtures' `(Id, FolderPath, FileName)`. Since existing fixtures are far smaller than `PageSize` (2000), a single mock setup per test suffices — no `SetupSequence` needed.
