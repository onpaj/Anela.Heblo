## Module
Invoices

## Finding
`IIssuedInvoiceRepository.cs` in the Domain project defines two classes that are application-level concerns, not domain concerns:

- `IssuedInvoiceFilters` (lines 101–114 of `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`): contains pagination (`PageNumber`, `PageSize`), sorting (`SortBy`, `SortDescending`), and UI-driven filter fields (`ShowOnlyUnsynced`, `ShowOnlyWithErrors`)
- `PaginatedResult<T>` (lines 116–127): a generic pagination wrapper with computed properties like `TotalPages`, `HasNextPage`, `HasPreviousPage`

These classes express the UI query model and application-layer pagination concerns. The Domain layer should only define repository contracts and domain entities — it should be agnostic of how callers page or sort results.

## Why it matters
Clean Architecture requires the Domain layer to have zero knowledge of application or infrastructure concerns. By placing these classes in the Domain project:
- The Domain project accumulates application-level vocabulary (`PageNumber`, `ShowOnlyUnsynced`) that has no business meaning
- If multiple modules adopt `PaginatedResult<T>`, they are forced to take a dependency on the Invoices domain to access a generic utility
- Any future move toward module-specific DbContexts becomes harder because the query model is entangled with the domain interface

## Suggested fix
Move `IssuedInvoiceFilters` and `PaginatedResult<T>` to the Application layer, e.g.:
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs`
- `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` (it is generic and could be shared across modules in Application, not Domain)

Update `IIssuedInvoiceRepository` to reference them from their new location.

---
_Filed by daily arch-review routine on 2026-05-29._