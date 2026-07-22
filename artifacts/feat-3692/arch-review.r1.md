# Architecture Review: Batch `SaveChangesAsync` calls in `PhotobankIndexJob.UpsertPhotoAsync`

## Skip Design: true

Confirmed by reading the full spec: this is an internal background-job refactor (`Infrastructure/Jobs/PhotobankIndexJob.cs`) with no controller, contract, DTO, or frontend surface change (spec's own "API / Interface Design" section states this explicitly). No new or changed UI components are involved.

## Architectural Fit Assessment

This fits cleanly into the existing Vertical Slice structure — the change is confined to one file already in the correct location (`Application/Features/Photobank/Infrastructure/Jobs/`), and it does not need to cross the module boundary rules in `docs/architecture/development_guidelines.md` (no new contracts, no new repository interface members, no DI changes).

The spec is right that `ReapplyRulesHandler` (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`) is the correct in-repo precedent: it already does "resolve everything for the whole set → one flush → apply everything → one flush" using `IPhotobankRepository`'s existing bulk-friendly methods (`GetOrCreateTagsAsync`, `AddPhotoTagsAsync`, `RemoveRuleTagsAsync`). I read `IPhotobankRepository`/`PhotobankRepository` in full and confirm every method the batch rewrite needs already exists — no interface change is required, consistent with the spec's claim.

However, active exploration surfaced two things the spec did not account for, both load-bearing for correctness:

1. **`GetOrCreateTagAsync` (singular) has a hidden internal flush.** `PhotobankRepository.GetOrCreateTagAsync` (line 191–202) calls `_context.SaveChangesAsync` itself whenever it creates a brand-new tag. `UpsertPhotoAsync`'s Phase B (tag application) calls this per item today. If the batch rewrite keeps calling it per item inside Phase B, every *first occurrence of a new tag name* in a batch triggers an extra, uncounted `SaveChangesAsync` — invisible to the "2 calls per batch" accounting in FR-1/FR-2, and inconsistent with the `ReapplyRulesHandler` precedent the spec cites, which instead pre-resolves all tag names for the whole set via the bulk `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` (one dictionary lookup + at most one flush for *all* new tags, not one per new tag). This should be fixed as part of this change, not left as-is — see Decision 1 below.

2. **In-batch item collisions are a real correctness risk, not just a performance detail.** Today, `SaveChangesAsync` runs after *every* item, so item K's `GetPhotoBySharePointFileIdAsync` query always sees the fully committed effect of items `1..K-1`. Deferring the flush to end-of-batch breaks that invariant for two scenarios that Microsoft Graph delta feeds can legitimately produce within a single page/response:
   - **Same `SharePointFileId` appears twice as a non-deleted item** in one batch (e.g. modified twice since last sync). Phase A's per-item `GetPhotoBySharePointFileIdAsync` would return `null` for the second occurrence (the first's insert isn't in the DB yet), creating **two** `Photo` rows with the same `SharePointFileId` — violating the uniqueness assumption NFR-2 explicitly calls out as something batching "must continue to hold."
   - **A file is upserted then deleted within the same delta** (rename/modify followed by delete, both surfacing in one sync). Today the upsert's `SaveChangesAsync` commits before the delete item is processed, so the delete's `GetPhotoBySharePointFileIdAsync` finds it and removes it. With deferred batch flushing, if the delete is processed before the pending upsert batch is flushed, the delete's lookup misses the still-unflushed row, the row is never removed, and (because `DeltaLink` advances past this delta) the orphaned photo is never cleaned up by a later run either.

   Both are addressed by two concrete, cheap design rules below (Decisions 2 and 3) — no interface changes needed, purely a matter of *how* the new method is written.

With those two additions, the approach is sound: mechanical, contained, no schema change, no new config surface (matches spec's Out of Scope).

## Proposed Architecture

### Component Overview

```
IndexRootAsync(root)
  ├─ GetDeltaAsync ............................ unchanged (already returns full in-memory List)
  ├─ GetActiveTagRulesAsync ................... unchanged
  └─ single pass over delta.Items (order preserved):
        non-deleted item  -> append to pendingBatch
        deleted item      -> if pendingBatch non-empty: FlushBatch(pendingBatch) first
                              -> GetPhotoBySharePointFileIdAsync + RemovePhotoAsync (unchanged, inline)
        pendingBatch.Count == BatchSize -> FlushBatch(pendingBatch)
     end of items -> FlushBatch(pendingBatch) if any remain
  ├─ root.DeltaLink / LastIndexedAt ........... unchanged, single SaveChangesAsync at end
  └─ catch/log/continue ....................... unchanged

FlushBatch(batch) == UpsertPhotoBatchAsync(batch, tagRules, driveId, ct)
  Phase A (per item, no flush):
    - lookup-or-create Photo via seenInThisBatch dict, falling back to GetPhotoBySharePointFileIdAsync
    - set fields, compute pathChanged, conditionally clear LastAutoTaggedAt
  -> SaveChangesAsync()                         [1 round-trip — FR-1]

  Phase B (per item, no flush):
    - pre-resolve ALL matching tag names for the whole batch via GetOrCreateTagsAsync (bulk)
    - per item: remove existing Rule tags (RemovePhotoTagsAsync — no flush), compute matches via
      TagRuleMatcher, add PhotoTag guarded by PhotoTagExistsAsync
  -> SaveChangesAsync()                         [1 round-trip — FR-2]
```

### Key Design Decisions

#### Decision 1: Pre-resolve tags for the whole batch with `GetOrCreateTagsAsync`, not per-item `GetOrCreateTagAsync`
**Options considered:**
- (a) Keep calling `GetOrCreateTagAsync(string)` per item inside Phase B, as today.
- (b) Before Phase B's per-item loop, collect the union of all `TagRuleMatcher.GetMatchingTags(...)` results across the whole batch, call `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` once to get a `name -> tagId` dictionary, then use that dictionary (no further repo calls to resolve tags) inside the per-item loop.

**Chosen approach:** (b), mirroring `ReapplyRulesHandler` exactly.

**Rationale:** `GetOrCreateTagAsync` silently calls `SaveChangesAsync` when it creates a tag (`PhotobankRepository.cs` line 200). Per-item calls reintroduce an uncounted, unbounded-in-N flush that the spec's round-trip math doesn't account for and that has no reason to exist once tag resolution is batched — the set of distinct tag names is bounded by the (small, static) rule count, so resolving them once per batch is strictly better and matches the cited precedent's actual code, not just its intent.

#### Decision 2: Deletions flush any pending upsert batch first
**Options considered:**
- (a) Process deleted items inline exactly where they occur, ignoring any pending (unflushed) upsert batch — matches the letter of FR-3 ("deleted items continue to be processed inline") but not the implicit invariant the rest of the job relies on.
- (b) When a deleted item is encountered, flush the pending upsert batch first (both phases), then process the deletion, then resume accumulating a fresh batch.

**Chosen approach:** (b).

**Rationale:** Deletes are rare relative to upserts, so the extra flush is cheap and only paid when it's actually needed (an upsert/delete interleaving for related items in the same delta). This preserves the exact today's-behavior guarantee that a delete always sees every prior item in the delta as committed — closing the orphaned-row correctness gap described above — without touching `RemovePhotoAsync` or the deletion query itself (FR-4 stays satisfied: deletion handling, `DeltaLink`/bookkeeping, and the outer `try`/`catch` are untouched).

#### Decision 3: Batch-scoped lookup for photos not yet flushed
**Options considered:**
- (a) Rely solely on `GetPhotoBySharePointFileIdAsync` for existing-photo lookup in Phase A, as today.
- (b) Maintain a `Dictionary<string, Photo>` scoped to the current batch, populated as each item is processed in Phase A; check it before falling back to `GetPhotoBySharePointFileIdAsync`.

**Chosen approach:** (b).

**Rationale:** This is the direct fix for the same-`SharePointFileId`-twice-in-one-batch scenario. It adds no new DB round-trips (it's a plain in-memory dictionary check ahead of the existing query) and keeps the upsert idempotent within a batch exactly as it already is across batches (per NFR-2).

## Implementation Guidance

### Directory / Module Structure
No new files or folders. All changes stay inside the existing file:
`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`

- Replace `UpsertPhotoAsync(GraphPhotoItem, ...)` with `UpsertPhotoBatchAsync(IReadOnlyList<GraphPhotoItem> batch, List<TagRule> tagRules, string? driveId, CancellationToken ct)` as the spec names it.
- Add a `private const int BatchSize = 200;` constant (spec FR-3: no config/feature-flag surface).
- Restructure `IndexRootAsync`'s `foreach` into the single-pass accumulate/flush loop described above (Component Overview). Do not split into "filter non-deleted, then chunk" as two separate passes — that would lose the delete-triggers-flush ordering from Decision 2. A streaming accumulate-and-flush over the original `foreach` preserves both order and the flush-before-delete rule with the least code change.

### Interfaces and Contracts
No `IPhotobankRepository` / `PhotobankRepository` changes. Call-site guidance for the new batch method:

| Concern | Use | Not |
|---|---|---|
| Resolve rule tag names for the batch | `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` once per batch | `GetOrCreateTagAsync(string)` per item |
| Add new `PhotoTag` rows | `AddPhotoTagAsync` per row is fine (no internal flush) — bulk `AddPhotoTagsAsync` also acceptable for consistency with `ReapplyRulesHandler`, developer's choice | N/A |
| Remove stale Rule tags per photo | `RemovePhotoTagsAsync` per item, as today (no internal flush, safe to call without batching) | N/A |
| Existing-photo lookup | `GetPhotoBySharePointFileIdAsync` guarded by an in-batch `Dictionary<string, Photo>` (Decision 3) | Raw per-item call with no batch-local cache |

### Data Flow
1. `IndexRootAsync` fetches the full delta and active tag rules (unchanged).
2. It walks `delta.Items` once, in order, accumulating non-deleted items into `pendingBatch`.
3. On a deleted item: if `pendingBatch` is non-empty, flush it (2 `SaveChangesAsync` calls) before handling the deletion inline (unchanged read + `RemovePhotoAsync`, no flush — the single end-of-root `SaveChangesAsync` at line 97 still covers it, exactly as today).
4. When `pendingBatch.Count == BatchSize`, flush immediately.
5. At the end of the items loop, flush any remaining partial batch.
6. `root.DeltaLink`/`LastIndexedAt` are set and saved exactly once, after the loop, unchanged (FR-4).

### Test Impact
`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` currently mocks `GetOrCreateTagAsync` (singular) and asserts `SaveChangesAsync` call counts (`Times.Once`, `Times.AtLeastOnce`) against single-item deltas — these single-item scenarios are unaffected by batch-size math (`ceil(1/200) = 1` batch either way) but the mock setups must switch to `GetOrCreateTagsAsync` (Decision 1) or the tests will fail to resolve tags at all under the new code. Add new test cases for:
- A delta with >1 item sharing the same batch, asserting exactly 2 `SaveChangesAsync` calls total for the upsert path (not 2×N).
- A delta larger than `BatchSize` (e.g. 201 items), asserting `2 * ceil(N/BatchSize)` calls.
- The same-`SharePointFileId`-twice-in-one-batch case, asserting a single `Photo` row results (Decision 3).
- An upsert-then-delete-for-the-same-item ordering within one delta, asserting the photo ends up removed (Decision 2).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Duplicate `SharePointFileId` within one batch creates two `Photo` rows (unique-constraint or duplicate-row bug not present today) | High | Decision 3: batch-local `Dictionary<string, Photo>` checked before the repository query |
| Upsert-then-delete for the same item within one delta leaves an orphaned row that's never cleaned up (DeltaLink already advanced) | Medium | Decision 2: flush any pending upsert batch before processing a deleted item |
| Per-item `GetOrCreateTagAsync` hidden flush defeats part of the round-trip reduction and is easy to miss in review since it "looks read-only" | Medium | Decision 1: use bulk `GetOrCreateTagsAsync` once per batch, as `ReapplyRulesHandler` does |
| Existing unit tests mock the wrong repository method (`GetOrCreateTagAsync`) and silently pass/fail for the wrong reason after refactor | Low | Update `PhotobankIndexJobTests.cs` mocks alongside the production change, in the same PR |
| Larger `BatchSize` grows the EF change-tracker graph per `SaveChangesAsync`, increasing per-call latency/memory | Low | 200 is a conservative default (spec's own number); no further tuning needed for this fix — `GetDeltaAsync` already holds the entire delta in memory, so a 200-item slice adds negligible overhead |

## Specification Amendments

1. **FR-2 / API-Interface-Design section should be amended** to specify that tag-name resolution in Phase B uses the bulk `GetOrCreateTagsAsync(IReadOnlyCollection<string>)` call once per batch (pre-resolving the union of matching tag names across all items in the batch), not the per-item `GetOrCreateTagAsync(string)`. Reason: the latter has a hidden internal `SaveChangesAsync` on new-tag creation (see Architectural Fit Assessment) that both breaks the spec's own round-trip accounting and contradicts the `ReapplyRulesHandler` precedent the spec cites as the model to follow.
2. **New requirement needed (call it FR-5): within-batch idempotency for repeated items.** The spec's NFR-2 says "Photo.SharePointFileId uniqueness assumption must continue to hold under batched writes exactly as under per-item writes" but doesn't specify *how*, given that batching removes the flush-per-item invariant that made this automatic before. Add: Phase A must resolve a photo via an in-batch cache before falling back to `GetPhotoBySharePointFileIdAsync`, so that multiple delta entries for the same `SharePointFileId` within one batch update the same tracked `Photo` instance rather than creating duplicates.
3. **FR-3 acceptance criteria should add:** if a batch of pending non-deleted items is open when a deleted item is reached, that pending batch is flushed (both `SaveChangesAsync` calls) before the deletion is processed — preserving today's implicit guarantee that a delete always observes every prior item in the delta as committed. This slightly changes the "exactly `2 * ceil(N/BatchSize)`" formula in NFR-1 to "at most" when deletes interleave with upserts in a way that forces early flushes; this is expected and should be called out as such rather than left as a silent deviation from the stated formula.

## Prerequisites
None beyond the code change itself — no new configuration, no migration, no infrastructure change. The existing `PhotobankIndexJobTests.cs` test file must be updated in the same change (not a separate prerequisite, but should not be treated as "done" without it, per the project's validation-before-completion rule requiring all touched tests to pass).
