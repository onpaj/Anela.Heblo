# Specification: Paginate photo loading in ReapplyRulesHandler

## Summary
`ReapplyRulesHandler.Handle` currently loads every row of the `Photos` table into memory via `IPhotobankRepository.GetAllPhotosAsync` to evaluate tag rules against each photo. This is unbounded and risks GC pressure / OOM as the photo catalog grows. This spec replaces the single full-table load with paginated, projection-based fetching so the handler's peak memory usage stays bounded regardless of table size.

## Background
The `/api/photobank/settings/rules/reapply` endpoint (admin-only, infrequent) recomputes rule-derived tags for photos, either across the whole catalog or scoped to a single rule via `RuleId`. The current implementation:
1. Loads all `TagRule`s and (optionally) all pre-existing rule tags — both small, bounded sets.
2. Loads **every** `Photo` entity in full (`GetAllPhotosAsync` → `_context.Photos.ToListAsync()`), including columns not needed for rule matching (`SharePointWebUrl`, `FileSizeBytes`, `DriveId`, `MimeType`, navigation collections, etc.).
3. Iterates in-memory, matching each photo's `FolderPath`/`FileName` against active rules via `TagRuleMatcher.GetMatchingTags`, and accumulates `PhotoTag` rows to insert.

At 50k–200k+ photos this becomes tens to hundreds of MB of unnecessary object allocation on every reapply, all synchronous in the request thread. The fix is to fetch only the fields needed (`Id`, `FolderPath`, `FileName`) in bounded pages, matching the pattern already used by `GetPhotosPendingAutoTagAsync` for the auto-tagging pipeline.

## Functional Requirements

### FR-1: Paginated, projected photo fetch for rule matching
Replace the call to `GetAllPhotosAsync` in `ReapplyRulesHandler.Handle` with a loop over pages fetched from a new repository method that returns a lightweight projection (`Id`, `FolderPath`, `FileName` only — the fields `TagRuleMatcher.GetMatchingTags` and tag-pair construction actually need), not full `Photo` entities.

The new repository method must:
- Query with `AsNoTracking()` (read-only, no entity tracking needed).
- Select only `Id`, `FolderPath`, `FileName` (no `Include`s, no full entity materialization).
- Order by a stable key (`Id`) so paging via `Skip`/`Take` is deterministic across pages.
- Follow the existing pagination signature convention used by `GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken)` in `IPhotobankRepository` for consistency, rather than introducing a different parameter order.

**Acceptance criteria:**
- `ReapplyRulesHandler.Handle` no longer calls `GetAllPhotosAsync`.
- The handler processes photos page-by-page (fixed page size, see FR-2) until a page returns fewer rows than the page size (end of data), without ever materializing more than one page of `Photo` rows in memory at a time.
- Rule matching and tag-pair accumulation (`addedPairs`, `newPhotoTags`, `photosUpdated`) produce results identical to the current full-load implementation for the same input data (same photos tagged with the same tags, same `PhotosUpdated` count) — existing behavior is preserved, only the fetch strategy changes.
- The new repository method uses `AsNoTracking()` and selects only `Id`, `FolderPath`, `FileName`.
- Unit tests in `PhotobankRepositoryReapplyPrimitivesTests.cs` and `ReapplyRulesHandlerTests.cs` that currently mock/exercise `GetAllPhotosAsync` are updated to use the new paginated method and continue to pass.

### FR-2: Bounded, configurable page size
Introduce a fixed page size constant for the paginated fetch (e.g. 2,000 photos per page — large enough to keep round-trip count low for typical catalogs, small enough to bound peak memory to a low single-digit MB per page). The exact value is an implementation detail, not a public contract; see Open Questions for the concrete number.

**Acceptance criteria:**
- Page size is defined once (a named constant, not a magic number scattered across the method).
- Changing the page size does not require changing the repository method's public signature.

### FR-3: Preserve `RuleId`-scoped behavior and existing transaction shape
The existing scoping logic (`scopeToTagName`, filtering `matchingTagNames` to the scoped tag) and the existing two-phase persistence (delete existing rule tags + `SaveChangesAsync`, then accumulate and insert new tags + a single final `SaveChangesAsync`) must be unchanged. Only the source of photo data changes from one bulk load to a paginated loop.

**Acceptance criteria:**
- `RemoveRuleTagsAsync` + first `SaveChangesAsync` still happen before the photo-matching loop, unconditionally, as today.
- `newPhotoTags` still accumulates across all pages in memory (this list is bounded by the number of *matches*, not the number of photos, and is already far smaller than the full photo set — no change needed here per the brief).
- A single `AddPhotoTagsAsync` + `SaveChangesAsync` call still happens after all pages have been processed (no per-page `SaveChangesAsync`, to avoid changing transactional behavior or introducing partial-apply states).
- Behavior for `RuleId`-scoped requests (rule not found → `PhotobankRuleNotFound`; no matching active rules → early return with `PhotosUpdated = 0`) is unchanged.

### FR-4: Remove or repurpose `GetAllPhotosAsync`
`GetAllPhotosAsync` on `IPhotobankRepository` currently has exactly one production caller (`ReapplyRulesHandler`). Once that caller is migrated to the paginated method, remove `GetAllPhotosAsync` from the interface and its implementation, unless another in-flight/near-term use depends on it (search the codebase before removing).

**Acceptance criteria:**
- `GetAllPhotosAsync` is deleted from `IPhotobankRepository` and `PhotobankRepository`, and from `PhotobankRepositoryReapplyPrimitivesTests.cs`'s `GetAllPhotosAsync_returnsAllPhotos` test (replaced with an equivalent test for the new paginated method), **or** a one-line note is added explaining why it was kept.
- `dotnet build` succeeds with no remaining references to the removed method.

## Non-Functional Requirements

### NFR-1: Performance / memory bound
Peak additional memory attributable to photo fetching during a reapply-rules request must scale with page size, not with total photo count. For a catalog of 200k photos, memory used for photo data at any point in time should be roughly equivalent to one page's worth of `(int, string, string)` projections (low single-digit MB), not the full 200k-row entity set.

### NFR-2: Behavioral equivalence
The change is a data-access optimization only. Output (`PhotosUpdated` count, which photos receive which tags, error codes for invalid `RuleId`) must be bit-for-bit identical to the pre-change implementation for the same database state. No new business rules are introduced.

### NFR-3: No new public API surface
This is an internal implementation change. The `ReapplyRulesRequest`/`ReapplyRulesResponse` contract and the `/api/photobank/settings/rules/reapply` endpoint signature are unaffected.

## Data Model
No schema changes. A new lightweight projection type is needed to carry `(Id, FolderPath, FileName)` out of the repository without pulling full `Photo` entities. Two options, either acceptable:
- Reuse the existing `PhotoAutoTagCandidate` record (`Domain/Features/Photobank/PhotoAutoTagCandidate.cs`), which already has exactly this shape (`Id`, `FolderPath`, `FileName`) and is used by the analogous `GetPhotosPendingAutoTagAsync` pagination method.
- Introduce a new record with a distinct name.

**Do not name the new type `PhotoLocator`** — that name is already taken by an unrelated record in `IPhotobankRepository.cs` (`PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt)`, used by `GetLocatorAsync` for SharePoint sync), and reusing it for a differently-shaped type would be confusing and would not compile as a redefinition.

## API / Interface Design
`IPhotobankRepository` changes:
- Remove: `Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken)`
- Add (name illustrative): `Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken)` — implemented as:
  ```csharp
  await _context.Photos
      .AsNoTracking()
      .OrderBy(p => p.Id)
      .Skip(offset)
      .Take(pageSize)
      .Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))
      .ToListAsync(cancellationToken);
  ```

`ReapplyRulesHandler.Handle` changes: replace the single `GetAllPhotosAsync` call and `foreach (var photo in photos)` loop with an outer `while` loop that fetches successive pages and runs the existing per-photo matching logic (lines 72–107 today) unchanged against each page's items, stopping when a page returns fewer than `pageSize` rows.

No changes to the MediatR request/response types, the controller, or any frontend/OpenAPI contract.

## Dependencies
- Entity Framework Core (`Microsoft.EntityFrameworkCore`) — already in use; `AsNoTracking`, `Skip`/`Take`, projection via `Select` are all standard EF Core features already used elsewhere in `PhotobankRepository` (e.g. `GetPhotosPendingAutoTagAsync`).
- Existing tests that mock/exercise `GetAllPhotosAsync`: `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` and `PhotobankRepositoryReapplyPrimitivesTests.cs` — must be updated as part of this change.

## Out of Scope
- Changing the two-phase `SaveChangesAsync` transaction shape (e.g. per-page commits, background job execution, or streaming responses). The brief explicitly scopes the fix to bounding the *read* side; the accumulate-then-save-once write pattern is preserved.
- Paginating or optimizing other full-table-load call sites outside `ReapplyRulesHandler` (there are none currently calling `GetAllPhotosAsync`, but other repository methods such as `GetRulesAsync`, `GetOccupiedTagPairsAsync`, `RemoveRuleTagsAsync` are out of scope — they operate on bounded rule/tag sets, not the full photo table).
- Moving rule reapplication to a background/queued job. It remains a synchronous request handler; this change only bounds its memory footprint.
- Changing `TagRuleMatcher.GetMatchingTags` or rule-matching semantics.
- Adding a `RuleId`-scoped SQL-side filter to avoid paging entirely for the scoped case (the brief notes the scoped path already touches a small rule count and calls the added risk low; a filtered fast path can be considered later but is not required here).

## Open Questions
None. Reasonable assumptions made and documented above:
- Page size assumed at 2,000 rows per page; exact value is an implementation detail and can be tuned without spec changes since it isn't part of the public contract (FR-2).
- New projection type recommended to reuse `PhotoAutoTagCandidate` rather than introduce a new record, since the shape is identical; either choice satisfies the requirements (Data Model section).
- New repository method name is illustrative (`GetPhotoRuleCandidatesPageAsync`); any name avoiding collision with the existing `PhotoLocator` record is acceptable.

## Status: COMPLETE
