# Architecture Review: MarketingAction Timestamp Parameter Consistency

## Skip Design: true

Backend-only refactor. No UI components, screens, or visual elements touched.

## Architectural Fit Assessment

The proposed change strengthens an **already-established convention** in this codebase: capture `DateTime.UtcNow` once at the application/handler boundary and thread it through as a parameter into domain methods. The convention is already followed by `MarketingAction.UpdateDetails`, `MarketingAction.MarkOutlookSynced`, and the constructor. `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` already capture `var now = DateTime.UtcNow;` and pass it through — they simply fail to forward it to `AssociateWithProduct` / `LinkToFolder`.

Integration points are narrow and contained to the Marketing slice:

- **Domain**: `MarketingAction.cs` (one file, three method signatures)
- **Application**: `CreateMarketingActionHandler`, `UpdateMarketingActionHandler` (already capture `now`, just forward it)
- **Persistence**: `MarketingActionRepository.DeleteSoftAsync` — this is the architectural surprise the brief glosses over. `SoftDelete` is **not** called from a handler directly; it is called from the repository's `DeleteSoftAsync`. The handler invokes `_repository.DeleteSoftAsync(id, userId, username, ct)` and never captures `now`. The brief's claim that "DeleteMarketingActionHandler captures `var now = DateTime.UtcNow;` and uses it for Outlook sync" is **factually incorrect** for the current code — the handler captures no `now` and Outlook delete only needs the event ID, not a timestamp.

This does not invalidate the refactor, but it forces an extra design choice the spec must address: who captures `now` for the delete path, and how does it reach `SoftDelete`?

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────┐
│  CreateMarketingActionHandler       │  now := DateTime.UtcNow
│  UpdateMarketingActionHandler       │  ────────────────────────┐
│  DeleteMarketingActionHandler  (NEW)│  ────────────────────────┤
└─────────────────────────────────────┘                          │
              │                                                  │
              ▼ (Create/Update: direct call)                     ▼
  action.AssociateWithProduct(code, now)              ┌──────────────────────────┐
  action.LinkToFolder(key, type, now)                 │ IMarketingActionRepo     │
  action.UpdateDetails(..., now)                      │  .DeleteSoftAsync        │
                                                      │   (id, uid, uname, NOW)  │
                                                      └──────────────┬───────────┘
                                                                     │
                                                                     ▼
                                                       entity.SoftDelete(uid, uname, now)
              │
              ▼
  ┌─────────────────────────────────────────────────────────────┐
  │  MarketingAction (domain entity) — pure, deterministic       │
  │   • No DateTime.UtcNow reads anywhere in this file           │
  │   • Every timestamp field derives from a parameter           │
  └─────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: How `utcNow` reaches `SoftDelete` through the repository
**Options considered:**
- **A. Add `DateTime utcNow` to `IMarketingActionRepository.DeleteSoftAsync` signature.** Handler captures `now`, repository forwards it to `entity.SoftDelete(userId, username, now)`.
- **B. Inline the soft-delete in the handler.** Remove `DeleteSoftAsync` from the repository; handler does `entity = await GetByIdAsync(...); entity.SoftDelete(uid, uname, now); await UpdateAsync(entity); await SaveChangesAsync();`.
- **C. Repository captures its own `now` internally.** Keep the public signature unchanged; `DeleteSoftAsync` calls `var now = DateTime.UtcNow;` and passes it down.

**Chosen approach: A — extend the repository signature.**

**Rationale:**
- Preserves the repository encapsulation of "load → mutate → persist" for the soft-delete operation (matches how `Create`/`Update` keep persistence in the handler but delegate the soft-delete sequence to the repository).
- Honors the project convention that **time is captured at the application boundary, not inside infrastructure code**. Option C violates that convention by re-introducing a clock read in `Persistence` — exactly the antipattern this refactor exists to remove.
- Option B is a larger change with no real win: it duplicates the "load → soft-delete → save" sequence at every caller and breaks parallelism with `CreateAsync`/`UpdateAsync`/`AddAsync` on the same repository.
- A leaves the `DeleteSoftAsync` contract honest: the timestamp written to the row is the caller's responsibility.

#### Decision 2: `DeleteMarketingActionHandler` captures `now` at the top of `Handle`
**Chosen approach:** Insert `var now = DateTime.UtcNow;` immediately after the auth check (mirroring `CreateMarketingActionHandler:47` and `UpdateMarketingActionHandler:59`), and pass it to `DeleteSoftAsync`.

**Rationale:** Keeps the handler self-consistent with siblings. Even though the delete path has no current downstream use for `now` other than the soft-delete itself, future additions (audit events, telemetry, retry-after-now metadata) inherit a single source of time.

#### Decision 3: No `IClock` / `TimeProvider` abstraction is introduced
**Chosen approach:** Follow the spec's Out-of-Scope clause. The handler-captures-`now` convention is the established pattern; do not introduce `TimeProvider` or `ISystemClock` in this PR.

**Rationale:** Single-purpose refactor. Adding a clock abstraction is a separate architectural decision affecting every slice and is explicitly out of scope.

#### Decision 4: Domain-internal `DateTime.UtcNow` is forbidden going forward
**Chosen approach:** Treat the FR-5 grep gate (zero `DateTime.UtcNow` / `DateTime.Now` matches in `MarketingAction.cs`) as the architectural invariant for this entity. Note in commit message that any future `MarketingAction` method must accept `utcNow` as a parameter.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits limited to:

```
backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs
backend/src/Anela.Heblo.Domain/Features/Marketing/IMarketingActionRepository.cs          (signature)
backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs               (impl)
backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs
backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs

backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs   (signature, add CreatedAt-equality test)
backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionLinkToFolderTests.cs           (NEW or add to existing)
backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionSoftDeleteTests.cs             (NEW)
backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs     (verify-call signature update)
```

`MarketingActionTestBuilder.cs` requires no change — it does not invoke any of the three refactored methods.

### Interfaces and Contracts

**Domain — `MarketingAction.cs`:**
```csharp
public void AssociateWithProduct(string productCode, DateTime utcNow);
public void LinkToFolder(string folderKey, MarketingFolderType folderType, DateTime utcNow);
public void SoftDelete(string userId, string username, DateTime utcNow);
```

**Repository contract — `IMarketingActionRepository.cs`:**
```csharp
Task DeleteSoftAsync(
    int id,
    string userId,
    string username,
    DateTime utcNow,                       // NEW
    CancellationToken cancellationToken = default);
```

**Handler call pattern (all three handlers must converge on this):**
```csharp
var now = DateTime.UtcNow;
// ... domain mutations / repository call all receive `now` ...
```

No HTTP/OpenAPI contract changes. No DTO changes. No client regeneration.

### Data Flow

**Create (already-correct + new propagation):**
```
Handler captures now → new MarketingAction(..., now)
                    → action.AssociateWithProduct(code, now)     ← change
                    → action.LinkToFolder(key, type, now)        ← change
                    → action.MarkOutlookSynced(eventId, now)     (already correct)
                    → repository.AddAsync + SaveChangesAsync
```

**Update (already-correct + new propagation):**
```
Handler captures now → action.UpdateDetails(..., now)            (already correct)
                    → action.MarkOutlookSynced(eventId, now)     (already correct)
                    → action.AssociateWithProduct(code, now)     ← change
                    → action.LinkToFolder(key, type, now)        ← change
```

**Delete (new structure):**
```
Handler captures now → outlookSync.DeleteEventAsync(eventId)     (no timestamp needed)
                    → repository.DeleteSoftAsync(id, uid, uname, now)
                            └─ entity.SoftDelete(uid, uname, now)
                            └─ DeletedAt == ModifiedAt == now  (guaranteed identical)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `IMarketingActionRepository.DeleteSoftAsync` is a public contract — adding a required parameter breaks any caller not enumerated in the spec. | LOW | Grep confirms only one caller (`DeleteMarketingActionHandler`) and one test fixture (`DeleteMarketingActionHandlerTests`). Verify with `grep -r "DeleteSoftAsync"` across `backend/` before merging. |
| Brief incorrectly describes the delete handler's current state (claims it captures `now` and uses it for Outlook sync — it does not). Spec FR-4 inherits the same inaccuracy. | MEDIUM | Spec amendment below. Implementation must add the `var now = DateTime.UtcNow;` capture as a new line; do not search for an existing one. |
| Existing `MarketingActionAssociateWithProductTests` tests use the old single-arg signature in 8+ places — non-compile until updated. | LOW | Treat as a one-shot mechanical update: add a fixed `private static readonly DateTime UtcNow = new(2026, 1, 1, …, DateTimeKind.Utc);` field, pass it to every call. |
| Other entities (`JournalEntry.AssociateWithProduct`, `JournalEntry.SoftDelete`) have identical names — tempting to refactor them too and exceed scope. | LOW | Spec explicitly bounds scope to `MarketingAction`. Leave `JournalEntry` untouched; mention it in a follow-up note if desired. |
| `DateTimeKind` ambiguity — handlers use `DateTime.UtcNow` (Kind=Utc); existing entity reads were also UTC. Test fixtures must use `DateTimeKind.Utc` to match. | LOW | Mirror `MarketingActionTestBuilder`'s pattern: `new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`. |
| EF Core column types (`CreatedAt`, `DeletedAt`, `ModifiedAt`) are unchanged — no migration needed. | NONE | No action. |

## Specification Amendments

1. **FR-4 must be reworded to remove the false premise about `DeleteMarketingActionHandler`.** The current handler does **not** capture `var now = DateTime.UtcNow;` and uses no timestamp for Outlook sync (it only passes `OutlookEventId`). The amended FR-4 should read:

   > **FR-4: Call sites updated to pass captured `now`.**
   > - `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` already capture `var now = DateTime.UtcNow;` — they must forward this value to `AssociateWithProduct(...)` and `LinkToFolder(...)` calls.
   > - `DeleteMarketingActionHandler` **does not currently call `SoftDelete` directly** — it invokes `IMarketingActionRepository.DeleteSoftAsync(id, userId, username, ct)`. This refactor extends `IMarketingActionRepository.DeleteSoftAsync` to accept `DateTime utcNow`, and the handler captures `var now = DateTime.UtcNow;` after the auth check and passes it through.
   > - `MarketingActionRepository.DeleteSoftAsync` forwards `utcNow` to `entity.SoftDelete(userId, username, utcNow)`. No `DateTime.UtcNow` reads remain in `MarketingActionRepository.cs` either (verify with grep on the file).

2. **FR-5 should additionally assert** that `MarketingActionRepository.DeleteSoftAsync` contains no direct `DateTime.UtcNow` reads (the entity refactor would otherwise be sidestepped by a clock read one layer up).

3. **Acceptance criterion add-on for FR-3**: include a regression test asserting `DeletedAt == ModifiedAt` (reference-equal `DateTime` value). The current implementation's two separate `DateTime.UtcNow` reads make this an observable bug, not just a theoretical one.

4. **Out of Scope clarification**: `JournalEntry.AssociateWithProduct` / `JournalEntry.SoftDelete` have the same shape but are explicitly **not** in scope.

## Prerequisites

None. This is a pure source-level refactor:

- No database migrations.
- No new NuGet packages.
- No new DI registrations.
- No config / Key Vault / feature-flag changes.
- No OpenAPI regeneration; no TypeScript client regeneration.
- No infrastructure or environment work.

Implementation can begin immediately. Validation gates per `CLAUDE.md`: `dotnet build` and `dotnet format` clean, plus all touched unit tests green.