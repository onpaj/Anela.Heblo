# Architecture Review: Journal default sort key ("entryDate") triggers spurious warning

## Skip Design: true

## Architectural Fit Assessment
This is a one-line control-flow fix inside `JournalRepository.ApplySort()`, a private static helper in `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`. It touches no public contract, no MediatR handler, no DTO, no UI. The existing `switch` expression already has the exact pattern needed — explicit lowercase-string arms per sort key falling through to a shared `ApplyDefaultSort`/`ApplyDefaultSortWithWarning` pair — and the same shape was already extended once before for `"createdbyusername"` (issue #2502). Adding an `"entrydate"` arm is a continuation of that established pattern, not a new one. There is no architectural risk and no integration surface beyond the single method.

## Proposed Architecture

### Component Overview
No new components. Single existing component modified:

```
JournalRepository (Persistence layer)
  └─ ApplySort(query, sortBy, sortDirection, logger)   ← add one switch arm here
       ├─ "title"              → explicit OrderBy/OrderByDescending
       ├─ "createdbyusername"  → explicit OrderBy/OrderByDescending (+ ThenBy tiebreak)
       ├─ "entrydate"          → NEW: delegates to ApplyDefaultSort (no warning)
       └─ _ (anything else)    → ApplyDefaultSortWithWarning (logs + delegates to ApplyDefaultSort)
```

### Key Design Decisions

#### Decision 1: Where the fix lives
**Options considered:**
- (a) Add an explicit `"entrydate"` arm in the switch (as spec proposes).
- (b) Change `ApplyDefaultSortWithWarning` to suppress the warning specifically when `sortBy` equals `"entrydate"`.
- (c) Validate/normalize `sortBy` at the request/handler layer instead of in the repository.

**Chosen approach:** (a) — add the explicit switch arm, exactly as both the finding and the spec describe.

**Rationale:** (b) special-cases the warning helper for one value, which is exactly backwards — that helper's entire purpose is "log because this key is unrecognized"; teaching it to recognize `"entrydate"` duplicates the switch's job in a second place. (c) moves sort-key validation to a different architectural layer for a single repository's internal helper, which is a bigger change than the bug warrants and contradicts the existing pattern (the sibling `"createdbyusername"` fix was also done at this exact layer, in the switch). (a) is the smallest change consistent with precedent.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Single edit:
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` — add the `"entrydate"` arm to the `switch` in `ApplySort()` (currently lines 152–163), placed before the `_` default arm, per the spec's FR-1. Arm order among the named cases doesn't matter functionally; placing `"entrydate"` first (as the spec shows) is fine and reads well since it's the default/most common key, but this is a style choice, not a requirement.

### Interfaces and Contracts
None changed. `IJournalRepository.GetEntriesAsync` / `SearchEntriesAsync` signatures, `ApplySort`, `ApplyDefaultSort`, and `ApplyDefaultSortWithWarning` all keep their existing signatures. `ApplyDefaultSort(query, ascending)` is reused as-is — it's already `private static` and already produces exactly the ordering needed for `"entrydate"`.

### Data Flow
Unchanged. `GetEntriesAsync`/`SearchEntriesAsync` → `ApplySort(query, sortBy, sortDirection, _logger)` → (lowercased `sortBy` matched in switch) → `IQueryable<JournalEntry>` with `OrderBy`/`OrderByDescending` applied → executed via `CountAsync`/`ToListAsync`. The only behavioral delta is which switch arm is taken for `"entrydate"`; the resulting `IQueryable` shape and the SQL it translates to are identical to what `ApplyDefaultSortWithWarning` already produced — only the `LogWarning` call is skipped.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Typo/case mismatch causes `"entrydate"` to still miss the new arm | Low | The switch already lowercases via `sortBy.ToLowerInvariant()` before matching, so the arm must be the literal lowercase string `"entrydate"` (no other casing needed) — copy verbatim from the spec's proposed diff. |
| A genuinely unrecognized sort key stops being flagged (over-broad fix) | Low | Not applicable here — the change adds exactly one new literal match; every other string still falls to `_` and still logs. Verified by existing test `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty`, which must keep passing unmodified. |

## Specification Amendments
None. The spec's FR-1, acceptance criteria, and Out of Scope section are correct and sufficiently scoped as written. One implementation note not called out in the spec: `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` already contains a `Mock<ILogger<JournalRepository>>`-based test harness with sibling tests (`GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty`, `..._NullSortBy_DoesNotLogWarning`, `..._EmptySortBy_DoesNotLogWarning`, `..._WhitespaceSortBy_DoesNotLogWarning`, and a `..._SortByCreatedByUsername_TiebreaksByEntryDateDesc` test covering the #2502 precedent). The new test the spec's acceptance criteria imply (`sortBy = "EntryDate"` does not log a warning) should be added to this existing file, following the same `Mock<ILogger<>>.Verify(...)` pattern as the neighboring tests — no new test file or test infrastructure is needed.

## Prerequisites
None. No migrations, config, feature flags, or infrastructure changes are required. The fix can be implemented, tested, and merged independently of any other in-flight work.
