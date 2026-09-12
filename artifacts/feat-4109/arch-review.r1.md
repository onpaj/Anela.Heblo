# Architecture Review: JournalEntry.Create() Factory Method for Domain-Owned Normalization

## Skip Design: true

This is a backend-only domain refactor: one new static method on an existing entity, one call-site change in a handler. No new or changed UI, API contract, or persisted schema. The designer phase's UX/UI sections do not apply; only its backend-only "Component Design" / "Data Schemas" format is relevant, and both sections are trivial for a change this size.

## Architectural Fit Assessment

The spec aligns cleanly with an existing, already-used convention in this codebase: static factory methods on domain entities/value objects that centralize invariant enforcement at construction time. Confirmed precedents in `backend/src/Anela.Heblo.Domain/`:
- `Catalog/MarginLevel.cs` — `public static MarginLevel Create(decimal sellingPrice, decimal totalCost, decimal levelCost)`, with a `Zero` static property as an additional named-construction affordance.
- `DataQuality/InvoiceDqtResult.cs` and `DataQuality/DqtDriftResult.cs` — same `static ... Create(...)` shape.

`JournalEntry` itself already uses the "domain method owns the invariant" pattern for every other mutation (`Update()`, `SoftDelete()`, `AssociateWithProduct()`, `ReplaceProductAssociations()`, `AssignTag()`, `ReplaceTagAssignments()`). Construction is the sole outlier — it is currently the only place a caller can produce a `JournalEntry` in an inconsistent state (untrimmed strings, un-normalized date) purely by using the public object-initializer syntax that C# classes expose by default. Adding `Create()` does not introduce a new pattern; it extends `JournalEntry` to match its own internal convention and the two other domain types that already use this exact shape.

Integration points are minimal and well-contained:
- `JournalEntry` (domain) — the only entity touched.
- `CreateJournalEntryHandler` (application) — the only caller changed.
- No repository interface change (`IJournalRepository.AddAsync` already accepts a `JournalEntry` instance, however constructed).
- No EF Core / persistence impact: `JournalEntryConfiguration` maps properties, not constructors; EF Core materializes via the parameterless constructor + property setters regardless of whether application code also uses `Create()`. Confirmed no `HasNoKey`/backing-field/constructor-binding configuration exists for `JournalEntry` that would be affected.
- No contract (DTO) change: `CreateJournalEntryRequest`/`CreateJournalEntryResponse` are untouched, consistent with this repo's DTOs-are-classes rule (not implicated here since no DTO is being modified).

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────┐        ┌──────────────────────────────────┐
│ CreateJournalEntryHandler    │        │ JournalEntry (domain)            │
│  (Application layer)         │        │  (Domain layer)                  │
│                               │        │                                  │
│  Handle(request, ct)          │──────▶│  static Create(title, content,   │
│    ...auth & title checks...  │  calls │    entryDate, userId, username,  │
│    now = DateTime.UtcNow      │        │    now) : JournalEntry           │
│    entry = JournalEntry       │        │    → trims Title/Content         │
│      .Create(request.Title,   │        │    → normalizes EntryDate.Date   │
│              request.Content, │        │    → stamps CreatedAt/ModifiedAt │
│              request.EntryDate│        │    → stamps CreatedByUserId/     │
│              userId,          │        │        CreatedByUsername         │
│              currentUser.Name │        │                                  │
│                ?? "Unknown..",│        │  (existing) Update(...)          │
│              now)              │        │  (existing) SoftDelete(...)      │
│    ...product/tag loops...    │        │  (existing) AssociateWith...     │
│    repo.AddAsync(entry)       │        └──────────────────────────────────┘
└─────────────────────────────┘
```

No new components, no new files beyond test files. `Create()` sits alongside `Update()`/`SoftDelete()` in `JournalEntry.cs` as a third invariant-owning domain operation, distinguished from the others only by being `static` (it produces the entity rather than mutating an existing instance).

### Key Design Decisions

#### Decision 1: Static factory method vs. constructor overload

**Options considered:**
- A public constructor `JournalEntry(string title, string content, DateTime entryDate, string userId, string username, DateTime now)`.
- A static factory method `JournalEntry.Create(...)` (the issue's suggested fix).

**Chosen approach:** Static factory method, exactly as the issue proposes.

**Rationale:** `MarginLevel` and `InvoiceDqtResult` both already use `static Create(...)` rather than constructor overloads for this purpose in this codebase — a constructor-based approach would be an unprecedented third convention for the same problem. A named static method also self-documents at call sites (`JournalEntry.Create(...)` reads as "construct a new, normalized entry" vs. a bare `new JournalEntry(...)` which does not visually distinguish a validating constructor from a trivial one). It also avoids fighting EF Core's expectation of a parameterless constructor for materialization — adding a *second*, parameterized public constructor is unnecessary risk for zero benefit here, whereas a static method sits entirely outside EF's constructor-selection logic.

#### Decision 2: `now` as an explicit parameter vs. `DateTime.UtcNow` inside `Create()`

**Options considered:**
- `Create()` calls `DateTime.UtcNow` internally (mirroring `Update()`/`SoftDelete()`, which do this today).
- `Create()` takes `now` as a caller-supplied parameter (the issue's suggested signature).

**Chosen approach:** Caller-supplied `now` parameter, per the issue's suggested fix — `CreatedAt` and `ModifiedAt` must be set to the *identical* instant, and the existing handler already computes `now = DateTime.UtcNow` once specifically to guarantee that. If `Create()` called `DateTime.UtcNow` twice internally (once per field) there is a theoretical, if narrow, risk of `CreatedAt != ModifiedAt` by a few ticks — a regression versus current handler behavior, which explicitly reuses one `now` value for both.

**Rationale / consistency note:** This makes `Create()` slightly inconsistent with `Update()`/`SoftDelete()`, which do call `DateTime.UtcNow` directly rather than taking it as a parameter. This inconsistency is deliberate and scoped: `Update()`/`SoftDelete()` only set one timestamp field each (`ModifiedAt`, or `ModifiedAt`+`DeletedAt` from the same call), so there is no dual-field consistency risk for them to begin with. `Create()` must set two fields (`CreatedAt`, `ModifiedAt`) to the same value, which is exactly the case the parameter avoids miscoordinating. Do not "fix" this asymmetry by also converting `Update()`/`SoftDelete()` to take a `now` parameter — that is out of scope for this issue and would be unrelated scope creep (see spec's Out of Scope).

#### Decision 3: What `Create()` does *not* set

**Chosen approach:** `Create()` only sets the seven fields enumerated in the issue's suggested fix (`Title`, `Content`, `EntryDate`, `CreatedAt`, `ModifiedAt`, `CreatedByUserId`, `CreatedByUsername`). It does not touch `Id` (DB-generated), `IsDeleted`/`DeletedAt`/`DeletedByUserId`/`DeletedByUsername` (irrelevant at creation — default `false`/`null` is already correct), `ModifiedByUserId`/`ModifiedByUsername` (no "modifier" exists yet at creation time — leaving `null` is correct and matches current handler behavior, which never sets these on the initial object initializer), or `ProductAssociations`/`TagAssignments` (populated by the handler after construction via `AssociateWithProduct`/`AssignTag`, exactly as today).

**Rationale:** Minimal, behavior-preserving scope. Setting fields `Create()` doesn't need to touch would be unrequested surface area and risks a silent behavior change (e.g., initializing `ModifiedByUserId` to something non-null would diverge from every existing `CreateJournalEntryHandlerTests` assertion built around the current object-initializer's field set).

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Two existing files are modified, one existing test file gains new test cases:

```
backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs                                          ← add Create() method
backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs  ← replace object initializer with Create() call
backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs                                     ← add Create() unit tests
```

`CreateJournalEntryHandlerTests.cs` requires no structural change — its assertions target handler-level outcomes (repository calls, response shape, error codes), which are unaffected by swapping the internal construction mechanism. Existing tests there (e.g. `Handle_WhenTitleIsWhitespaceOnly_TitleIsTrimmedBeforePersist`) continue to pass unmodified and now exercise `Create()` transitively — they remain valid as regression coverage for the refactor.

### Interfaces and Contracts

New domain-internal method signature (matches the issue's suggested fix exactly):

```csharp
public static JournalEntry Create(
    string title, string content, DateTime entryDate,
    string userId, string username, DateTime now)
```

Placement within `JournalEntry.cs`: add immediately before `Update()` (line 153 in current file), under a `// Domain methods` region that already groups `AssociateWithProduct`, `ReplaceProductAssociations`, `AssignTag`, `ReplaceTagAssignments`, `Update`, `SoftDelete`. Grouping `Create()` with these — rather than at the top near the property declarations — keeps all invariant-owning behavior visually together, matching the file's existing organization.

No change to `IJournalRepository`, no change to any controller, no change to `CreateJournalEntryRequest`/`CreateJournalEntryResponse`.

### Data Flow

Unchanged end-to-end flow; only the internal construction step changes:

1. `CreateJournalEntryHandler.Handle()` authenticates the user and validates `Title` is non-blank (unchanged).
2. `now = DateTime.UtcNow` computed once (unchanged).
3. **Changed:** `entry = JournalEntry.Create(request.Title, request.Content, request.EntryDate, userId, currentUser.Name ?? "Unknown User", now)` replaces the object-initializer block. Trimming and date normalization now happen inside `Create()`.
4. Product association and tag assignment loops run against the now-constructed `entry` (unchanged — these already call domain methods `AssociateWithProduct`/`AssignTag`).
5. `_journalRepository.AddAsync(entry, ct)` then `SaveChangesAsync(ct)` (unchanged).
6. Response construction and logging (unchanged).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A field the handler currently sets is accidentally dropped or a new one accidentally added inside `Create()`, changing persisted data | Low | `Create()`'s field list is fixed by this review (Decision 3) to exactly mirror the current object initializer; existing `CreateJournalEntryHandlerTests` assertions (e.g. on `CreatedByUserId`, `Title` after trim) act as a regression guard with zero test changes needed |
| `CreatedAt`/`ModifiedAt` diverge if `Create()` is later refactored to call `DateTime.UtcNow` internally instead of taking `now` | Low | Explicit `now` parameter (Decision 2) closes this off structurally; a code reviewer should reject any future PR that removes the parameter in favor of an internal `DateTime.UtcNow` call |
| Someone constructs a `JournalEntry` via the object initializer elsewhere in the codebase in the future, re-introducing the bypass this issue fixes | Low | Out of scope to enforce via a compiler mechanism (e.g. private setters) in this pass — the issue's suggested fix does not request that, and `Title`/`Content`/`EntryDate` setters must stay public for EF Core materialization and for `Update()`'s own field assignment pattern. Mitigate via code review convention: `Create()` is documented (this review) as the required construction path; a future arch-review pass can independently flag any new object-initializer construction of `JournalEntry` outside `Create()` and test files |
| EF Core migration/materialization interaction | Very Low | `Create()` is pure application-code construction, not involved in EF's own materialization path (which uses the parameterless constructor + property setters regardless). No `IEntityTypeConfiguration<JournalEntry>` change is needed or should be made |

## Specification Amendments

None. The spec (`spec.r1.md`) already matches this review's conclusions on field scope, the `now` parameter, and file placement — no changes needed.

## Prerequisites

None. No migration, no config, no new package reference. Implementation can start immediately against `spec.r1.md`.
