# Specification: Remove Unused `ILotsClient` Dependency from `GetCatalogDetailHandler`

## Summary
Remove the unused `ILotsClient` constructor dependency and `_lotsClient` field from `GetCatalogDetailHandler` in the Catalog module. The handler already sources lot data from the `CatalogAggregate` cache, making the injection dead code that inflates the constructor signature and misleads readers about the handler's data flow.

## Background
The arch-review routine (filed 2026-05-30) identified that `GetCatalogDetailHandler` (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`) declares `ILotsClient` as a constructor dependency but never invokes any of its methods. Lots are populated from `catalogItem.Stock.Lots` (sourced from the catalog cache aggregate) rather than fetched on-demand from the lots client.

This is a YAGNI violation: the unused dependency adds noise to the constructor signature, complicates DI wiring, and obscures the actual data flow. It also delays detection of a missing `ILotsClient` registration if the DI binding were ever removed elsewhere. The fix is a pure refactor with no behavioral change.

## Functional Requirements

### FR-1: Remove `ILotsClient` from handler constructor
The `GetCatalogDetailHandler` constructor must no longer accept an `ILotsClient` parameter. The corresponding private readonly field `_lotsClient` must be removed.

**Acceptance criteria:**
- The `ILotsClient` parameter is removed from the `GetCatalogDetailHandler` constructor signature.
- The `_lotsClient` private readonly field is removed from the class.
- No `using` directives or namespace imports orphaned by the removal remain in the file.
- The handler continues to compile and produce identical output for all `GetCatalogDetailRequest` inputs.

### FR-2: Preserve existing lot data behavior
Lots in the response must continue to be sourced from `catalogItem.Stock.Lots` via the `CatalogAggregate` cache. No new data fetching is introduced.

**Acceptance criteria:**
- `catalogItemDto.Lots` is still populated from `catalogItem.Stock.Lots.Select(...)` when `catalogItem.HasLots` is true.
- No new calls to `ILotsClient` or any other client are added.
- Response shape and content for `GetCatalogDetailResponse` are unchanged.

### FR-3: Update unit tests if they reference the removed parameter
Any unit tests that instantiate `GetCatalogDetailHandler` directly and pass a mocked `ILotsClient` must be updated to match the new constructor signature.

**Acceptance criteria:**
- All existing unit tests for `GetCatalogDetailHandler` continue to pass.
- Test setup code no longer constructs or mocks `ILotsClient` solely for this handler.
- Mocks for `ILotsClient` that are still required for other handlers/tests remain untouched.

### FR-4: Leave DI registration of `ILotsClient` intact
The `ILotsClient` interface and its DI registration must remain in place for other consumers. Only the unused injection point in `GetCatalogDetailHandler` is removed.

**Acceptance criteria:**
- The DI registration for `ILotsClient` (wherever it lives in the module wiring) is unchanged.
- Other handlers/services that legitimately depend on `ILotsClient` continue to resolve their dependency.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. Removing an unused constructor parameter slightly reduces DI resolution work but is not measurable.

### NFR-2: Security
No security implications. No authentication, authorization, or data-handling code paths are touched.

### NFR-3: Maintainability
The change improves readability and reduces cognitive load for future readers of the handler. The constructor signature should reflect only dependencies the handler actually uses.

### NFR-4: Backward compatibility
No public API or contract changes. The MediatR request/response DTOs (`GetCatalogDetailRequest`, `GetCatalogDetailResponse`) are not modified.

## Data Model
No data model changes. Lot data continues to flow from `CatalogAggregate.Stock.Lots` through the existing cache pipeline.

## API / Interface Design
No external API changes. The MediatR handler signature and its request/response contracts are unchanged. Only the internal constructor of `GetCatalogDetailHandler` is modified — an implementation detail not exposed outside the application layer.

**Before:**
```csharp
public GetCatalogDetailHandler(
    ICatalogRepository catalogRepository,
    ILotsClient lotsClient,
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<GetCatalogDetailHandler> logger)
```

**After:**
```csharp
public GetCatalogDetailHandler(
    ICatalogRepository catalogRepository,
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<GetCatalogDetailHandler> logger)
```

## Dependencies
- **`ICatalogRepository`** — already injected, continues to provide `CatalogAggregate` data.
- **`IMapper`** (AutoMapper) — unchanged.
- **`TimeProvider`** — unchanged.
- **`ILogger<GetCatalogDetailHandler>`** — unchanged.
- **Existing tests** — must be updated if they mock `ILotsClient` for this handler.

## Out of Scope
- Removing `ILotsClient` registration from the DI container (it is used by other consumers).
- Refactoring the lots fetching strategy or cache pipeline.
- Modifying the `GetCatalogDetailResponse` DTO or any mapping profiles.
- Removing or modifying any other unused dependencies in the Catalog module — only the `ILotsClient` instance in `GetCatalogDetailHandler` is in scope.
- Adding new functionality, telemetry, or logging.

## Open Questions
None.

## Status: COMPLETE