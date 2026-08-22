# Architecture Review: Validate LLM-returned PhotoId against the batch before applying auto-tags

## Skip Design: true
Backend-only bug fix inside `PhotobankAutoTagJob` (a background job with no UI). No new or changed screens, components, or visual elements. `## Data Schemas` in the design doc can stay minimal/empty per the designer's own "no UI" branch of its template.

## Architectural Fit Assessment
This is a self-contained, single-class fix that stays entirely inside the existing Vertical Slice for Photobank (`Anela.Heblo.Application.Features.Photobank.Infrastructure.Jobs`). It does not cross module boundaries, does not touch `contracts/`, does not add a DTO, and does not change any repository interface (`IPhotobankAutoTagRepository`, `IPhotobankPhotoTagRepository`). It aligns cleanly with existing conventions:

- The class already has an `ILogger<PhotobankAutoTagJob>` injected and used for warning/error conditions (e.g. `LogError` on LLM call failure) — the new rejection log follows the same pattern, no new dependency.
- `JsonResponseParser.ParseOrFallback` already treats untrusted LLM output defensively (falls back to an empty result set on parse failure); this fix extends that same "don't trust the model's output" posture to the `id` field specifically, which today is the one field taken on faith.
- Tag-name validation against `tagsByName` already exists in `ApplyTagsForPhotoAsync` as the precedent for "validate LLM output against a known-good set before using it" — the fix for `Id` is structurally the same technique (allow-list membership check) applied to the currently-unchecked field.

No architectural amendment to the module's shape is needed; this is implementation-guidance-only, not a design decision.

## Proposed Architecture

### Component Overview
```
ProcessBatchAsync(batch, tagsByName, ct)
   │
   ├─ batchIds = batch.Select(p => p.Id)          [already exists]
   ├─ batchIdSet = new HashSet<int>(batchIds)      [NEW — one allocation per batch]
   │
   ├─ call LLM → parse → parsed.Results
   │
   ├─ foreach result in parsed.Results:
   │      if result.Id not in batchIdSet:
   │          LogWarning(...); continue            [NEW — reject before apply]
   │      else:
   │          ApplyTagsForPhotoAsync(result, ...)  [unchanged]
   │
   ├─ SaveChangesAsync                             [unchanged]
   └─ StampAutoTaggedAtAsync(batchIds, ...)         [unchanged — stamps the sent batch,
                                                      not the accepted results]
```

`ExecuteForPhotosAsync` needs no separate change: it already delegates to `ProcessBatchAsync` per sub-batch, so fixing the shared method covers both entry points (FR-2).

### Key Design Decisions

#### Decision 1: Where to enforce the check
**Options considered:**
1. Filter `parsed.Results` in `ProcessBatchAsync` before the `foreach` loop (build the allow-listed subset, then iterate).
2. Pass `batchIdSet` into `ApplyTagsForPhotoAsync` and have it early-return (with a log) when `result.Id` isn't in the set.

**Chosen approach:** Option 2 — pass the id set into `ApplyTagsForPhotoAsync` and reject at the top of that method.

**Rationale:** Keeps the "is this result trustworthy" decision co-located with the method that actually mutates data (`AddPhotoTagAsync`), mirroring how the existing tag-name allow-list check (`tagsByName.ContainsKey`) already lives inside the same method rather than being pre-filtered by the caller. This also means any future caller of `ApplyTagsForPhotoAsync` (there is currently only one) cannot forget to validate — the guard travels with the mutation, not with one particular call site. `ProcessBatchAsync`'s `foreach` loop body stays a one-line call, matching its current shape.

#### Decision 2: Set construction
**Options considered:**
1. Rebuild a `HashSet<int>` from `batchIds` per LLM result (inside the loop).
2. Build it once per batch, before the loop, and pass it down.

**Chosen approach:** Option 2.

**Rationale:** `batchIds` (a `List<int>`) already exists per batch; converting it to a `HashSet<int>` once keeps membership checks O(1) instead of O(n) per result, satisfying NFR-1 without adding real complexity — batch sizes are small so this is not performance-critical, but doing it right costs nothing extra.

## Implementation Guidance

### Directory / Module Structure
No new files. All changes confined to:
`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankAutoTagJob.cs`

### Interfaces and Contracts
No interface changes. `ApplyTagsForPhotoAsync`'s signature grows by one parameter (the batch id set), e.g.:

```csharp
private async Task ApplyTagsForPhotoAsync(
    AutoTagResult result,
    HashSet<int> batchIds,
    Dictionary<string, int> tagsByName,
    CancellationToken ct)
{
    if (!batchIds.Contains(result.Id))
    {
        _logger.LogWarning(
            "AI tagging result id {ResultId} is not in the sent batch (batch size {BatchSize}); dropping result.",
            result.Id, batchIds.Count);
        return;
    }

    // ... existing tag-vocabulary filtering and AddPhotoTagAsync loop, unchanged ...
}
```

`ProcessBatchAsync` builds `var batchIdSet = new HashSet<int>(batchIds);` alongside the existing `batchIds` list (both are needed: the `List<int>` for `StampAutoTaggedAtAsync`'s existing `IReadOnlyList<int>` parameter, the `HashSet<int>` for O(1) membership checks) and passes `batchIdSet` into each `ApplyTagsForPhotoAsync` call.

This is purely additive/internal — no public surface changes, so no OpenAPI/TS client regeneration is triggered.

### Data Flow
Unchanged end-to-end shape (LLM → parse → apply → save → stamp → invalidate cache). The only new branch is a reject-and-log short-circuit inside the existing apply step, for a single `AutoTagResult` at a time — it does not affect the batch-level `SaveChangesAsync` / `StampAutoTaggedAtAsync` / `_cache.Invalidate()` calls, which still run for the whole batch exactly as today (per FR-1's acceptance criterion that stamping behavior is unchanged).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing tests assert exact `AddPhotoTagAsync` call counts/args; adding a parameter to a private method doesn't break callers outside the class, but any test that reflects into the private method signature would need updating | Low | `PhotobankAutoTagJobTests` only exercises `ExecuteAsync`/`ExecuteForPhotosAsync` (public surface) and asserts via mocks — no direct calls to the private method, so no existing test needs modification. New tests should assert the reject path via the same public-surface pattern (mock LLM response with an out-of-batch id, assert `AddPhotoTagAsync` not called for that id, batch's real ids still stamped). |
| Log noise/level miscalibration — if set too low (Information), the signal gets lost in nightly job logs; if it fires on every run due to a bug in id-set construction, it becomes noise that gets ignored | Low | Use `LogWarning` (per NFR-3) and keep the log payload to just the offending id + batch size — cheap to scan, and only fires on the actual anomaly (out-of-batch id), which given normal LLM behavior should be rare/zero in steady state. |
| Someone assumes this also needs to filter `Tags`-array-level id spoofing (e.g. if the schema later changes to a nested id-per-tag) | Low | Out of current schema — `AutoTagResult` only has a single `Id` per result today; noted as N/A, not a real risk under the current contract. |

## Specification Amendments
None. The spec's FR-1/FR-2/FR-3 and NFR-1..3 are implementable as written; the "Decision 1" choice above (validate inside `ApplyTagsForPhotoAsync`, not by pre-filtering in `ProcessBatchAsync`) is an implementation detail within FR-1/FR-2's stated acceptance criteria (a single shared validation point used by both entry points), not a change to the spec's requirements.

## Prerequisites
None. No migrations, config, or infrastructure changes required — this is a pure code change deployable with the next normal build/deploy cycle.
