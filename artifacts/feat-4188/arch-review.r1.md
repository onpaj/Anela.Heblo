# Architecture Review: Photobank Batch Tag Upsert N+1 Query Fix

## Skip Design: true
Pure backend performance fix inside an existing background job's private method. No new or changed UI, no new endpoints, no visual components.

## Architectural Fit Assessment
This change fits cleanly into the existing Photobank module without introducing new architectural concepts. `PhotobankIndexJob.UpsertPhotoBatchAsync` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`, "Phase B" starting at line 204) already batches Phase A (photo upsert) correctly with bulk tag-name resolution via `GetOrCreateTagsAsync`; only the tag-reconciliation inner loop (lines 240–259) regressed to per-photo/per-pair queries. The repository this touches, `IPhotobankPhotoTagRepository` / `PhotobankPhotoTagRepository` (`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs`), already exposes the exact bulk primitive needed (`GetOccupiedTagPairsAsync`), proven in production by `ReapplyRulesHandler` (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`). This is a same-layer, same-pattern fix — no module boundary changes, no new dependencies.

**One important nuance the fix must account for** (confirmed by reading `PhotobankIndexJobTests.cs`, e.g. `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk`, lines 986–1099): `PhotoTagExistsAsync` today queries the live database directly and — by design, per that test's own comment — **can never see this batch's own unflushed inserts**, because `SaveChangesAsync` for the tag phase is deferred to a single flush at the end of the whole batch (line 261). The current per-photo dedupe against *within-batch* duplicates is only safe today because `tagNamesByPhoto` (lines 233–238) already collapses multiple Graph delta items for the same `SharePointFileId` into one dictionary entry *before* the tag loop runs — the loop itself never processes the same photo twice. Any bulk-preload replacement must preserve this "each photo processed once" invariant, or explicitly add an in-memory `HashSet` of pairs added so far in the batch (mirroring `ReapplyRulesHandler`'s `addedPairs`, lines 79 and 105–107) so the guard does not silently rely on a check that cannot see the batch's own pending writes.

## Proposed Architecture

### Component Overview
```
PhotobankIndexJob.UpsertPhotoBatchAsync
  │
  ├─ Phase A (unchanged): upsert Photo rows, single SaveChangesAsync
  │
  └─ Phase B (changed): tag reconciliation
       1. Compute tagNamesByPhoto (unchanged, lines 233-238)
       2. Resolve tag IDs via GetOrCreateTagsAsync (unchanged, line 220-222)
       3. NEW: bulk-preload existing Rule-source tags for
          photosByFileId.Keys  →  IPhotobankPhotoTagRepository
          .GetPhotoTagsByPhotosAndSourceAsync(photoIds, Rule, ct)
          → Dictionary<int PhotoId, List<PhotoTag>>
       4. NEW: bulk-preload occupied non-Rule pairs for the same
          photo IDs → IPhotobankPhotoTagRepository
          .GetOccupiedTagPairsAsync(scopeToTagName: null, ct)
          — SAME METHOD ReapplyRulesHandler already uses, just
            called once per batch here too, no interface change
          → HashSet<(int PhotoId, int TagId)>
       5. Per-photo loop: remove existing rule tags (from step 3's
          dictionary) + add new ones, guarded by the step-4 HashSet
          UNIONED with an in-loop "addedPairs" HashSet for pairs
          just queued for insertion in this same batch
       6. Single SaveChangesAsync (unchanged, line 261)
```

### Key Design Decisions

#### Decision 1: Scope `GetOccupiedTagPairsAsync` to the batch's photo IDs, don't add a new method
**Options considered:**
- (a) Reuse `GetOccupiedTagPairsAsync(string? scopeToTagName, ...)` as-is, unscoped by photo, and filter client-side to the batch's photo IDs.
- (b) Add a new overload/parameter that scopes the query by a photo-ID set at the SQL level (`WHERE PhotoId IN (...)`), matching how `RemovePhotoTagsBySourceAsync` already takes `IReadOnlyList<int> photoIds`.
**Chosen approach:** (b) — add photo-ID scoping to the existing method (or a sibling overload) so the query itself is bounded by batch size, not by total table size. `ReapplyRulesHandler` can afford an unscoped, `scopeToTagName`-only query because it runs standalone over the whole table; `PhotobankIndexJob` runs nightly against a table that only grows, so an unscoped `GetOccupiedTagPairsAsync` call would itself become an unbounded full-table scan — reintroducing a scaling problem one level up.
**Rationale:** Keeps the fix's complexity truly O(batch size), consistent with NFR-1 in the spec, and follows the existing convention of `RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, ...)` for photo-ID-scoped bulk repository calls.

#### Decision 2: Add `GetPhotoTagsByPhotosAndSourceAsync` (plural) as a new repository method rather than repurposing the singular one
**Options considered:**
- (a) Change `GetPhotoTagsByPhotoAndSourceAsync(int photoId, ...)`'s signature to accept a collection, breaking/changing its one existing call site.
- (b) Add a new method `GetPhotoTagsByPhotosAndSourceAsync(IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken ct)` returning `IReadOnlyDictionary<int, List<PhotoTag>>`, leaving the singular method untouched.
**Chosen approach:** (b). Grep confirms `GetPhotoTagsByPhotoAndSourceAsync` is called only from the one loop this fix removes (`PhotobankIndexJobTests.cs` mocks are all for this same call site) — but adding a new method rather than changing the existing one keeps the interface change additive and avoids any risk to other call sites this review did not need to touch, and mirrors the `RemovePhotoTagAsync`/`RemovePhotoTagsAsync` (singular/plural) naming pattern already present on `IPhotobankPhotoTagRepository`.
**Rationale:** Minimizes blast radius; additive interface changes are lower-risk than modifying an existing method's contract.

#### Decision 3: Preserve "remove-then-add" semantics per photo, driven entirely by preloaded in-memory data
**Options considered:**
- (a) Keep the exact current control flow (remove existing rule tags unconditionally, then add each newly-matched tag if not "existing") but source both checks from the two new in-memory lookups instead of two new queries per photo.
- (b) Redesign the loop as an explicit diff (compute added-tags and removed-tags sets per photo, only touching what changed), matching `ReapplyRulesHandler`'s style more closely.
**Chosen approach:** (a), with the guard against within-batch duplicates from Decision derived below. Preserves the exact tag mutation semantics (FR-3 in the spec) with the smallest possible diff to the method — this is a targeted N+1 fix, not a rewrite of the reconciliation algorithm.
**Rationale:** Lowest risk, smallest diff, directly addresses the filed finding without expanding scope. See **Specification Amendments** below for a related semantics note this decision surfaces.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Changes are confined to:
- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankPhotoTagRepository.cs` — add one new method signature (plural, photo-set-scoped tag lookup by source). `GetOccupiedTagPairsAsync`'s existing signature can be reused unscoped-by-photo-ID if its SQL cost at current table size is negligible, or extended with a photo-ID filter if not (see Decision 1) — the architect defers the exact call-site decision to implementation, informed by the current `PhotoTags` table size/indexing.
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — implement the new method as a single `WHERE PhotoId IN (...) AND Source == ...` query grouped by `PhotoId`.
- `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs` — replace lines 240–259's per-photo `await` calls with the two preloaded lookups plus an in-loop `addedPairs` `HashSet<(int, int)>` for within-batch duplicate protection (see Architectural Fit Assessment nuance).
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` — existing tests mock `GetPhotoTagsByPhotoAndSourceAsync`/`PhotoTagExistsAsync` per call; these mocks must be updated to mock the new bulk method(s) instead. Every existing test's asserted outcome (which `AddPhotoTagAsync`/`RemovePhotoTagsAsync` calls happen, how many times) must still pass unchanged, since FR-3 requires identical externally observable behavior.

### Interfaces and Contracts
```csharp
// IPhotobankPhotoTagRepository — new method
Task<IReadOnlyDictionary<int, List<PhotoTag>>> GetPhotoTagsByPhotosAndSourceAsync(
    IReadOnlyCollection<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);
```
`GetOccupiedTagPairsAsync` keeps its current signature and return type (`Task<HashSet<(int PhotoId, int TagId)>>`); whether it needs a photo-ID-scoped overload is an implementation-time call based on current `PhotoTags` row counts (flag for the developer to check via `docs/architecture/development_guidelines.md` conventions before deciding to add scoping vs. reuse as-is).

### Data Flow
1. Batch arrives → Phase A upserts `Photo` rows (unchanged).
2. `tagNamesByPhoto` computed (unchanged) → set of distinct `photosByFileId.Keys` for this batch is known.
3. Two new bulk queries run once, keyed to that photo-ID set: existing Rule tags per photo, and occupied non-Rule pairs.
4. Per-photo loop becomes pure in-memory: dictionary lookup for "what Rule tags exist now" (→ `RemovePhotoTagsAsync`), `HashSet.Contains` for "does this pair already exist" (→ skip `AddPhotoTagAsync` or not), with an additional in-loop `addedPairs` set to dedupe insertions attempted twice within the same batch.
5. Single `SaveChangesAsync` flushes everything (unchanged).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Bulk-scoping `GetOccupiedTagPairsAsync` (or its replacement) by photo IDs at large batch counts could still be a large `IN (...)` clause if batch size grows well beyond 200 | Low | Batch size is a job-level constant already tuned for Phase A; no change proposed to it. Revisit only if batch size itself changes. |
| Existing unit tests mock the two per-photo/per-pair repository methods directly; all of them need updating to mock the new bulk method(s) instead, or they will fail to compile/pass | Medium | Enumerated explicitly in Implementation Guidance above; this is expected, contained test-maintenance work, not a design risk. |
| `PhotoTagExistsAsync`'s documented inability to see the batch's own unflushed inserts (per the existing `...AppliesTagOnceWithNoDuplicateKeyRisk` test) must not be silently reintroduced by the bulk replacement for the same reason | Medium | The proposed design explicitly adds an in-loop `addedPairs` HashSet (mirroring `ReapplyRulesHandler`) so within-batch duplicate protection no longer depends on any DB round-trip, closing this gap for good rather than reproducing it in a new form. |
| Bulk-preloading Rule tags gives the implementation, for the first time, a clean in-memory snapshot of "what exists" *before* any removal — this makes it possible (and tempting) to also fix the "remove-then-re-add" semantics into a true diff. Doing so as a drive-by change would silently alter production tag-reconciliation behavior beyond what was reported | Medium | Explicitly scoped OUT in Decision 3: keep the current remove-then-add control flow, sourced from preloaded data. Any deeper reconciliation-logic change belongs in a separate, deliberately-reviewed follow-up, not bundled into this N+1 fix. |

## Specification Amendments
- **FR-1 / FR-2 in `spec.r1.md`** should be read as covering the two new/reused bulk queries described above (`GetPhotoTagsByPhotosAndSourceAsync` new; `GetOccupiedTagPairsAsync` reused, with photo-ID scoping decided at implementation time per Decision 1).
- **Add to FR-3 (or as a new FR-4):** the replacement must add an explicit in-memory guard (`addedPairs`-style `HashSet`) against inserting the same `(PhotoId, TagId)` pair twice within one batch, since the current per-photo `PhotoTagExistsAsync` DB check that (co-incidentally) also served this purpose is being removed. This is not new scope — it is a correctness-preserving requirement of removing that call, confirmed necessary by the existing `UpsertPhotoBatch_DuplicateSharePointFileIdMatchingSameTagRule_AppliesTagOnceWithNoDuplicateKeyRisk` test.
- No other amendments; the spec's FRs, NFRs, and Out of Scope section otherwise hold as written.

## Prerequisites
None. No migrations, no config, no infrastructure changes — this is a pure application/persistence-layer code change deployable through the normal build/deploy pipeline.
