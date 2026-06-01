I have enough context. Writing the architecture review now.

# Architecture Review: Refactor PhotobankIndexJob to use IPhotobankRepository

## Skip Design: true

This is a pure backend refactor — no UI, schema, API surface, or operational config changes. No design artifacts are required.

## Architectural Fit Assessment

The refactor restores compliance with the project's stated module boundary: the Application layer must depend only on Domain abstractions, not on `Anela.Heblo.Persistence`. The pattern is already proven on the sibling `PhotobankAutoTagJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs:15`), which uses `IPhotobankRepository` exclusively. After the change, both jobs in the directory will follow the same convention.

Integration points are minimal and entirely internal:
- `IPhotobankRepository` (Domain) gains seven new methods — no breaking changes to existing callers.
- `PhotobankRepository` (Application) implements them against the same `ApplicationDbContext` it already wraps.
- `PhotobankIndexJob` swaps `ApplicationDbContext` for `IPhotobankRepository` at the constructor.
- DI requires **no change** — `PhotobankIndexJob` is already auto-registered as both `IRecurringJob` and itself via the assembly scan in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:357-376` (`AddRecurringJobs`). The explicit `services.AddScoped<PhotobankAutoTagJob>()` at `PhotobankModule.cs:32` is redundant with the scan; do not mirror it.

## Proposed Architecture

### Component Overview

```
+---------------------------+      +-------------------------------+
|  PhotobankIndexJob        |----->|  IPhotobankRepository         |
|  (Application/.../Jobs)   |      |  (Domain/Features/Photobank)  |
+---------------------------+      +-------------------------------+
                                                  |
                                                  v
                                   +-------------------------------+
                                   |  PhotobankRepository          |
                                   |  (Application/Features/       |
                                   |   Photobank)                  |
                                   +-------------------------------+
                                                  |
                                                  v
                                   +-------------------------------+
                                   |  ApplicationDbContext         |
                                   |  (Persistence)                |
                                   +-------------------------------+
```

After the refactor, `PhotobankIndexJob` has no compile-time reference to `Anela.Heblo.Persistence` or `Microsoft.EntityFrameworkCore`, identical to `PhotobankAutoTagJob`.

### Key Design Decisions

#### Decision 1: New Add/Remove methods stage-only (no internal SaveChangesAsync)

**Options considered:**
- (a) New methods call `SaveChangesAsync` internally (encapsulate transaction inside repository).
- (b) New methods only stage entities; the caller invokes `SaveChangesAsync` explicitly.

**Chosen approach:** (b) — stage-only. Matches the convention already established by `AddPhotoTagAsync`, `AddPhotoTagsAsync`, `RemovePhotoTagAsync`, `AddRootAsync`, `AddRuleAsync`, `RemoveRuleTagsAsync`, etc. (see `PhotobankRepository.cs:229-263`).

**Rationale:** The job has explicit transactional boundaries — it batches multiple writes between `SaveChangesAsync` calls (e.g. `UpsertPhotoAsync` flushes once after the photo upsert, then stages rule-tag removals and additions, then flushes again at line 168). Pushing `SaveChangesAsync` into individual repository methods would break this batching and change the number/position of flushes — violating FR-5. The spec already specifies this in NFR-1/FR-5; this review makes it explicit as a design rule.

#### Decision 2: `GetActiveRootsWithDriveAsync` returns tracked entities

**Options considered:**
- (a) `AsNoTracking()` + a separate `UpdateRootAsync(PhotobankIndexRoot)` for the `RootItemId`/`DeltaLink`/`LastIndexedAt` writes.
- (b) Return tracked entities so the job's in-place mutations (`root.RootItemId = ...`, `root.DeltaLink = ...`, `root.LastIndexedAt = ...`) are flushed by the next `SaveChangesAsync`.

**Chosen approach:** (b). This matches `GetRootsAsync` in `PhotobankRepository.cs:281-286`, preserves the exact mutation pattern at `PhotobankIndexJob.cs:71-72,102-104`, and avoids introducing a new method that exists only because of a tracking choice.

**Rationale:** Tracking is EF Core's default behavior; the only deviation in this repository is `GetTagsWithCountsAsync` which uses `AsNoTracking()` because it returns a projection. The job code explicitly relies on tracking. Hidden non-tracked reads would silently lose writes — a footgun.

#### Decision 3: Use existing `GetOrCreateTagAsync` instead of replicating lookup-then-create

**Options considered:**
- (a) Add a new `CreateTagAsync(Tag)` method and keep the job's lookup-then-create flow.
- (b) Reuse the existing `GetOrCreateTagAsync(normalizedName)` (which already saves internally — `PhotobankRepository.cs:173-184`) and delete the job's private `CreateTagAsync` helper.

**Chosen approach:** (b). Consolidates the duplicated lookup-or-create pattern. The current job's `CreateTagAsync` (lines 171–177) calls `SaveChangesAsync` after the insert — identical semantics to `GetOrCreateTagAsync`. Behavior is preserved.

**Rationale:** Removes duplicated logic. The race-condition characteristics are identical to today's job (out of scope per spec). Note: `GetOrCreateTagAsync` adds one `SaveChangesAsync` call **only when a tag is newly created** — exactly what the current `CreateTagAsync` does today. No new flush is introduced.

#### Decision 4: Test migration uses `Mock<IPhotobankRepository>` with `Verify`

**Options considered:**
- (a) Keep `InMemoryDatabase` and inject a thin `PhotobankRepository` wired to it.
- (b) Pure `Mock<IPhotobankRepository>` with `It.Is<>` argument matchers / `Verify` / captures.
- (c) A hand-rolled `InMemoryPhotobankRepository` fake.

**Chosen approach:** (b), matching the spec, with (c) as a fallback if a specific assertion would be unreadable as pure Verify (e.g. asserting a sequence of state-dependent reads).

**Rationale:** The whole point of the refactor is that the job is now testable without persistence wiring. Option (a) would re-introduce the EF Core in-memory dependency the spec wants to remove from this file. Option (b) is the standard approach used elsewhere in this test project.

## Implementation Guidance

### Directory / Module Structure

No new files. Edit only:

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — add seven method signatures.
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — implement them. **Note: the spec references this file at `Infrastructure/Persistence/PhotobankRepository.cs`, but the actual location is the flat `Features/Photobank/` directory.** Group the new methods under existing comment-banner sections (`// Photos`, `// Photo tags`, `// Roots`, `// Rules`) to keep the file consistent.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — swap field/ctor, replace `_db.*` calls, drop the private `CreateTagAsync` helper, remove `using Anela.Heblo.Persistence;` and `using Microsoft.EntityFrameworkCore;`.
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — rewrite against `Mock<IPhotobankRepository>`. Drop `IDisposable`, the in-memory DbContext, and EF/Persistence usings.

No change to `PhotobankModule.cs`.

### Interfaces and Contracts

New methods on `IPhotobankRepository` (group under existing sections):

```csharp
// Roots
Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken);

// Rules
Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken);

// Photos
Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken);
Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken);
Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken);

// Photo tags
Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
```

Implementation rules:
- `Add*` / `Remove*` methods return `Task.CompletedTask` after a synchronous `Add` / `Remove` / `RemoveRange` on the DbSet — match `AddPhotoTagAsync` at `PhotobankRepository.cs:229-233` and `RemoveRuleTagsAsync` at `PhotobankRepository.cs:255-263`.
- `GetActiveRootsWithDriveAsync` queries `_context.PhotobankIndexRoots.Where(r => r.IsActive && r.DriveId != null).ToListAsync(ct)`. **Do not** use `AsNoTracking()`.
- `GetActiveTagRulesAsync` queries `_context.PhotobankTagRules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ToListAsync(ct)`.
- `GetPhotoBySharePointFileIdAsync` uses `FirstOrDefaultAsync(p => p.SharePointFileId == sharePointFileId, ct)`. Tracked (no `AsNoTracking`) — the caller mutates the returned entity.
- `GetPhotoTagsByPhotoAndSourceAsync` filters by `pt.PhotoId == photoId && pt.Source == source` and returns `ToListAsync`. Tracked, since `RemovePhotoTagsAsync` will be called with the result.

No changes to: `IPhotobankRepository.SaveChangesAsync`, `GetOrCreateTagAsync`, `GetTagByNameAsync`, `AddPhotoTagAsync` — all reused as-is.

### Data Flow

For a single delta item in `IndexRootAsync`:

```
ExecuteAsync
  ├─ _repo.GetActiveRootsWithDriveAsync          (was: _db.PhotobankIndexRoots.Where...ToListAsync)
  └─ foreach root:
       IndexRootAsync
         ├─ [if RootItemId null] root.RootItemId = ...; _repo.SaveChangesAsync   (flush #1)
         ├─ _repo.GetActiveTagRulesAsync                                          (was: _db.PhotobankTagRules)
         └─ foreach delta item:
              if IsDeleted:
                ├─ existing = _repo.GetPhotoBySharePointFileIdAsync
                └─ [if existing] _repo.RemovePhotoAsync(existing)
              else:
                UpsertPhotoAsync
                  ├─ photo = _repo.GetPhotoBySharePointFileIdAsync
                  ├─ [if null] photo = new Photo(...); _repo.AddPhotoAsync(photo)
                  ├─ mutate scalar fields (incl. LastAutoTaggedAt = null if pathChanged)
                  ├─ _repo.SaveChangesAsync                                       (flush #2 — assigns photo.Id)
                  ├─ existingRuleTags = _repo.GetPhotoTagsByPhotoAndSourceAsync(photo.Id, Rule)
                  ├─ _repo.RemovePhotoTagsAsync(existingRuleTags)
                  ├─ foreach matched tagName:
                  │    ├─ tag = _repo.GetOrCreateTagAsync(tagName)                (may flush internally if new)
                  │    └─ _repo.AddPhotoTagAsync(new PhotoTag { ... })
                  └─ _repo.SaveChangesAsync                                       (flush #3)
         ├─ root.DeltaLink = ...; root.LastIndexedAt = ...
         └─ _repo.SaveChangesAsync                                                (flush #4)
```

This matches the original flush sequence (`PhotobankIndexJob.cs:72,104,145,168`) one-for-one. Reviewer-verifiable.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Mocked tests can't catch EF translation issues that real-DB tests would have surfaced (e.g., string comparison, LINQ-to-SQL gotchas) | Medium | Acceptable — the InMemoryDatabase tests this file uses today also don't catch those (they use LINQ-to-Objects). The job's data-shape behavior is still covered by the four scenarios; query-translation issues are caught by other code paths in PR/staging. Out of scope per spec. |
| `GetActiveRootsWithDriveAsync` returns untracked entities by mistake (copy-paste of `GetTagsWithCountsAsync`'s `AsNoTracking`) → silent data loss when job tries to write back `DeltaLink` | High | Decision 2 above is explicit; the third test (`ExecuteAsync_PersistsDeltaLink_AfterRun`) will catch this when migrated — set up the mock to return a real `PhotobankIndexRoot` instance and `Verify` `SaveChangesAsync` is called after the mutations, then assert against the captured instance's state. |
| `RemovePhotoTagsAsync` or `RemovePhotoAsync` mistakenly call `SaveChangesAsync` internally → changes the flush count, breaking FR-5 transactional preservation | High | Decision 1 above; add a code-review checklist item: "new Add/Remove methods on the repo must NOT call SaveChangesAsync." Verify by counting `await _context.SaveChangesAsync` occurrences in `PhotobankRepository.cs` before vs. after (should be unchanged: only `GetOrCreateTagAsync` and `GetOrCreateTagsAsync` save internally). |
| New methods become "future-proofing" surface area (e.g., adding overloads not the job needs) | Low | Spec FR-1 acceptance criterion already forbids this. Reviewer should confirm method count = 7 new. |
| Reflection-based DI scan picks up `PhotobankIndexJob` but a developer also adds an explicit registration in `PhotobankModule.cs` → double-registration of `IRecurringJob` (harmless) but signals confusion | Low | Do NOT add `services.AddScoped<PhotobankIndexJob>()` to `PhotobankModule.cs`. Recommend a follow-up (out of scope) to remove the redundant `PhotobankAutoTagJob` line at `PhotobankModule.cs:32`. |

## Specification Amendments

1. **File path correction.** Spec FR-1 and FR-2 imply `PhotobankRepository.cs` lives in an `Infrastructure/Persistence/` subfolder; the actual path is `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs`. The spec should reference the actual path so reviewers don't get confused.

2. **FR-3 simplification.** `PhotobankIndexJob` is already DI-registered via the assembly scan in `AddRecurringJobs()` at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:357-376` (registers both as `IRecurringJob` and as itself). The conditional "if it relies on convention-based scanning today, add an explicit `services.AddScoped<PhotobankIndexJob>()`" should be removed — no DI change is needed. The acceptance criterion "PhotobankIndexJob resolves successfully from the DI container at application startup" remains and is verified by the existing `RecurringJobDiscoveryService` startup path.

3. **Explicit "stage-only" convention.** Add to FR-1 the rule: `AddPhotoAsync`, `RemovePhotoAsync`, and `RemovePhotoTagsAsync` MUST NOT call `SaveChangesAsync` internally. They follow the existing convention of `AddPhotoTagAsync`, `RemovePhotoTagAsync`, `AddRootAsync`, `AddRuleAsync`, `RemoveRuleTagsAsync`. This is implied by FR-5 (preserved flush sequence) but worth stating directly to prevent a well-meaning implementer from "encapsulating the transaction."

4. **Tracking requirement for `GetPhotoBySharePointFileIdAsync` and `GetPhotoTagsByPhotoAndSourceAsync`.** Spec only states tracking for `GetActiveRootsWithDriveAsync`. The job also mutates the returned `Photo` (scalar field updates) and the returned `PhotoTag` list (passed to `RemovePhotoTagsAsync`). Both queries MUST also return tracked entities — i.e., no `AsNoTracking()`. Add this to FR-1.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The work can begin immediately on the existing branch.