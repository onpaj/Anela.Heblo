# Specification: MarketingAction Timestamp Parameter Consistency

## Summary
Refactor three domain methods on `MarketingAction` (`AssociateWithProduct`, `LinkToFolder`, `SoftDelete`) to accept a `DateTime utcNow` parameter instead of calling `DateTime.UtcNow` directly. This aligns these methods with the existing convention used by `UpdateDetails()` and the constructor, restoring deterministic testability and timestamp consistency between the entity and its handlers.

## Background
The `MarketingAction` aggregate in `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` currently uses two conflicting timestamp conventions:

- **Parameterized convention (correct):** The constructor (line 86) and `UpdateDetails()` (line 159) receive `DateTime utcNow` as a parameter.
- **Direct-call convention (incorrect):** `AssociateWithProduct` (line 108), `LinkToFolder` (line 124), and `SoftDelete` (lines 131–132) call `DateTime.UtcNow` inline.

This inconsistency causes three concrete problems:

1. **Untestable timestamps.** Unit tests for the three affected methods cannot assert exact timestamp values — only that they fall in a tolerance window — making the tests fragile.
2. **Ambiguous convention.** A developer adding a new domain method has no clear signal which pattern to follow.
3. **Handler/entity drift.** `DeleteMarketingActionHandler` captures `var now = DateTime.UtcNow;` and uses it for downstream operations (e.g., Outlook sync). The entity's internal call to `DateTime.UtcNow` produces a different value (microseconds to milliseconds later), introducing subtle discrepancies between the action's timestamps and the handler's `now`.

Identified by the daily architecture review routine on 2026-06-07.

## Functional Requirements

### FR-1: `AssociateWithProduct` accepts `utcNow` parameter
Change the signature of `MarketingAction.AssociateWithProduct` to accept `DateTime utcNow` and use that value for the `CreatedAt` field on the newly added `MarketingActionProduct`.

**Acceptance criteria:**
- Method signature: `public void AssociateWithProduct(string productCode, DateTime utcNow)`.
- `MarketingActionProduct.CreatedAt` is assigned from the `utcNow` parameter (not `DateTime.UtcNow`).
- No other behavior changes (normalization, duplicate-prevention, validation logic unchanged).
- A unit test asserts that, given a fixed `utcNow`, the resulting association's `CreatedAt` equals that value exactly.

### FR-2: `LinkToFolder` accepts `utcNow` parameter
Change the signature of `MarketingAction.LinkToFolder` to accept `DateTime utcNow` and use it for the `CreatedAt` field on the newly added `MarketingActionFolderLink`.

**Acceptance criteria:**
- Method signature: `public void LinkToFolder(string folderKey, MarketingFolderType folderType, DateTime utcNow)`.
- `MarketingActionFolderLink.CreatedAt` is assigned from the `utcNow` parameter.
- No other behavior changes.
- A unit test asserts that, given a fixed `utcNow`, the resulting folder link's `CreatedAt` equals that value exactly.

### FR-3: `SoftDelete` accepts `utcNow` parameter
Change the signature of `MarketingAction.SoftDelete` to accept `DateTime utcNow` and use it for both `DeletedAt` and `ModifiedAt`.

**Acceptance criteria:**
- Method signature: `public void SoftDelete(string userId, string username, DateTime utcNow)`.
- Both `DeletedAt` and `ModifiedAt` are assigned from the `utcNow` parameter, ensuring they are identical (not two separate `DateTime.UtcNow` reads).
- `IsDeleted`, `DeletedBy`, and audit fields populated as before.
- A unit test asserts that, given a fixed `utcNow`, both `DeletedAt` and `ModifiedAt` equal that value exactly and equal each other.

### FR-4: Call sites updated to pass captured `now`
Every handler that calls the three affected methods must pass its already-captured `now` value (typically `var now = DateTime.UtcNow;` at the top of the handler) into the domain method.

**Acceptance criteria:**
- All callers of `AssociateWithProduct`, `LinkToFolder`, and `SoftDelete` compile after the signature change.
- Each caller passes a single `now`/`utcNow` value through. If a caller does not yet capture one, add `var now = DateTime.UtcNow;` at the top of the handler's `Handle` method and use it consistently for both the domain call and any downstream operations (e.g., Outlook sync).
- Specifically: `DeleteMarketingActionHandler` passes the same `now` to `SoftDelete` and to the Outlook sync path, eliminating the previous millisecond drift.

### FR-5: No remaining `DateTime.UtcNow` references in `MarketingAction.cs`
After the refactor, the file `MarketingAction.cs` contains no direct calls to `DateTime.UtcNow` or `DateTime.Now`.

**Acceptance criteria:**
- A grep for `DateTime.UtcNow` and `DateTime.Now` in `MarketingAction.cs` returns zero matches.
- All timestamp assignments in the file derive from a method parameter.

## Non-Functional Requirements

### NFR-1: Backward Compatibility
This is a domain-internal refactor with no external API surface change. The OpenAPI contract, request/response DTOs, and HTTP endpoints are unchanged. No client regeneration required.

### NFR-2: Testability
After the change, unit tests for the three methods MUST be able to assert exact timestamp equality without time-based tolerance windows.

### NFR-3: Determinism
For a given input including `utcNow`, the three methods must produce byte-identical output. No internal clock reads remain.

### NFR-4: Performance
No measurable performance impact (one fewer `DateTime.UtcNow` syscall per invocation; effectively neutral).

### NFR-5: Validation
Validation must pass before completion: `dotnet build`, `dotnet format`, and all touched unit tests green.

## Data Model
No database schema changes. The `MarketingActionProduct.CreatedAt`, `MarketingActionFolderLink.CreatedAt`, `MarketingAction.DeletedAt`, and `MarketingAction.ModifiedAt` columns are unchanged — only the source of the values written to them changes.

## API / Interface Design

### Domain method signatures (before → after)

| Method | Before | After |
|---|---|---|
| `AssociateWithProduct` | `void AssociateWithProduct(string productCode)` | `void AssociateWithProduct(string productCode, DateTime utcNow)` |
| `LinkToFolder` | `void LinkToFolder(string folderKey, MarketingFolderType folderType)` | `void LinkToFolder(string folderKey, MarketingFolderType folderType, DateTime utcNow)` |
| `SoftDelete` | `void SoftDelete(string userId, string username)` | `void SoftDelete(string userId, string username, DateTime utcNow)` |

### Handler call-site pattern
```csharp
public async Task<TResponse> Handle(TRequest request, CancellationToken ct)
{
    var now = DateTime.UtcNow;
    // ... load action ...
    action.SoftDelete(userId, username, now);      // or AssociateWithProduct / LinkToFolder
    // ... downstream operations using same `now` ...
}
```

No HTTP/REST surface change.

## Dependencies
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — primary file modified.
- All MediatR handlers in the Marketing feature slice that invoke the three methods, including (non-exhaustive):
  - `DeleteMarketingActionHandler` (explicitly mentioned in the brief).
  - Handlers that associate a product with a marketing action.
  - Handlers that link a marketing action to a folder.
- Existing unit tests for `MarketingAction` and the affected handlers — assertions must be tightened to exact-equality on timestamps where applicable.

No new NuGet packages, services, or infrastructure dependencies.

## Out of Scope
- Introducing an `IClock`/`ISystemClock`/`TimeProvider` abstraction. The codebase's established convention is to capture `DateTime.UtcNow` at the handler boundary and pass it as a parameter; this refactor only enforces that existing convention.
- Refactoring any other aggregate or domain method outside `MarketingAction`.
- Database migrations, OpenAPI/TypeScript client regeneration, or UI changes.
- Changing the semantics of `ModifiedAt`/`DeletedAt`/`CreatedAt` (e.g., setting `ModifiedAt` on soft-delete is preserved as-is).
- Adding new unit tests beyond those required to validate the refactor.

## Open Questions
None.

## Status: COMPLETE