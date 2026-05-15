I have enough context. Now I will write the architecture review.

# Architecture Review: Encapsulate Replace Semantics for JournalEntry Collections

## Skip Design: true

(Pure backend refactor — no UI, no API contract change, no schema change.)

## Architectural Fit Assessment

The change is **strongly aligned with existing patterns** and is in fact a correction of a localized drift away from them:

- The codebase follows **Clean Architecture + Vertical Slice** (`Domain → Application → Persistence → API`) with MediatR handlers in `UseCases/*`. Domain entities under `Anela.Heblo.Domain/Features/Journal/` are rich models that own their invariants.
- `JournalEntry` already exposes intent-revealing methods (`AssociateWithProduct`, `AssignTag`, `SoftDelete`, `IsAssociatedWithProduct`). Adding `ReplaceProductAssociations` / `ReplaceTagAssignments` mirrors that style exactly.
- `CreateJournalEntryHandler` already routes 100% of mutations through domain methods; `UpdateJournalEntryHandler` is the lone deviation. This change restores symmetry.
- No new abstractions, DI registrations, NuGet packages, migrations, or contract changes are required. Integration points are: (a) `JournalEntry` entity, (b) `UpdateJournalEntryHandler`, (c) a new test file in `Anela.Heblo.Tests/Features/Journal/`.

**Verified against codebase reality:**
- `JournalEntryProduct` PK is composite `{JournalEntryId, ProductCodePrefix}` — DB-level uniqueness is enforced, so the in-memory dedupe is defense-in-depth, not the sole guard.
- `JournalEntryTagAssignment` PK is composite `{JournalEntryId, TagId}` — same.
- Both child entity configurations declare `CreatedAt` as `IsRequired()`. The existing `AssociateWithProduct` / `AssignTag` methods do **not** set `CreatedAt`, relying on EF/DB defaulting. The new methods must follow the same pattern (don't set it) to preserve behavioral parity — this is what the spec already prescribes.
- `IJournalRepository.GetByIdAsync` eagerly `Include`s both child collections, so `entry.ProductAssociations`/`TagAssignments` are fully materialized in the handler — no lazy-loading hazard when diffing.
- Soft delete query filter (`!x.IsDeleted`) operates only on `JournalEntries`; child collections are not filtered. The diff operates on the in-memory tracked set, which is correct.

## Proposed Architecture

### Component Overview

```
                   ┌──────────────────────────────────────────┐
                   │ UpdateJournalEntryHandler (Application)  │
                   │ ──────────────────────────────────────── │
                   │  GetByIdAsync (incl. children)           │
                   │  entry.Title/Content/EntryDate = …       │
                   │  entry.ReplaceProductAssociations(req…)  │◄── two domain calls
                   │  entry.ReplaceTagAssignments(req…)       │     replace lines 62–80
                   │  UpdateAsync + SaveChangesAsync          │
                   └──────────────────────────────────────────┘
                                      │
                                      ▼
                   ┌──────────────────────────────────────────┐
                   │ JournalEntry (Domain)                    │
                   │ ──────────────────────────────────────── │
                   │  AssociateWithProduct(code)   [existing] │
                   │  AssignTag(tagId)             [existing] │
                   │  ReplaceProductAssociations(codes) [NEW] │── set-diff:
                   │  ReplaceTagAssignments(tagIds)     [NEW] │   keep, add, remove
                   │  ProductAssociations  (ICollection)      │
                   │  TagAssignments       (ICollection)      │
                   └──────────────────────────────────────────┘
                                      │
                                      ▼
                   ┌──────────────────────────────────────────┐
                   │ EF Core Change Tracker                   │
                   │ ──────────────────────────────────────── │
                   │  Removed children → DELETE (cascade)     │
                   │  Added children   → INSERT               │
                   │  Kept children    → unchanged            │
                   └──────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Set-diff vs. clear-and-re-add

**Options considered:**
1. **Clear-and-re-add inside the domain method** — preserves the encapsulation goal but keeps EF tracking churn and discards original `CreatedAt` for unchanged children.
2. **Set-diff (compute add/remove against existing items)** — only removes children that are truly gone and only adds children that are truly new; unchanged items keep their identity and `CreatedAt`.

**Chosen approach:** Set-diff using `HashSet<T>` for both keys (normalized `ProductCodePrefix` and `TagId`).

**Rationale:**
- Preserves the historical `CreatedAt` on unchanged children (the spec calls this out explicitly).
- Generates a minimal change set for EF — only the children that actually moved produce SQL. With current sizes (single-digit / low-double-digit), the algorithmic cost (O(n+m)) is identical in practice, but the SQL footprint is strictly smaller.
- Plays nicely with the composite PK: EF cannot have two tracked entities with the same key, so clear-and-re-add in the same `SaveChanges` cycle can trigger tracking conflicts on overlapping items unless the cleared list is detached first. Set-diff sidesteps that class of bug entirely.

#### Decision 2: Where the input-normalization rules live

**Options considered:**
1. Duplicate the `Trim().ToUpperInvariant()` + whitespace guard logic inside `ReplaceProductAssociations`.
2. Extract a private static helper (e.g. `NormalizeProductCode`) and use it from both `AssociateWithProduct` and `ReplaceProductAssociations`.

**Chosen approach:** Extract one private static helper inside `JournalEntry` (e.g. `private static string NormalizeProductCode(string raw)`), used by both the existing `AssociateWithProduct` and the new `ReplaceProductAssociations`. Same guard semantics, same normalization, **one source of truth**.

**Rationale:**
- The spec's acceptance criteria require behavioral parity with `AssociateWithProduct` for trim/upper/whitespace. Two independent copies of the rule will drift. The spec lists "Refactoring `AssociateWithProduct`" as out of scope, but extracting a private helper used by both is a non-behavioral, internal refactor — it leaves the public API of `AssociateWithProduct` unchanged. This is the minimum required change to keep the rule DRY.
- If the user intends "do not touch `AssociateWithProduct` at all", fall back to duplicating the four-line normalization inline. **Recommend the helper; flag as an explicit choice for the user.**

#### Decision 3: Whitespace-guard placement

**Options considered:**
1. Pre-filter (silently drop) blanks before diffing.
2. Throw `ArgumentException` on the first whitespace-only entry, before any mutation.

**Chosen approach:** Throw `ArgumentException` **before** mutating the collection. Validate the entire incoming set in a single pass first, then apply the diff. This matches the spec's acceptance criterion ("Calling with `[" "]` throws `ArgumentException`") and preserves entity state on invalid input — no partial replacement.

**Rationale:** Domain methods that throw mid-mutation leave aggregates in inconsistent states; the handler currently has no try/catch and would propagate to MediatR's pipeline. Fail fast, atomically. This also matches the guard-first ordering used by `AssociateWithProduct`.

#### Decision 4: Tag validation (existence)

**Options considered:**
1. Validate `tagId` exists in `JournalEntryTag` before assigning.
2. Skip existence validation (current `AssignTag` behavior).

**Chosen approach:** Skip — behavioral parity with existing `AssignTag`.

**Rationale:** The spec is explicit on this. FK constraint at the DB level will reject an invalid `TagId` at `SaveChangesAsync` time; the resulting `DbUpdateException` is the existing handling path. Introducing pre-validation would expand scope and require injecting `IJournalTagRepository` into the domain entity — a Clean-Architecture violation.

#### Decision 5: Method signatures accept `IEnumerable<T>?`

**Chosen approach:** `IEnumerable<string>?` and `IEnumerable<int>?` (nullable). Null and empty are treated identically (clear all).

**Rationale:** Matches the nullable `UpdateJournalEntryRequest.AssociatedProducts` / `TagIds` shape and removes the null-check from the handler — the whole point of the refactor is to let the handler stop reasoning about the input shape.

## Implementation Guidance

### Directory / Module Structure

No new files except the test file. Modifications only:

```
backend/src/Anela.Heblo.Domain/Features/Journal/
  JournalEntry.cs                                   [MODIFY: +2 public methods, +1 private helper]

backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/
  UpdateJournalEntryHandler.cs                      [MODIFY: replace lines 61–80 with 2 calls]

backend/test/Anela.Heblo.Tests/Features/Journal/
  JournalEntryDomainTests.cs                        [NEW: xUnit + FluentAssertions]
```

This matches `docs/architecture/filesystem.md` exactly: domain entities live under `Domain/Features/{Feature}/`, handlers under `Application/Features/{Feature}/UseCases/{UseCase}/`, tests mirror at `test/Anela.Heblo.Tests/Features/{Feature}/`.

### Interfaces and Contracts

**Public surface added to `JournalEntry`:**

```csharp
public void ReplaceProductAssociations(IEnumerable<string>? productCodes);
public void ReplaceTagAssignments(IEnumerable<int>? tagIds);
```

**Internal helpers (private to `JournalEntry`):**

```csharp
private static string NormalizeProductCode(string raw); // Trim().ToUpperInvariant(), throws on whitespace-only
```

**Unchanged:**
- `IJournalRepository`, `UpdateJournalEntryRequest`, `UpdateJournalEntryResponse`, `JournalController` PUT route, all EF configurations and migrations.

### Data Flow

**Update flow (golden path):**

1. `JournalController` → MediatR → `UpdateJournalEntryHandler.Handle`.
2. `_journalRepository.GetByIdAsync(id, ct)` loads entry **with** `ProductAssociations` + `TagAssignments` + `Tag` (already configured with `Include` chains).
3. Handler updates scalar fields (`Title`, `Content`, `EntryDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`).
4. Handler calls `entry.ReplaceProductAssociations(request.AssociatedProducts)`:
   - **Validate pass**: normalize every non-null code via `NormalizeProductCode` (throws `ArgumentException` if any is whitespace).
   - **Build target set**: `HashSet<string>` of normalized codes (deduplicated, case-insensitive via the `ToUpperInvariant` normalization step).
   - **Remove**: iterate `ProductAssociations` snapshot, remove any whose `ProductCodePrefix` is not in target set.
   - **Add**: iterate target set, add a new `JournalEntryProduct { JournalEntryId = Id, ProductCodePrefix = normalized }` for any not already present.
5. Handler calls `entry.ReplaceTagAssignments(request.TagIds)` with the same algorithm keyed on `TagId`.
6. `UpdateAsync` + `SaveChangesAsync` flush diffs. EF's change tracker emits `DELETE` for removed children and `INSERT` for new ones. Unchanged children produce no SQL.
7. Return `UpdateJournalEntryResponse { Id, ModifiedAt }`.

**Failure flow — invalid product code:**

`ReplaceProductAssociations` throws `ArgumentException` before any mutation. The handler does not catch it; MediatR pipeline / ASP.NET middleware returns 500 — same behavior as the existing `AssociateWithProduct` invocation when a blank prefix is sent. No partial state.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Iterating and mutating `ProductAssociations` in the same loop throws `InvalidOperationException` | Medium | Snapshot to `var toRemove = existing.Where(...).ToList()` before calling `Remove`. Standard C# pattern; the test "disjoint set removes the old codes" will catch a violation. |
| EF change tracker conflict if two children with the same composite PK ever coexist in the tracked graph | Medium | The set-diff approach never adds a duplicate to the in-memory collection — the "already present" branch returns the existing instance untouched. Tests for the "overlap preserves instance reference" case validate this. |
| `CreatedAt` on `JournalEntryProduct`/`JournalEntryTagAssignment` is `IsRequired()` but neither old nor new construction paths set it | Low (pre-existing) | Out of scope for this refactor; spec explicitly requires parity with `AssociateWithProduct`. If `CreatedAt` is currently being populated by a value generator, `AsUtcTimestamp()`, or DB default, the new method inherits the same behavior. Flag for a follow-up issue. |
| Behavioral drift between `AssociateWithProduct` and `ReplaceProductAssociations` normalization rules | Medium | Single private `NormalizeProductCode` helper used by both. If the user vetoes touching `AssociateWithProduct`, duplicate the four lines verbatim and add a comment cross-referencing the two. |
| Future audit/validation requirements on remove operations | Low | The whole point of the refactor — the domain method becomes the single seam where future remove invariants land. |
| Test file name collision | Low | Verified no `JournalEntryDomainTests.cs` or `UpdateJournalEntryHandlerTests.cs` exists in `backend/test/Anela.Heblo.Tests/Features/Journal/`. |

## Specification Amendments

1. **Extract a private `NormalizeProductCode` helper on `JournalEntry`** (Decision 2). The spec says "Refactoring `AssociateWithProduct` itself is out of scope" — clarify that **swapping its inline normalization for a private helper call is permitted and recommended**, because it is a non-behavioral internal cleanup that prevents the DRY violation the spec otherwise creates. If the user prefers strict no-touch on `AssociateWithProduct`, duplicate the normalization inside the new method and add an explicit `// keep in sync with AssociateWithProduct` comment.

2. **Validate-then-mutate ordering** (Decision 3). The spec lists the whitespace-throws acceptance criterion but does not say at which point in the algorithm the throw happens. Specify: **all incoming items are normalized/validated in a first pass; mutation only begins if validation succeeds.** Add an acceptance criterion: "Calling with `["A", " ", "B"]` throws `ArgumentException` and leaves `ProductAssociations` unchanged" (state preservation on failure).

3. **`ReplaceTagAssignments` null-input contract**. The spec lists `null` → clear for products and tags but does not list "calling with `null` empties existing tag assignments" in the FR-2 acceptance bullets (line says it under "Behavior" but missing from the bullets). Add the explicit acceptance bullet for symmetry with FR-1.

4. **Test naming convention.** Spec proposes `JournalEntryDomainTests.cs`. Looking at the existing test layout (`GetJournalEntryHandlerTests.cs`, `JournalEntryMapperTests.cs`, `SearchJournalEntriesHandlerTests.cs`), a more idiomatic name is `JournalEntryTests.cs` (entity tests are named after the entity, not after "Domain"). Minor; either is fine.

5. **Add one new test case**: "calling `ReplaceProductAssociations` on an entry with `["X", "Y"]` and an input of `["x"]` (different case) preserves the existing `X` instance reference and removes `Y`." Pins the case-insensitive matching against the existing-instance preservation contract.

## Prerequisites

None. The refactor is self-contained:

- No DB migration (schema unchanged).
- No configuration / appsettings change.
- No infrastructure change.
- No new NuGet packages, no DI registration changes.
- No frontend change.
- No prior PR or feature must land first.

Implementation can start immediately. Validation gates per project rules: `dotnet build`, `dotnet format`, `dotnet test --filter FullyQualifiedName~Journal` must all pass; existing handler-level tests (if any are added later) must continue to pass.