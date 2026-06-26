I've written the specification to `artifacts/feat-arch-review-photobank-reapplyrulesasync-/spec.md`.

# Specification: Move `ReapplyRules` orchestration out of `PhotobankRepository` into its handler

## Summary
`PhotobankRepository.ReapplyRulesAsync` is a ~100-line method that performs application-layer orchestration (loading all photos, calling the `TagRuleMatcher` domain service, creating tags on the fly, deduplicating, and resolving source precedence) inside the data-access layer. This work moves that orchestration into `ReapplyRulesHandler` and reduces the repository to a small set of thin, individually testable data-access primitives. The change is **behavior-preserving**: the resulting set of `PhotoTag` rows after a re-apply must be identical to today's.

## Background
The Photobank module re-applies tag rules to photos in two modes: re-apply **all** active rules, or re-apply only the tag produced by a **single** rule (scoped). The current implementation buries the entire algorithm in `PhotobankRepository.ReapplyRulesAsync` (`backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs:275–372`), while `ReapplyRulesHandler` (`UseCases/ReapplyRules/ReapplyRulesHandler.cs`) is a 16-line shell that just calls the repository and `SaveChangesAsync`.

This inverts the intended Clean Architecture layering for this codebase:
- **Business logic belongs in MediatR handlers**, not repositories. The repository should be a thin data-access primitive.
- `TagRuleMatcher` is a **domain** service. Calling it from inside the repository creates an unnatural Domain → Repository orchestration dependency.
- Loading the **entire** `Photos` table into memory (`_context.Photos.ToListAsync`) on every re-apply is a scalability concern that the handler layer should be positioned to control (e.g. future batching) — impossible while the loop is hidden in the repository.
- The matching/precedence logic cannot be unit-tested without a real EF `DbContext`.

### Current algorithm (must be preserved exactly)
1. Filter rules to `IsActive == true`.
2. Delete all `PhotoTag` rows with `Source == Rule` (scoped to `Tag.Name == scopeToTagName` when a single rule is targeted).
3. Snapshot the `(PhotoId, TagId)` pairs of all **non-Rule** (`Manual`/`AI`) tags (same scope filter). `PhotoTag` has a composite primary key `(PhotoId, TagId)` shared across sources (`Domain/Features/Photobank/PhotoTag.cs`), so a Rule tag **cannot** be inserted where a Manual/AI tag already occupies that pair — Manual/AI wins.
4. Compute the distinct lowercased tag names produced by the active rules (scoped when applicable). If none, return `0`.
5. Load existing `Tag` rows for those names; create any missing `Tag` rows and flush so they receive DB-assigned IDs before use.
6. Load all photos. For each photo, call `TagRuleMatcher.GetMatchingTags(folderPath, fileName, activeRules)` (filtered to `scopeToTagName` when scoped).
7. For each matched tag name, add a `PhotoTag { Source = Rule, CreatedAt = now }` — skipping pairs already added in this run (`addedPairs`) and pairs occupied by Manual/AI (`occupiedSet`).
8. Return the count of **photos** that received at least one new Rule tag (`photosUpdated`).
9. The handler then calls `SaveChangesAsync` and invalidates the tags cache.

## Functional Requirements

### FR-1: Remove `ReapplyRulesAsync` from the repository interface and implementation
`Task<int> ReapplyRulesAsync(List<TagRule> allRules, string? scopeToTagName, CancellationToken cancellationToken)` is removed from `IPhotobankRepository` and from `PhotobankRepository`.

**Acceptance criteria:**
- `IPhotobankRepository` no longer declares `ReapplyRulesAsync`.
- `PhotobankRepository` no longer implements it.
- No `TagRuleMatcher` reference remains in the repository layer.
- Solution compiles with no references to the removed method.

### FR-2: Add thin data-access primitives to the repository
Add `GetAllPhotosAsync`, `RemoveRuleTagsAsync(scopeToTagName)`, `GetOccupiedTagPairsAsync(scopeToTagName)`, `GetOrCreateTagsAsync(normalizedNames)`, and `AddPhotoTagsAsync(photoTags)` — each a single-responsibility data-access operation with no domain logic. (Full signatures and per-method acceptance criteria are in the spec file.)

### FR-3: Move orchestration into `ReapplyRulesHandler`
The handler performs the full algorithm using only repository primitives plus `TagRuleMatcher`. **Behavior-preservation is the binding criterion**: identical resulting `PhotoTag` rows and `PhotosUpdated` count for any input, Manual/AI precedence preserved, scoped/not-found paths unchanged, cache invalidated once after save.

### FR-4: Update tests
Rewrite `ReapplyRulesHandlerTests` against the new primitives, add repository-level tests for each primitive, and add at least one end-to-end behavior-preservation test against a real `DbContext`.

## Notable design decisions (assumptions baked into the spec)
- I added two primitives the brief's suggested-fix list omitted but the algorithm requires: **`GetOccupiedTagPairsAsync`** (the Manual/AI shared-PK collision snapshot) and **`GetOrCreateTagsAsync`** (batch tag resolve+create). I chose a **batch** tag primitive rather than reusing the existing per-name `GetOrCreateTagAsync` to preserve the current single-round-trip behavior and avoid an N+1 (called out in NFR-1).
- Confirmed via `PhotoTag.cs` that the PK is composite `(PhotoId, TagId)` shared across sources, which is what makes the occupied-pair check correct — captured explicitly so the implementer doesn't drop it.

The spec ends with `## Status: COMPLETE` — no open questions block implementation.