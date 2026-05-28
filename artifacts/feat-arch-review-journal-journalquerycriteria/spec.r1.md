# Specification: Remove Journal Query/Search Criteria from Domain Layer

## Summary
Eliminate `JournalQueryCriteria` and `JournalSearchCriteria` from the Domain layer. These types carry application/infrastructure concerns (pagination, sort directives, validation attributes) that pollute the domain model, duplicate the existing `GetJournalEntriesRequest`/`SearchJournalEntriesRequest` MediatR contracts, and force handlers to perform pointless field-copy translations. The repository interface in Domain will accept primitive query parameters directly, and handlers will pass values from the Application Request straight through.

## Background
A daily architecture review flagged a Clean Architecture violation in the Journal module:

- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs` and `JournalSearchCriteria.cs` live in the Domain layer.
- Their fields are exclusively Application/infrastructure concerns: `PageNumber`, `PageSize`, `SortBy`, `SortDirection`, `SearchText`, `DateFrom`, `DateTo`, `ProductCodePrefix`, `TagIds`, `CreatedByUserId`.
- `JournalSearchCriteria` carries `[MaxLength(200)]` and `[MaxLength(100)]` data annotations — validation concerns, not domain invariants — which pulls `System.ComponentModel.DataAnnotations` into Domain purely for query plumbing.
- The fields duplicate the already-existing Application contracts `GetJournalEntriesRequest` and `SearchJournalEntriesRequest`, which MediatR handlers already receive.
- `GetJournalEntriesHandler` and `SearchJournalEntriesHandler` exist in part to copy fields from the Request into the Criteria — a translation step with no semantic value.

Per `docs/architecture/development_guidelines.md`, the Domain layer must not depend on Application or Infrastructure. Pagination, sort, and validation are query-plumbing concerns that belong in the Application layer (or expressed as plain method parameters at the repository boundary). Removing the Criteria types restores layer purity, deletes duplicated code, and eliminates the dead translation step.

## Functional Requirements

### FR-1: Delete Domain query criteria types
Remove the two files and any references to them from the Domain layer.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs` is deleted.
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs` is deleted.
- `grep -r "JournalQueryCriteria\|JournalSearchCriteria"` over `backend/src` returns no matches.
- The `Anela.Heblo.Domain` project no longer references `System.ComponentModel.DataAnnotations` solely on behalf of these types (verify the using/package is unused before removing).

### FR-2: Redefine `IJournalRepository` query signatures with primitive parameters
The repository interface in Domain must expose `GetEntriesAsync` and `SearchEntriesAsync` using primitive parameters that name domain-relevant inputs explicitly. No reference to Application contracts.

**Acceptance criteria:**
- `IJournalRepository.GetEntriesAsync` signature:
  ```csharp
  Task<PagedResult<JournalEntry>> GetEntriesAsync(
      int pageNumber,
      int pageSize,
      string sortBy,
      string sortDirection,
      CancellationToken cancellationToken = default);
  ```
- `IJournalRepository.SearchEntriesAsync` signature accepts pagination, sort, and filter parameters as named arguments:
  ```csharp
  Task<PagedResult<JournalEntry>> SearchEntriesAsync(
      string? searchText,
      DateTime? dateFrom,
      DateTime? dateTo,
      string? productCodePrefix,
      IReadOnlyCollection<int>? tagIds,
      string? createdByUserId,
      int pageNumber,
      int pageSize,
      string sortBy,
      string sortDirection,
      CancellationToken cancellationToken = default);
  ```
- The Domain project has no `using` reference to `Anela.Heblo.Application` namespaces.
- `IJournalRepository` continues to derive from `IRepository<JournalEntry, int>` (Xcc abstraction unchanged).
- Other repository methods (`GetEntriesByProductAsync`, `GetJournalIndicatorsAsync`, base CRUD) are not modified.

### FR-3: Update `JournalRepository` (Persistence) implementation
The EF Core implementation in `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` must implement the new interface signatures using the same query logic that currently operates on the Criteria objects.

**Acceptance criteria:**
- Sorting behavior preserved: `sortBy` values `"title"`, `"createdat"`, and the default `"entrydate"` branch behave exactly as today (case-insensitive match on `sortBy?.ToLower()`).
- `sortDirection == "ASC"` produces ascending order; any other value (including `"DESC"`, null, or empty) produces descending order — matching current behavior.
- Filter behavior preserved:
  - `searchText`: trimmed, lower-cased, matched as `Contains` against `Title` and `Content`.
  - `dateFrom` / `dateTo`: applied as `>= Date` / `<= Date` against `EntryDate`.
  - `productCodePrefix`: kept as `criteria.ProductCodePrefix.StartsWith(pa.ProductCodePrefix)` — same direction as current code.
  - `tagIds`: applied when non-null and non-empty.
  - `createdByUserId`: equality filter when non-empty.
  - `IsDeleted == false` and the `ProductAssociations` / `TagAssignments` / `Tag` includes are unchanged.
- `PagedResult<JournalEntry>` is populated identically: `Items`, `TotalCount`, `PageNumber`, `PageSize`.

### FR-4: Simplify `GetJournalEntriesHandler`
The handler must call the repository directly using fields from `GetJournalEntriesRequest`, removing the intermediate `JournalQueryCriteria` allocation.

**Acceptance criteria:**
- `GetJournalEntriesHandler.Handle` contains no construction of `JournalQueryCriteria`.
- The handler invokes `_journalRepository.GetEntriesAsync(request.PageNumber, request.PageSize, request.SortBy, request.SortDirection, cancellationToken)`.
- Response shaping (`GetJournalEntriesResponse` fields) is unchanged.

### FR-5: Simplify `SearchJournalEntriesHandler`
The handler must call the repository directly using fields from `SearchJournalEntriesRequest`, removing the intermediate `JournalSearchCriteria` allocation.

**Acceptance criteria:**
- `SearchJournalEntriesHandler.Handle` contains no construction of `JournalSearchCriteria`.
- The handler invokes `_journalRepository.SearchEntriesAsync(...)` passing each `request.*` field as the corresponding parameter.
- `CreateContentPreview` / `ExtractHighlightTerms` helpers and response shaping are unchanged.

### FR-6: Update unit tests
Existing tests that mock the repository against `JournalQueryCriteria` / `JournalSearchCriteria` arguments must be updated to match the new signatures.

**Acceptance criteria:**
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` and any other test using `It.IsAny<JournalSearchCriteria>()` or `It.IsAny<JournalQueryCriteria>()` are updated to verify against the new parameter-based signatures.
- All Journal-related test classes still pass: `dotnet test --filter "FullyQualifiedName~Journal"` is green.
- Test intent is preserved: where a test previously asserted that a specific Criteria value flowed through, the new test must assert the equivalent parameter argument flows through (use `Moq.It.Is<T>(...)` matchers on the relevant parameter, or `Verify(...)` with explicit argument values).

### FR-7: External behavior unchanged
The HTTP API surface and resulting query results must be identical before and after the change.

**Acceptance criteria:**
- `GetJournalEntriesRequest` and `SearchJournalEntriesRequest` (Application contracts) keep their public properties, default values, and `[MaxLength]` annotations. They are unchanged.
- The MediatR endpoints (`GET /api/journal`, `POST /api/journal/search` or whatever routes exist — not touched here) return the same JSON shape.
- E2E smoke against the Journal list/search page shows the same results pre- and post-change.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected. The EF Core query plan is unchanged; only the data carrier between handler and repository is removed. Verify by spot-checking SQL output via EF logging for a representative search call — the generated SQL should match the previous implementation.

### NFR-2: Architectural conformance
- Domain layer must not reference `Anela.Heblo.Application` (verified by reviewing `Anela.Heblo.Domain.csproj` after the change — only existing references retained).
- Domain layer must not reference `System.ComponentModel.DataAnnotations` solely on behalf of the deleted types. (If used elsewhere in Domain it stays; if not, remove the using directives — do not modify the .csproj unless an explicit package reference becomes unused.)
- `dotnet build` succeeds with zero warnings introduced by this change.
- `dotnet format` reports clean.

### NFR-3: Test coverage
- All existing unit and integration tests touching the Journal module continue to pass.
- Coverage of the Journal module (handlers + repository methods modified) does not drop. Coverage measured by the existing test suite — no new coverage targets imposed.

### NFR-4: Backward compatibility
- Public API contracts (`GetJournalEntriesRequest`, `SearchJournalEntriesRequest`, response DTOs) remain byte-for-byte compatible.
- No frontend changes required. The auto-generated OpenAPI TypeScript client must not need regeneration as a result of this refactor (the public schema does not change).

## Data Model
No schema changes. No migrations. Affected types:

- **Deleted (Domain)**: `JournalQueryCriteria`, `JournalSearchCriteria` (transient parameter carriers, no persistence).
- **Modified (Domain)**: `IJournalRepository` — two method signatures.
- **Modified (Persistence)**: `JournalRepository.GetEntriesAsync`, `JournalRepository.SearchEntriesAsync` — implementations.
- **Modified (Application)**: `GetJournalEntriesHandler.Handle`, `SearchJournalEntriesHandler.Handle` — call sites.
- **Unchanged**: `JournalEntry`, `JournalEntryProduct`, `JournalEntryTag`, `JournalEntryTagAssignment`, `JournalIndicator`, `GetJournalEntriesRequest`, `SearchJournalEntriesRequest`, all response DTOs.

## API / Interface Design

### Repository interface (after)
```csharp
namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            string? searchText,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? productCodePrefix,
            IReadOnlyCollection<int>? tagIds,
            string? createdByUserId,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }
}
```

### Handler call site (after, GetJournalEntries)
```csharp
var result = await _journalRepository.GetEntriesAsync(
    request.PageNumber,
    request.PageSize,
    request.SortBy,
    request.SortDirection,
    cancellationToken);
```

### Handler call site (after, SearchJournalEntries)
```csharp
var result = await _journalRepository.SearchEntriesAsync(
    request.SearchText,
    request.DateFrom,
    request.DateTo,
    request.ProductCodePrefix,
    request.TagIds,
    request.CreatedByUserId,
    request.PageNumber,
    request.PageSize,
    request.SortBy,
    request.SortDirection,
    cancellationToken);
```

### HTTP API
No change. Controllers/Minimal APIs continue to bind to `GetJournalEntriesRequest` / `SearchJournalEntriesRequest`. MediatR pipeline is unchanged.

## Dependencies
- `MediatR` — unchanged.
- `Microsoft.EntityFrameworkCore` — unchanged.
- `Anela.Heblo.Xcc.Persistance.IRepository<T,TKey>` and `PagedResult<T>` — used as-is.
- No new packages.

## Out of Scope
- Renaming or restructuring `GetJournalEntriesRequest` / `SearchJournalEntriesRequest`.
- Changing the HTTP route surface, OpenAPI spec, or generated TypeScript client.
- Reworking the sorting/filtering logic itself (e.g., switching from `Contains` to full-text search) — behavior is preserved exactly.
- Database schema migrations.
- Frontend changes.
- Refactoring other repositories that may exhibit the same anti-pattern in other modules (e.g., catalog, purchase) — those are tracked as separate arch-review findings.
- Introducing a generic `PageRequest` / `SortRequest` abstraction shared across modules — YAGNI for this surgical fix.

## Assumptions
1. **Parameter-list approach is acceptable over re-introducing a parameter object.** The `SearchEntriesAsync` signature has 10 parameters plus a `CancellationToken`. This is long but explicit and free of layering issues. The alternative (introducing a Domain-layer query record limited to domain-relevant filters) is more invasive and the brief explicitly recommends the parameter-list approach as "the smallest fix."
2. **`PagedResult<T>` (in `Anela.Heblo.Xcc.Persistance`) is acceptable as a return type from a Domain interface.** It is shared infrastructure plumbing, not Application-layer code. Confirmed by inspecting existing references.
3. **The `[MaxLength]` annotations on `SearchJournalEntriesRequest` (in Application) provide adequate validation at the API boundary.** No additional validation is added in the repository — invalid input that gets past model binding (e.g., negative `PageSize`) behaves as it does today.
4. **Tests use `Moq`** (confirmed in `SearchJournalEntriesHandlerTests.cs`). Updates use `It.Is<T>(...)` and `Verify(...)` matchers consistent with the existing test style.

## Open Questions
None.

## Status: COMPLETE