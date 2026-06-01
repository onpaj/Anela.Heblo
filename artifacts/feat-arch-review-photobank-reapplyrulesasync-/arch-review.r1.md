I now have everything I need, including a critical grounding finding: the EF Core InMemory provider does **not** support `ExecuteDeleteAsync` (confirmed by `LeafletRepositoryTests.cs:129` skip and the smartsupp plan note), which directly shapes how the removal primitive must be built and tested. Here is the review.

```markdown
# Architecture Review: Move `ReapplyRules` orchestration out of `PhotobankRepository` into its handler

## Skip Design: true
Backend-only refactor of the Photobank tag re-apply path. No new or changed UI components, screens, or visual decisions. The HTTP contract (`ReapplyRulesRequest`/`ReapplyRulesResponse`) is unchanged.

## Architectural Fit Assessment

This refactor moves the codebase *toward* its own stated conventions, not away from them. `docs/architecture/development_guidelines.md` explicitly lists "Business logic in Controller class" as forbidden because "Business logic should be in MediatR handlers" — the same principle applies one layer down: orchestration in the repository is the same smell. `RetagPhotosHandler` (`UseCases/RetagPhotos/RetagPhotosHandler.cs`) is the model to follow — it sequences several repository primitives plus a cache invalidation, holds the in-memory decision logic itself, and keeps the repository methods thin. `ReapplyRulesHandler` should look like that after this change.

Integration points are all internal and well-bounded:
- **`ReapplyRulesHandler`** — gains the orchestration loop. Already depends on `IPhotobankRepository`, `IPhotobankTagsCache`, and the `Domain.Features.Photobank` namespace (where `TagRuleMatcher` lives), so no new dependency directions are introduced. Application → Domain is the correct, allowed direction.
- **`IPhotobankRepository` / `PhotobankRepository`** — lose `ReapplyRulesAsync`, gain five primitives. The `TagRuleMatcher` reference disappears from the repository entirely (it survives only as a `using` today; confirm the `using Anela.Heblo.Domain.Features.Photobank` line stays for the other types).
- **Callers** — verified: `ReapplyRulesAsync` has exactly one production caller (`ReapplyRulesHandler:36`) plus test mocks. No controller, job, or other handler calls it. The blast radius is contained.

The proposal aligns with existing primitive granularity (`AddPhotoTagAsync`, `RemovePhotoTagAsync`, `GetOrCreateTagAsync` all already exist as single-purpose methods).

## Proposed Architecture

### Component Overview

```
PhotobankController
   └─ MediatR ─► ReapplyRulesHandler                         (orchestration: NEW home)
                    │
                    ├─ IPhotobankRepository.GetRulesAsync           (exists)
                    ├─ IPhotobankRepository.RemoveRuleTagsAsync     (NEW primitive)
                    ├─ IPhotobankRepository.SaveChangesAsync        (exists) ── flush removals
                    ├─ IPhotobankRepository.GetOccupiedTagPairsAsync(NEW primitive)
                    ├─ IPhotobankRepository.GetOrCreateTagsAsync    (NEW primitive, flushes)
                    ├─ IPhotobankRepository.GetAllPhotosAsync       (NEW primitive)
                    │
                    ├─ TagRuleMatcher.GetMatchingTags (Domain)      ── per photo, in-memory
                    │   + dedup (addedPairs) + precedence (occupiedSet)  ── in handler
                    │
                    ├─ IPhotobankRepository.AddPhotoTagsAsync       (NEW primitive)
                    ├─ IPhotobankRepository.SaveChangesAsync        (exists) ── final commit
                    └─ IPhotobankTagsCache.Invalidate               (exists) ── once, after save
```

The repository becomes a set of EF-only data primitives. All branching, deduplication, source-precedence, and counting logic lives in the handler as pure in-memory work over materialized lists — which is exactly what makes it unit-testable with a mocked `IPhotobankRepository`.

### Key Design Decisions

#### Decision 1: `RemoveRuleTagsAsync` must use tracked `RemoveRange`, NOT `ExecuteDeleteAsync`
**Options considered:**
- (A) `ExecuteDeleteAsync` with the `pt.Tag.Name == scopeToTagName` filter — fewer round-trips, matches `RemovePhotoTagsBySourceAsync:416–422`.
- (B) Tracked query + `RemoveRange`, exactly as the current `ReapplyRulesAsync:280–285` does.

**Chosen approach:** (B) — tracked `RemoveRange`.

**Rationale:** The EF Core **InMemory provider does not support `ExecuteDeleteAsync`** — confirmed by the explicitly skipped `LeafletRepositoryTests.cs:129` (*"a relational operation not supported by the in-memory EF provider"*) and the note in `docs/superpowers/plans/2026-05-20-smartsupp-webhook-audit-replay.md:1759`. FR-4 mandates a behavior-preservation test against a real `DbContext`, and every existing repository test in this module (`PhotobankRepositoryFilterTests`, etc.) runs on InMemory. Choosing (A) would make both the new primitive's test and the e2e behavior-preservation test impossible to run on the project's standard test harness (they'd have to be skipped, as the Leaflet one was). The current code already uses tracked `RemoveRange` precisely because it is InMemory-testable; preserve that. This is also strictly behavior-preserving for the resulting rows.

#### Decision 2: Flush staged removals before re-adding (the delete/re-add tracking hazard)
**Options considered:**
- (A) Keep the current *conditional* intermediate flush (`if (newTagsCreated) SaveChanges`) and reproduce it implicitly.
- (B) Always flush removals before the add loop — i.e. handler calls `RemoveRuleTagsAsync` then `SaveChangesAsync`, *then* resolves tags / adds.

**Chosen approach:** (B) — deterministic flush of removals before any `PhotoTag` is added.

**Rationale:** With composite PK `(PhotoId, TagId)` shared across sources (`PhotoTag.cs`), a Rule tag removed via tracked `RemoveRange` sits in the change tracker as `Deleted`. On a no-op re-apply (rules unchanged, tags already exist, photos still match), the handler re-adds the *same* `(PhotoId, TagId)` pairs. EF Core throws *"another instance with the same key value is already being tracked"* when an `Added` entity collides with a `Deleted` one of the same key. The current code only escapes this when `newTagsCreated == true` (its intermediate flush at line 326 detaches the deleted rows first); the no-new-tags path is a latent hazard. Flushing removals unconditionally before re-adds eliminates the collision in **all** paths and yields identical final rows. Since `GetOrCreateTagsAsync` already needs to flush to obtain DB-assigned IDs for new tags, the cleanest sequencing is: `RemoveRuleTagsAsync` → `SaveChangesAsync` (commit removals) → `GetOrCreateTagsAsync` → build → `AddPhotoTagsAsync` → final `SaveChangesAsync`. This is one extra save versus today in the no-new-tags branch, which is acceptable: the spec's binding criterion is identical resulting rows, not identical save count.

#### Decision 3: Batch `GetOrCreateTagsAsync`, not a loop over the existing `GetOrCreateTagAsync`
**Options considered:** Reuse per-name `GetOrCreateTagAsync` in a handler loop vs. a new batch primitive.
**Chosen approach:** New batch `GetOrCreateTagsAsync(IReadOnlyCollection<string> normalizedNames)`.
**Rationale:** The spec already calls this out (NFR-1). `GetOrCreateTagAsync` issues one query + one `SaveChanges` *per name* (`PhotobankRepository:160–171`); looping it would turn today's single round-trip resolve into an N+1. The batch primitive preserves the current `Where(t => names.Contains(t.Name))` single-query load + single flush for missing tags (`ReapplyRulesAsync:311–326`). Return `IReadOnlyDictionary<string, int>` (name→TagId), not tracked `Tag` entities — the handler only needs the IDs to build `PhotoTag` rows, and returning IDs keeps tracking concerns inside the repository.

#### Decision 4: Keep "load all photos" — do not introduce batching in this refactor
**Chosen approach:** `GetAllPhotosAsync` returns the full `List<Photo>`, identical to `_context.Photos.ToListAsync` today.
**Rationale:** The brief flags load-all as a scalability concern, but the stated goal of *this* change is behavior preservation and relocating the seam. Introducing batching now would (a) violate behavior-preservation, (b) complicate the `addedPairs`/`occupiedSet` dedup which currently assumes a single in-memory pass, and (c) be speculative (YAGNI). The value delivered here is that batching becomes *possible* at the handler layer later. State this as an explicit non-goal.

## Implementation Guidance

### Directory / Module Structure
No new files for production code — edit in place:
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs` — remove `ReapplyRulesAsync`; add the five primitive signatures (place them under the existing `// Photo tags` / `// Tags` comment groupings to match the file's organization).
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs` — delete `ReapplyRulesAsync` (lines 275–372); add the five primitive implementations.
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — absorb the orchestration. `ReapplyRulesRequest`/`ReapplyRulesResponse` are unchanged.

Tests:
- `backend/test/Anela.Heblo.Tests/Features/Photobank/ReapplyRulesHandlerTests.cs` — rewrite against the new primitives (the current three "delegated to repository" tests no longer make sense once logic moves into the handler).
- New repository primitive tests — extend the existing `PhotobankRepository*Tests` InMemory pattern (per-test `Guid` DB, `IDisposable`).
- New end-to-end behavior-preservation test against a real `ApplicationDbContext` (InMemory) — see Risks.

### Interfaces and Contracts
Add to `IPhotobankRepository` (signatures are prescriptive; match existing nullability/`CancellationToken` conventions):

```csharp
// Photos
Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken);

// Photo tags
Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(
    string? scopeToTagName, CancellationToken cancellationToken);
Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);

// Tags
Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(
    IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);
```

Per-primitive behavior contract (each must be behavior-identical to the corresponding slice of `ReapplyRulesAsync`):
- `RemoveRuleTagsAsync`: tracked `Where(pt => pt.Source == Rule)`, plus `Where(pt => pt.Tag.Name == scopeToTagName)` when scoped → `RemoveRange`. **Does not save** (handler controls the save).
- `GetOccupiedTagPairsAsync`: `Where(pt => pt.Source != Rule)` + scope filter, project `(PhotoId, TagId)`, return as `HashSet`. Read-only.
- `GetOrCreateTagsAsync`: single query for existing tags by name; create missing as `new Tag { Name = name }`; **flush** so all have IDs; return name→Id map. Mirrors `GetOrCreateTagAsync`'s save-internally precedent.
- `AddPhotoTagsAsync`: `_context.PhotoTags.AddRange(...)`. **Does not save** (mirrors existing `AddPhotoTagAsync`).
- `GetAllPhotosAsync`: `_context.Photos.ToListAsync` — no `Include`; the handler only reads `Id`, `FolderPath`, `FileName`.

Handler responsibilities (the algorithm from spec Background, steps 1–9), kept in this order to honor Decision 2:

```
1. allRules = GetRulesAsync(); resolve scopeToTagName from RuleId (existing logic, unchanged).
2. activeRules = allRules.Where(r => r.IsActive).
3. ruleTagNames = activeRules.Select(TagName.ToLowerInvariant()).Distinct()
                  [.Where(== scopeToTagName) when scoped]; if empty → return PhotosUpdated = 0.
4. await RemoveRuleTagsAsync(scopeToTagName); await SaveChangesAsync();   // commit removals
5. occupied = await GetOccupiedTagPairsAsync(scopeToTagName);
6. tagIdsByName = await GetOrCreateTagsAsync(ruleTagNames);               // flushes new tags
7. photos = await GetAllPhotosAsync();
8. addedPairs = new HashSet<(int,int)>(); now = DateTime.UtcNow; photosUpdated = 0;
   foreach photo: matched = TagRuleMatcher.GetMatchingTags(folderPath, fileName, activeRules)
                          [.Where(== scopeToTagName) when scoped];
       for each matched name → pair (photo.Id, tagId): skip if !addedPairs.Add(pair); skip if occupied.Contains(pair);
       else stage new PhotoTag{Rule, now}; mark photo updated.
9. await AddPhotoTagsAsync(staged); await SaveChangesAsync(); _cache.Invalidate();
   return PhotosUpdated = photosUpdated.
```

Note step 3 is hoisted *before* the removal (the current code computes it after the snapshots, but it has no dependency on them) so the early `return 0` short-circuits before any DB mutation — preserving the current "no active rule tag names ⇒ return 0, nothing removed" outcome. Verify this matches: in the current code, when `ruleTagNames` is empty the method returns 0 at line 309 *after* staging the removal at line 285 but *before* the final `SaveChanges` (which the handler owns), so the removal is never committed. Hoisting the empty-check before `RemoveRuleTagsAsync` reproduces that exact net effect (no removal committed) — confirm in the e2e test.

### Data Flow
**Re-apply all (`RuleId == null`):** removal scoped to all Rule tags → occupied = all Manual/AI pairs → all active rule tag names resolved/created → every photo matched against all active rules → new Rule tags staged where not deduped and not occupied → single commit → cache invalidated.

**Re-apply one rule (`RuleId == n`):** `scopeToTagName = rule.TagName.ToLowerInvariant()` filters every step (removal, occupied snapshot, tag-name set, per-photo match filter), so only that one tag's Rule rows are touched. Rule-not-found returns `ErrorCodes.PhotobankRuleNotFound` before any mutation (unchanged from `ReapplyRulesHandler:29–31`).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Delete-then-re-add of the same `(PhotoId, TagId)` throws an EF change-tracker collision on no-op re-applies (the latent hazard in today's no-new-tags path) | **HIGH** | Decision 2: flush removals (`SaveChangesAsync`) before staging any adds. Add a behavior-preservation test that re-applies twice with pre-existing rule tags and **no** new `Tag` rows created, asserting identical rows and no exception. |
| Behavioral drift from the 100-line algorithm (precedence, dedup, scoped filter, empty-rule early return, count semantics) | **HIGH** | The end-to-end test (FR-4) against a real `DbContext` is the binding gate. Cover: Manual/AI wins over Rule on shared PK; duplicate matches counted once; `photosUpdated` counts photos not tags; scoped vs. unscoped; empty active rules ⇒ 0 and no removal committed. |
| Choosing `ExecuteDeleteAsync` for removal would break InMemory tests | MEDIUM | Decision 1: use tracked `RemoveRange`. Do not copy `RemovePhotoTagsBySourceAsync`'s `ExecuteDeleteAsync` pattern here (that method is, notably, not covered by an InMemory test). |
| N+1 regression if the handler loops `GetOrCreateTagAsync` instead of batching | MEDIUM | Decision 3: batch `GetOrCreateTagsAsync` with a single load + single flush. |
| Stale handler tests assert `ReapplyRulesAsync` is called (`ReapplyRulesHandlerTests` + `PhotobankTagsCacheInvalidationTests:206`) | MEDIUM | Rewrite both. `PhotobankTagsCacheInvalidationTests:206` mocks `ReapplyRulesAsync` and must be updated to the new primitive sequence or it won't compile. |
| Scoped name comparison casing | LOW | `scopeToTagName` is already lowercased in the handler; `ruleTagNames` lowercased; `TagRuleMatcher` returns lowercased names. Preserve all `ToLowerInvariant()` calls verbatim. |

## Specification Amendments

1. **Add `GetOccupiedTagPairsAsync` and `GetAllPhotosAsync` as named primitives in FR-2** with the contracts above — the spec summary lists them but pin the `HashSet<(int,int)>` return for occupied pairs and the no-`Include` requirement for photos.
2. **Amend FR-1/FR-3 with the removal-flush ordering (Decision 2).** The spec's algorithm reproduces the current *conditional* intermediate flush; replace it with an unconditional flush of removals before adds, and document why (change-tracker collision). This is the one place where the refactor deliberately diverges from the current save sequence while keeping resulting rows identical.
3. **Constrain the removal mechanism in FR-2:** `RemoveRuleTagsAsync` must use tracked `RemoveRange`, explicitly **not** `ExecuteDeleteAsync`, citing InMemory-provider incompatibility (Decision 1). Without this, FR-4's "real `DbContext`" test cannot run on the project's standard harness.
4. **FR-4 e2e test must include the no-new-tags double-apply case** (the HIGH-risk path), not only the happy path.
5. **State load-all-photos batching as an explicit non-goal** of this change (Decision 4), so a reviewer doesn't expect it.

## Prerequisites
None. No migrations (schema unchanged — `PhotoTag`, `Tag`, `TagRule` untouched), no config, no infrastructure. This is a pure relocation of logic plus repository surface changes. Standard validation gates apply before completion: `dotnet build`, `dotnet format`, and the touched test projects (`Anela.Heblo.Tests` Photobank suite) green on the InMemory provider.
```