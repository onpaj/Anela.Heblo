# Architecture Review: Paginate photo loading in ReapplyRulesHandler

## Skip Design: true

## Architectural Fit Assessment

This is a data-access optimization confined entirely to the Photobank module's persistence boundary — no contract, controller, or frontend surface changes. It aligns tightly with an existing, proven pattern already in the same module: `IPhotobankRepository.GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken)`, implemented in `PhotobankRepository.cs` as an `AsNoTracking().OrderBy(p => p.Id).Skip(offset).Take(pageSize).Select(...)` projection into `PhotoAutoTagCandidate`, and consumed by `PhotobankAutoTagJob.ExecuteAsync` via a `while` loop that pages until a page comes back empty.

`ReapplyRulesHandler.Handle` should adopt the same shape: same parameter order, same projection type, same repository placement. The only material deviation from the auto-tag job's loop is the termination condition — recommended below — chosen deliberately to reduce round-trips and simplify test mocking, not because the auto-tag job's convention is wrong.

Module boundaries are respected: `IPhotobankRepository` lives in `Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` (not `Application`, contrary to the finding's file reference — the interface and the `PhotoLocator`/`PhotoAutoTagCandidate` record shapes it depends on are Domain types), `PhotobankRepository` in `Anela.Heblo.Persistence/Photobank/`, and the handler in `Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/`. No cross-module coupling is introduced or touched.

## Proposed Architecture

### Component Overview

```
ReapplyRulesHandler.Handle
   │
   ├─ GetRulesAsync()                     (unchanged, bounded)
   ├─ RemoveRuleTagsAsync + SaveChanges   (unchanged, unconditional, first)
   ├─ GetOccupiedTagPairsAsync            (unchanged, bounded)
   ├─ GetOrCreateTagsAsync                (unchanged, bounded)
   │
   ├─ [NEW] while (true):
   │      page = GetPhotoRuleCandidatesPageAsync(PageSize, offset, ct)
   │      foreach photo in page: match rules → accumulate newPhotoTags   (existing per-photo logic, unmoved)
   │      offset += page.Count
   │      if page.Count < PageSize: break
   │
   └─ AddPhotoTagsAsync(newPhotoTags) + SaveChanges   (unchanged, single call, after loop)

IPhotobankRepository (Domain)
   └─ GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken)
         → PhotobankRepository (Persistence): AsNoTracking, OrderBy(Id), Skip/Take, Select → PhotoAutoTagCandidate

[REMOVED] IPhotobankRepository.GetAllPhotosAsync / PhotobankRepository.GetAllPhotosAsync
```

The per-photo matching body (rule matching via `TagRuleMatcher.GetMatchingTags`, `addedPairs`/`newPhotoTags` accumulation, `photosUpdated` counting) is untouched logic — only its data source changes from a single in-memory `List<Photo>` to successive `List<PhotoAutoTagCandidate>` pages. `photo.Id`/`photo.FolderPath`/`photo.FileName` field access is unaffected since `PhotoAutoTagCandidate` carries exactly these three fields under the same names.

### Key Design Decisions

#### Decision 1: Reuse `PhotoAutoTagCandidate` as the page projection type

**Options considered:**
- Reuse `PhotoAutoTagCandidate(int Id, string FolderPath, string FileName)` (Domain/Features/Photobank).
- Introduce a new, differently-named record with the identical shape.

**Chosen approach:** Reuse `PhotoAutoTagCandidate`.

**Rationale:** The shape is identical to what `ReapplyRulesHandler` needs, it's already a Domain-layer type used by an analogous pagination method (`GetPhotosPendingAutoTagAsync`), and it is an internal projection type never serialized over the wire — introducing a second record with the same three fields would be pure duplication with no behavioral or architectural benefit. This is an internal domain type, so the project's "DTOs are classes, not records" rule (which targets `contracts/` API DTOs to protect OpenAPI codegen) does not apply — `PhotoAutoTagCandidate` is already a record and stays one.

#### Decision 2: New repository method name and parameter order

**Options considered:**
- `GetPhotoLocatorsPageAsync(int offset, int pageSize, ...)` as suggested in the original finding.
- `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, ...)` matching `GetPhotosPendingAutoTagAsync`'s existing signature convention.

**Chosen approach:** `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken)`, added to `IPhotobankRepository` directly below `GetPhotosPendingAutoTagAsync` in the "Auto-tagging"-adjacent region (or its own "// Rule reapply" comment block, consistent with the file's existing `// Photos` / `// Tags` / `// Auto-tagging` section comments).

**Rationale:** `PhotoLocator` is already a distinct, unrelated record (`(DriveId, SharePointFileId, ModifiedAt)`) used by `GetLocatorAsync` for SharePoint sync — reusing that name for a differently-shaped return type would not compile and would be confusing even if it did. Matching `GetPhotosPendingAutoTagAsync`'s `(pageSize, offset, ct)` order (rather than the finding's `(offset, pageSize, ct)`) keeps the one pagination convention in this repository consistent — a developer skimming the interface should not have to check parameter order per method.

#### Decision 3: Loop termination — break when a page is short, not when it's empty

**Options considered:**
- Mirror `PhotobankAutoTagJob.ExecuteAsync` exactly: loop while `processedCount < max`, break when `batch.Count == 0` (i.e., always issue one trailing query that returns zero rows to detect the end).
- Break as soon as a page returns fewer rows than the requested `pageSize` (no trailing empty-page query).

**Chosen approach:** Break on short page (`page.Count < PageSize`), as FR-1's acceptance criteria and the spec's API/Interface Design section both specify.

**Rationale:** Two independent reasons converge on the same choice. First, it saves one DB round-trip per invocation (real, if small, savings on an admin endpoint that already does several sequential queries). Second — and more importantly for reviewability — it keeps every existing `ReapplyRulesHandlerTests.cs` test (which currently does a single `_repo.Setup(...GetAllPhotosAsync...).ReturnsAsync(new List<Photo> { ... })` with 1–3 photos) working with a **single** mock setup of the new method, since any test fixture smaller than `PageSize` (2,000) naturally terminates the loop on its first call. The `Count == 0` convention, by contrast, would force every existing test to switch to `SetupSequence` (returning the fixture, then an empty list) purely to satisfy the loop's exit condition — a mechanical test-only change with no behavioral value, exactly the kind of pattern already visible in `PhotobankAutoTagJobTests.cs`'s `SetupSequence` usage, which this change does not need to import.

#### Decision 4: Page size as a private constant, not a configurable option

**Options considered:**
- Follow `AutoTagOptions`/`IOptions<T>` pattern (config-driven, like `PhotobankAutoTagJob.BatchSize`).
- A single `private const int` on the handler.

**Chosen approach:** `private const int PageSize = 2000;` on `ReapplyRulesHandler`.

**Rationale:** `AutoTagOptions.BatchSize` is operator-tunable because it trades off LLM API cost against latency — a genuine ops knob. Here the page size only bounds memory for a synchronous, admin-only, infrequent endpoint; there is no cost/latency tradeoff an operator would ever need to tune at runtime, and NFR-3/FR-2 explicitly say this is not part of the public contract. A named constant satisfies FR-2's "not a magic number" requirement with far less machinery (no `PhotobankModule.cs` registration, no options class, no config section) than an `IOptions<T>`. If real-world tuning need ever emerges, promoting it to config later is a small, isolated change.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. All changes are edits to existing files:

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — remove `GetAllPhotosAsync`, add `GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken)`.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs` — remove the `GetAllPhotosAsync` method (currently lines ~147–150), add `GetPhotoRuleCandidatesPageAsync` implementing the `AsNoTracking().OrderBy(p => p.Id).Skip(offset).Take(pageSize).Select(p => new PhotoAutoTagCandidate(p.Id, p.FolderPath, p.FileName))` query. Place it near `GetPhotosPendingAutoTagAsync` (same file, "Auto-tagging" region) or immediately after the other `// Photos` methods — either is acceptable; do not scatter it elsewhere.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — replace the `GetAllPhotosAsync` call + `foreach (var photo in photos)` with the paginated `while` loop described above. Add `private const int PageSize = 2000;` at class level.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankRepositoryReapplyPrimitivesTests.cs` — replace `GetAllPhotosAsync_returnsAllPhotos` (lines 31–46) with an equivalent test against `GetPhotoRuleCandidatesPageAsync`, and add at least one test asserting page-boundary behavior (e.g. 3 photos, `pageSize: 2` → first call returns 2 ordered by `Id`, second call with `offset: 2` returns 1).
- `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` — every `_repo.Setup(r => r.GetAllPhotosAsync(...))` (constructor default at line 33–34, and the per-test overrides at lines 86, 117, 146, 170) becomes `_repo.Setup(r => r.GetPhotoRuleCandidatesPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))`, returning `List<PhotoAutoTagCandidate>` built from the existing `Photo` fixtures' `(Id, FolderPath, FileName)` (the `PhotoAt` helper at line 41 should gain a sibling `CandidateAt` or be adapted to produce `PhotoAutoTagCandidate` directly). Per Decision 3, no `SetupSequence` is needed for these small fixtures.

### Interfaces and Contracts

```csharp
// IPhotobankRepository.cs (Domain) — Photos region
Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(
    int pageSize, int offset, CancellationToken cancellationToken);
```

```csharp
// PhotobankRepository.cs (Persistence)
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

```csharp
// ReapplyRulesHandler.cs
private const int PageSize = 2000;

// ... replacing `var photos = await _repository.GetAllPhotosAsync(cancellationToken);`
//     and the `foreach (var photo in photos)` block:

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

No change to `ReapplyRulesRequest` / `ReapplyRulesResponse` (already classes, per DTO convention — unaffected by this change), no controller change, no OpenAPI regeneration needed.

### Data Flow

1. Admin calls `POST /api/photobank/settings/rules/reapply` (unchanged).
2. Handler loads rules and resolves scope (unchanged, bounded reads).
3. Handler commits rule-tag removal (unchanged, first, unconditional).
4. Handler loads occupied pairs and resolves/creates tag IDs (unchanged, bounded reads).
5. **Changed:** Handler pages through `Photos` in `PageSize`-row chunks ordered by `Id`, running the existing per-photo `TagRuleMatcher` matching against each page in turn, accumulating `newPhotoTags` in memory across pages (this list is bounded by *matches*, not total photos — unchanged from today).
6. Handler inserts all accumulated `newPhotoTags` in one `AddPhotoTagsAsync` + one final `SaveChangesAsync` (unchanged), then invalidates the tag cache (unchanged).

Peak photo-data memory at any instant is now one page's worth of `(int, string, string)` tuples (≈low single-digit MB at `PageSize = 2000`) instead of the full `Photo` entity set.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Offset-based (`Skip`/`Take`) pagination can skip or duplicate rows if photos are inserted/deleted between page fetches (e.g. a concurrent SharePoint sync job writing new `Photo` rows mid-reapply) | Low | Endpoint is admin-only and infrequent; a missed row is corrected by the *next* reapply run or by the (separately existing) index/auto-tag pipelines, not silently lost forever. Accept as-is per spec's explicit scope (no SQL-side filtering or keyset pagination required); do not block on this — note it as a known limitation in a code comment near the loop if convenient, but it is not worth the added complexity of keyset (`WHERE Id > lastId`) pagination for this low-frequency admin path. |
| Test regressions: 5+ call sites across two test files reference the removed `GetAllPhotosAsync` | Medium (build-breaking, not logic risk) | Update both test files as part of the same change (listed above); `dotnet build` will fail loudly if any reference is missed, so this cannot silently regress. |
| `newPhotoTags` still accumulates unbounded across all pages in memory | Low | Explicitly out of scope per the spec (FR-3) and brief — bounded by match count, not photo count, and already far smaller than the photo table in all realistic scenarios. No change needed; flagged only for completeness. |
| A future developer re-adds a full-table `Photos.ToListAsync()` elsewhere in Photobank without noticing this fix | Low | No structural guard exists (e.g. no analyzer rule); rely on this review and the arch-review routine that originally caught this instance to catch recurrences. Not worth adding tooling for a single call site. |

## Specification Amendments

1. **File path correction:** The brief's finding cites `IPhotobankRepository` as if under `Anela.Heblo.Application`; it actually lives in `Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`. Implementers should edit the Domain-project file, not search under Application.
2. **Loop termination clarified as a requirement, not just an option:** FR-1 already specifies "until a page returns fewer rows than page size" — this review treats that as load-bearing (Decision 3) rather than a stylistic choice, specifically because it lets existing handler tests avoid `SetupSequence`. Implementers should not substitute the `PhotobankAutoTagJob`-style "loop until empty" pattern even though it exists elsewhere in the same module.
3. **Method placement:** recommend adding `GetPhotoRuleCandidatesPageAsync` adjacent to `GetPhotosPendingAutoTagAsync` in `PhotobankRepository.cs` (both are page-projection queries over `Photos`), rather than strictly under the `// Photos` region comment where `GetAllPhotosAsync` currently sits — this is a minor organizational call left to the implementer; either location is acceptable and the spec is silent on it.

No functional amendments — the spec's FR-1 through FR-4 and NFR-1 through NFR-3 are implementable as written and are what this review's guidance follows.

## Prerequisites

None. No schema migration, no new configuration, no infrastructure changes. The change is implementable immediately against the current `main`/branch state: `Photo.Id` is already the table's primary/ordering key, `AsNoTracking`/`Skip`/`Take`/projection `Select` are already in use elsewhere in `PhotobankRepository.cs`, and the EF Core InMemory provider used by existing repository tests already supports `OrderBy`+`Skip`+`Take` (proven by `GetPhotosAsync`'s existing paginated tests in the same test class family).
