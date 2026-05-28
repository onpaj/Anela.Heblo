## Module
Journal

## Finding
`JournalQueryCriteria` and `JournalSearchCriteria` are defined in the Domain layer:

- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalQueryCriteria.cs`
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalSearchCriteria.cs`

Neither type is a domain concept. Their fields are exclusively Application/infrastructure concerns:

- **Pagination**: `PageNumber`, `PageSize`
- **Sort directives**: `SortBy` (string), `SortDirection` (string)
- **Text search**: `SearchText` (with `[MaxLength(200)]` data annotation)
- **Filter params**: `DateFrom`, `DateTo`, `ProductCodePrefix`, `TagIds`, `CreatedByUserId`

The `[MaxLength]` data annotation on `SearchJournalSearchCriteria.SearchText` (line 10) is particularly telling — it's a validation concern, not a domain invariant. Domain entities enforce business rules through behaviour; they do not carry query-plumbing parameters.

Additionally, these two types almost entirely duplicate the already-existing Application contracts `GetJournalEntriesRequest` and `SearchJournalEntriesRequest`, which carry the same fields and are the types MediatR handlers already receive. The handlers in `GetJournalEntriesHandler` and `SearchJournalEntriesHandler` exist solely to copy fields from the Request into the Criteria — a pointless translation step.

## Why it matters
Per `docs/architecture/development_guidelines.md`:
> Domain layer must NOT depend on Application or Infrastructure

Placing pagination/sort/filter DTOs in Domain pollutes the domain model with application-layer plumbing. It also means the Domain project pulls in `System.ComponentModel.DataAnnotations` purely for a query parameter — a coupling that has nothing to do with domain invariants.

## Suggested fix
1. Delete `JournalQueryCriteria` and `JournalSearchCriteria` from the Domain layer.
2. Update `IJournalRepository` (Domain) to accept the existing Application contracts directly, **or** (cleaner) define minimal interfaces in Domain that only expose the three domain-relevant filter concepts (product code prefix, soft-delete flag, date range) and let the repository implementation translate from the Application Request internally.
3. Remove the now-trivial mapping step in `GetJournalEntriesHandler` and `SearchJournalEntriesHandler`.

The smallest fix is option 2: accept the Application Request types directly in the Application-layer handler, eliminate the Criteria objects entirely, and pass the filter values through to the repository method as parameters.

---
_Filed by daily arch-review routine on 2026-05-27._