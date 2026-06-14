I have enough context. Writing the architecture review now.

```markdown
# Architecture Review: Encapsulate JournalEntry update in domain method

## Skip Design: true

Backend-only refactor. No UI/UX surface area, no new visual components, no public API contract change. Design phase is not required.

## Architectural Fit Assessment

This change aligns cleanly with the established conventions of this codebase:

- **Clean Architecture / Vertical Slice.** `JournalEntry` lives in `Anela.Heblo.Domain/Features/Journal/`. The Application layer (`UpdateJournalEntryHandler`) orchestrates use cases via MediatR. Moving audit-trail bookkeeping from the handler into the entity *strengthens* this separation — currently the handler reaches across the boundary to mutate audit fields, which violates the "behaviour belongs in entities" rule explicitly called out in `docs/architecture/development_guidelines.md:241` ("Don't create anemic domain models — Put behaviour in entities").
- **Mirrors `SoftDelete`.** `JournalEntry.SoftDelete(string userId, string username)` at `JournalEntry.cs:153` already encapsulates the same audit-trail concern for deletion. Adding `Update` adjacent to it gives a single, discoverable home for "what happens when someone modifies a journal entry."
- **No friction with persistence.** The `JournalEntryConfiguration` (EF Core) maps the same six fields the new method will set. EF change tracking will pick up the mutations the same way it does today. No migration, no schema change, no repository contract change.
- **One omission to be aware of.** The handler at `UpdateJournalEntryHandler.cs:61-62` also calls `entry.ReplaceProductAssociations(request.AssociatedProducts)` and `entry.ReplaceTagAssignments(request.TagIds)`. These are **already** domain methods on the entity and are correctly out of scope. The new `Update` method MUST NOT touch product/tag collections — those remain as separate calls in the handler. The spec implicitly assumes this but does not state it; see Specification Amendments below.

The main integration points are exactly two files: `JournalEntry.cs` (add method) and `UpdateJournalEntryHandler.cs` (replace block at lines 51–59 with a single call). No other module, controller, contract, validator, or repository is affected.

## Proposed Architecture

### Component Overview

```
HTTP Controller
      │
      ▼
UpdateJournalEntryRequest ──► MediatR ──► UpdateJournalEntryHandler
                                                  │
                                                  │ 1. resolve current user
                                                  │ 2. authorization / not-found guards
                                                  │ 3. load entry via IJournalRepository
                                                  ▼
                                          JournalEntry
                                          ├── Update(title, content, entryDate, userId, username)   ◄── NEW
                                          ├── ReplaceProductAssociations(codes)
                                          └── ReplaceTagAssignments(ids)
                                                  │
                                                  ▼
                                  IJournalRepository.UpdateAsync + SaveChangesAsync
                                                  │
                                                  ▼
                                  UpdateJournalEntryResponse
```

The arrow from handler into `JournalEntry` collapses six direct property assignments into one method call. All other arrows are unchanged.

### Key Design Decisions

#### Decision 1: Method signature mirrors `SoftDelete`, not a "command object"

**Options considered:**
- (a) `Update(string? title, string content, DateTime entryDate, string userId, string username)` — five primitive parameters, parallel to `SoftDelete(string userId, string username)`.
- (b) `Update(UpdateJournalEntryCommand cmd)` — pass a dedicated command/value object from the domain layer.
- (c) `Update(request, currentUser)` — pass the MediatR request DTO and the `CurrentUser` record directly.

**Chosen approach:** (a), as specified.

**Rationale:** `SoftDelete` is the established precedent in the same aggregate. Introducing a domain-layer command object (option b) is speculative generality — there is one caller, no second use case is planned, and YAGNI applies. Option (c) would leak `Anela.Heblo.Application` types (`UpdateJournalEntryRequest`) and `Anela.Heblo.Domain.Features.Users.CurrentUser` (already in Domain, but conceptually an application concern) into a domain method, making the domain depend on knowledge of the application/HTTP shape. Primitive parameters keep the entity's API symmetric with `SoftDelete` and free of upstream coupling.

#### Decision 2: Normalisation (`Trim`, `.Date`) belongs in the domain method

**Options considered:**
- (a) Normalise in the entity (`Title = title?.Trim()`, `Content = content.Trim()`, `EntryDate = entryDate.Date`).
- (b) Keep normalisation in the handler; entity stores whatever it's given.
- (c) Move normalisation upstream into a FluentValidation rule or DTO setter.

**Chosen approach:** (a).

**Rationale:** Normalisation rules are invariants about the stored shape ("titles never carry leading/trailing whitespace", "entry dates are date-only"). Those invariants belong with the entity that owns the data. Option (b) preserves today's split-brain problem the refactor is trying to eliminate. Option (c) is plausible but is a larger architectural choice (and would have to handle validators that strip vs. validators that reject); doing it inside `Update` mirrors `NormalizeProductCode` (`JournalEntry.cs:105`) which already normalises product codes inside the entity.

#### Decision 3: Validation/invariants remain out of scope; `Update` is a *recorder*, not a *guard*

**Options considered:**
- (a) `Update` only assigns and normalises; invariants ("only original author may edit", "content non-empty", "EntryDate not in future") are handled elsewhere or added later.
- (b) Add basic guards now (e.g. `ArgumentException` on empty `content`, on `username` being null/empty).

**Chosen approach:** (a), per spec "Out of Scope".

**Rationale:** Behaviour preservation is FR-3. Today the handler does not guard against empty content (FluentValidation does that at the API boundary). Adding guards now would either be redundant with the validator or would change observable behaviour for the few code paths that bypass validation (none currently). The spec is explicit: the refactor *enables* future invariants but does not introduce them. Stay surgical.

#### Decision 4: `username` parameter is non-nullable `string`; the `?? "Unknown User"` fallback stays in the handler

**Options considered:**
- (a) `Update(..., string userId, string username)` — entity demands a non-null username; handler resolves it.
- (b) `Update(..., string userId, string? username)` — entity stores whatever the application gives it, including null.

**Chosen approach:** (a), matching `SoftDelete`'s signature.

**Rationale:** "How do we present a missing user identity?" is an application-layer policy, not a domain rule. The domain just records what it's told. This is consistent with how `DeleteJournalEntryHandler.cs:51` calls `entry.SoftDelete(currentUser.Id, currentUser.Name)`. **However, see the Risks section below — there is a pre-existing nullability inconsistency in the Delete path that the architecture review surfaces but explicitly does NOT fix in this change.**

## Implementation Guidance

### Directory / Module Structure

No new files. Two edits:

```
backend/src/Anela.Heblo.Domain/Features/Journal/
  └── JournalEntry.cs                                    [MODIFY] add Update method adjacent to SoftDelete (around line 153)

backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/
  └── UpdateJournalEntryHandler.cs                       [MODIFY] replace lines 51-59 with single call to entry.Update(...)

backend/test/Anela.Heblo.Tests/Features/Journal/
  ├── JournalEntryTests.cs                               [MODIFY] add Update domain method tests (see Test placement note)
  └── UpdateJournalEntryHandlerTests.cs                  [NEW]    handler-level tests (see Specification Amendments)
```

**Test placement note.** Domain entity tests for this codebase live under `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryTests.cs`, not under `Domain/Journal/`. (`Domain/Journal/SimpleJournalTests.cs` exists separately but is a coarser smoke test.) Follow the established convention: add `Update` tests to `Features/Journal/JournalEntryTests.cs` alongside `ReplaceProductAssociations_*` and `ReplaceTagAssignments_*`.

### Interfaces and Contracts

**New public method on `JournalEntry`:**

```csharp
public void Update(string? title, string content, DateTime entryDate, string userId, string username)
```

Contract:
- **Inputs.** `title` may be null; `content`, `userId`, `username` are non-null; `entryDate` may carry a time component (it will be stripped).
- **Outputs.** None. Mutates `Title`, `Content`, `EntryDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`.
- **Invariants preserved.** `CreatedAt`, `CreatedByUserId`, `CreatedByUsername`, `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername` are not touched.
- **Side effects.** Reads `DateTime.UtcNow` (same as `SoftDelete`).
- **Throws.** Nothing currently; `content.Trim()` would throw `NullReferenceException` if `content` were null, which is acceptable — the validator guarantees non-null at the boundary and `[Required]` is declared on the property (`JournalEntry.cs:17`). This matches `SoftDelete`'s "no guards" stance.

No changes to:
- `IJournalRepository`
- `UpdateJournalEntryRequest` / `UpdateJournalEntryResponse`
- `JournalEntryDto` / `JournalEntryMapper`
- HTTP routes, status codes, error codes
- FluentValidation rules
- OpenAPI-generated TypeScript client

### Data Flow

**Update use case (post-refactor):**

```
1. HTTP PUT /api/journal-entries/{id}
       │
       ▼
2. UpdateJournalEntryRequest (validated by FluentValidation)
       │
       ▼
3. UpdateJournalEntryHandler.Handle
   ├─ a. _currentUserService.GetCurrentUser()
   ├─ b. guard: unauthenticated → UnauthorizedJournalAccess response
   ├─ c. _journalRepository.GetByIdAsync(request.Id)
   ├─ d. guard: null → JournalEntryNotFound response
   ├─ e. entry.Update(                                  ◄── single call replaces 7 lines
   │         request.Title,
   │         request.Content,
   │         request.EntryDate,
   │         currentUser.Id,
   │         currentUser.Name ?? "Unknown User")
   ├─ f. entry.ReplaceProductAssociations(request.AssociatedProducts)   [UNCHANGED]
   ├─ g. entry.ReplaceTagAssignments(request.TagIds)                    [UNCHANGED]
   ├─ h. _journalRepository.UpdateAsync(entry)
   ├─ i. _journalRepository.SaveChangesAsync()
   └─ j. log + return UpdateJournalEntryResponse { Id, ModifiedAt = entry.ModifiedAt }
```

Step (e) is the only difference from today. Steps (f) and (g) must remain in the handler — they are separate aggregate operations with different normalisation rules.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Developer accidentally folds `ReplaceProductAssociations` / `ReplaceTagAssignments` into the new `Update` method, breaking the contract of those methods (which validate input). | MEDIUM | State explicitly in the PR description / commit message that `Update` covers only the six listed fields. Add a comment near the call site in the handler if needed (one line, only if it's non-obvious). Reviewer should diff `Update` against `SoftDelete` for structural symmetry. |
| Time-stripping changes a stored `EntryDate` for any client currently relying on a stored time component. | LOW | The current handler already does `request.EntryDate.Date` at `UpdateJournalEntryHandler.cs:56`. Behaviour is preserved. No change. |
| `DateTime.UtcNow` is read once (in `Update`) instead of once (`var now = ...` in the handler). For a single call this is irrelevant, but a unit test that asserts equality of `entry.ModifiedAt` against a captured `before/after` window must allow for tiny drift. | LOW | New unit tests should use range assertions (`should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow)`) rather than exact equality. |
| **Pre-existing nullability inconsistency.** `DeleteJournalEntryHandler.cs:51` passes `currentUser.Name` (a `string?`) directly into `SoftDelete(string userId, string username)`. Under nullable-reference-types this should emit CS8604, and at runtime the `DeletedByUsername` field can end up null while the new `Update` path falls back to `"Unknown User"`. Two paths, two policies. | LOW (existing, out of scope) | **Do NOT fix in this refactor.** Surgical-changes rule applies. Flag it in the PR description as a follow-up. A separate change should align both handlers (either both fall back to `"Unknown User"`, or both let the domain record null — the codebase needs to pick one). |
| No existing handler-level tests cover `UpdateJournalEntryHandler` (verified: no `UpdateJournalEntryHandlerTests.cs` exists). The spec FR-2 says "All existing handler-level tests continue to pass" — there are none to keep green. | MEDIUM | See Specification Amendments: add new `UpdateJournalEntryHandlerTests` modelled on `DeleteJournalEntryHandlerTests`. |
| `Title` `MaxLength(200)` and `Content` `MaxLength(10000)` are enforced only by FluentValidation and EF's column types — not by the new `Update` method. A bypass that constructs and saves an entity outside the request pipeline could exceed these limits. | LOW | Matches today's behaviour. Out of scope (consistent with the "Update enables future invariants but does not introduce them" decision). |

## Specification Amendments

The spec is sound but two items need to be tightened before implementation:

1. **State explicitly that `ReplaceProductAssociations` and `ReplaceTagAssignments` remain on the handler.** The spec lists "six fields" that the entity will now own (`Title`, `Content`, `EntryDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`). It does not say what happens to the calls at `UpdateJournalEntryHandler.cs:61-62`. Add a sentence to FR-2 / Out of Scope: *"`entry.ReplaceProductAssociations(...)` and `entry.ReplaceTagAssignments(...)` continue to be invoked from the handler; the new `Update` method does not touch product or tag collections."*

2. **Correct the "existing handler-level tests" claim.** The spec FR-2 says: *"All existing handler-level tests continue to pass without modification beyond test-double / mock setup adjustments required by the call-site change."* No such file exists (verified via `find` — `Update*` tests do not exist under `backend/test/Anela.Heblo.Tests/Features/Journal/`). Replace the sentence with: *"Add `UpdateJournalEntryHandlerTests.cs` mirroring the structure of `DeleteJournalEntryHandlerTests.cs`, covering: unauthenticated → `UnauthorizedJournalAccess`; empty `currentUser.Id` → `UnauthorizedJournalAccess`; entry not found → `JournalEntryNotFound`; valid request → repository receives entity with updated fields, audit trail populated, and product/tag associations replaced; null `currentUser.Name` → `ModifiedByUsername == "Unknown User"`."* This brings the touched code to ≥80% per NFR-4 (today it is effectively 0% for this handler).

3. **(Optional / informational only.)** Note in the spec's Background or Out of Scope that an analogous split exists for the `DeleteJournalEntryHandler` regarding the `?? "Unknown User"` fallback (see Risks). This refactor does not address it — but a future PR should align the two handlers' policies.

## Prerequisites

None. The change is self-contained:

- No database migration.
- No new packages, services, or DI registrations.
- No new configuration, secrets, or Key Vault entries.
- No infrastructure changes.
- No OpenAPI / TypeScript client regeneration is necessary (no contract change).
- No feature flag.

Implementation can start immediately once this review is approved and the spec amendments above are applied.
```