Now I have enough context to write the spec. The brief describes an architectural module-boundary violation where `ManufactureStockTakingController` dispatches a Catalog-owned MediatR request. I also confirmed:

- `StockTakingController` already exposes the same handler at `/api/StockTaking/history` (Catalog-scoped endpoint)
- `SubmitManufactureStockTakingHandler` is correctly Manufacture-owned and uses `IManufactureCatalogSource` cross-module contract
- The frontend hook `useStockTakingHistory` in `useManufactureStockTaking.ts` calls the Manufacture endpoint
- The history filtering is currently scoped to a single product code (no Material-type restriction)

# Specification: Remove Cross-Module Dependency in `ManufactureStockTakingController.GetManufactureStockTakingHistory`

## Summary
The `GET /api/manufacture-stock-taking/history` endpoint in the Manufacture module currently dispatches `GetStockTakingHistoryRequest`, a MediatR contract owned by the Catalog module, creating a silent compile-time coupling that violates the project's module-boundary rules and undermines future microservice extractability. This work introduces a Manufacture-owned use case (`GetManufactureStockTakingHistory`) that serves the same endpoint without referencing the Catalog application namespace, and migrates the frontend off the deprecated Catalog-typed response shape.

## Background
The repository targets a Clean Architecture vertical-slice layout (see `docs/architecture/development_guidelines.md`) with one rule that each module's controller may only dispatch requests owned by that module's own `Application/Features/<Module>/UseCases/*` namespace. A daily arch-review routine flagged the following violation:

- File: `backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs`
- Line 1: `using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;`
- Line 47: dispatches `GetStockTakingHistoryRequest`, a Catalog-owned MediatR request handled by `GetStockTakingHistoryHandler` in the Catalog module.

The sibling action `POST /submit` on the same controller correctly dispatches `SubmitManufactureStockTakingRequest`, a Manufacture-owned request whose handler depends on `IManufactureCatalogSource` (a Manufacture-defined cross-module contract). The history action breaks this pattern.

Note that `StockTakingController` (the Catalog-owned controller) already exposes the same handler at `GET /api/StockTaking/history`. The Manufacture endpoint is, in effect, a duplicate that routes through a foreign module — but its frontend caller currently expects it to live on the Manufacture surface (used by the material-inventory feature; the `useStockTakingHistory` hook in `frontend/src/api/hooks/useManufactureStockTaking.ts` calls `manufactureStockTaking_GetManufactureStockTakingHistory`).

**Selected approach: Option B from the brief — introduce a Manufacture-owned handler.** This matches the existing pattern of `SubmitManufactureStockTakingHandler`, preserves the public endpoint (and the React Query hook that depends on it), and supports microservice extractability by keeping all dispatch from `ManufactureStockTakingController` resolvable inside the Manufacture slice. The Catalog-scoped endpoint at `/api/StockTaking/history` is unaffected.

## Functional Requirements

### FR-1: Introduce Manufacture-owned `GetManufactureStockTakingHistory` use case
Create a new use case under `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/` containing:
- `GetManufactureStockTakingHistoryRequest` (class implementing `IRequest<GetManufactureStockTakingHistoryResponse>`)
- `GetManufactureStockTakingHistoryResponse` (class extending `BaseResponse`)
- `ManufactureStockTakingHistoryItemDto` (class)
- `GetManufactureStockTakingHistoryHandler` (class implementing `IRequestHandler<...>`)

The request mirrors the existing Catalog request fields: `ProductCode` (required, `[StringLength(50)]`), `PageNumber` (default 1), `PageSize` (default 20), `SortBy` (nullable, default `"date"`), `SortDescending` (default `true`).

**Acceptance criteria:**
- The new files exist in the Manufacture namespace `Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory`.
- DTOs are declared as `class`, not `record` (per project rule for OpenAPI client compatibility).
- `GetManufactureStockTakingHistoryResponse` extends `BaseResponse` and exposes the same fields the Catalog response exposes today: `Items`, `TotalCount`, `PageNumber`, `PageSize`, computed `TotalPages`.
- `ManufactureStockTakingHistoryItemDto` carries the same fields the existing `StockTakingHistoryItemDto` carries: `Id`, `Type`, `Code`, `AmountNew`, `AmountOld`, `Date`, `User`, `Error`, computed `Difference`.
- `GetManufactureStockTakingHistoryHandler` MUST NOT take any dependency on `Anela.Heblo.Application.Features.Catalog.*`. It depends only on Manufacture-owned contracts (`IManufactureCatalogSource` for product access) and the Domain layer (`Anela.Heblo.Domain.Features.Catalog.*` domain types are allowed; only Catalog **Application** namespaces are forbidden, since Domain is shared per existing guidelines).
- The handler returns `ErrorCodes.ProductNotFound` when the product code does not resolve, matching the current Catalog handler's contract.

### FR-2: Repoint `ManufactureStockTakingController` at the Manufacture-owned request
Update `backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs`:
- Remove `using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockTakingHistory;`.
- Change the `GET history` action to accept `[FromQuery] GetManufactureStockTakingHistoryRequest` and return `ActionResult<GetManufactureStockTakingHistoryResponse>`.
- Keep route (`api/manufacture-stock-taking/history`), HTTP method, authorization attribute, and logging behavior unchanged.

**Acceptance criteria:**
- `ManufactureStockTakingController.cs` contains no `using` of any `Anela.Heblo.Application.Features.Catalog.*` namespace.
- The compiled `dotnet build` succeeds.
- The endpoint continues to return paginated stock-taking history for a given product code at the same URL with the same query-string parameter names.

### FR-3: Preserve the Catalog-owned endpoint untouched
`StockTakingController` (`GET /api/StockTaking/history`) and the underlying Catalog handler `GetStockTakingHistoryHandler` MUST remain in place and unchanged. They serve the general (non-Manufacture) stock-taking history surface.

**Acceptance criteria:**
- No edits to files under `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetStockTakingHistory/`.
- No edits to `StockTakingController.cs`.
- Tests in `backend/test/Anela.Heblo.Tests/Features/Catalog/GetStockTakingHistoryHandlerTests.cs` continue to pass without modification.

### FR-4: Regenerate the OpenAPI clients and update the frontend hook
The TypeScript client is auto-generated on build (per `docs/development/api-client-generation.md`). The renamed types must propagate to `frontend/src/api/generated/api-client.ts`. Update `frontend/src/api/hooks/useManufactureStockTaking.ts` to import the new `GetManufactureStockTakingHistoryResponse` type instead of `GetStockTakingHistoryResponse` from `../generated/api-client`.

**Acceptance criteria:**
- `frontend/src/api/hooks/useManufactureStockTaking.ts` no longer imports `GetStockTakingHistoryResponse` from the generated client; it imports `GetManufactureStockTakingHistoryResponse` instead.
- The internal request shape `GetStockTakingHistoryRequest` declared inside that hook file is renamed to `GetManufactureStockTakingHistoryRequest` (or left in place if it remains an internal-only convenience type — see Open Questions).
- `frontend/src/api/hooks/useStockTaking.ts` (the Catalog-side hook) is unchanged.
- `npm run build` and `npm run lint` pass.

### FR-5: Test coverage for the new handler
Add `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetManufactureStockTakingHistory/GetManufactureStockTakingHistoryHandlerTests.cs` covering:
- Returns paginated history items for an existing product, sorted by Date descending by default.
- Honours `SortBy` for the supported fields (date, code, type, amountnew, amountold, user).
- Honours `SortDescending` true/false.
- Returns `ErrorCodes.ProductNotFound` when the product code does not resolve.
- Returns the correct `TotalCount` and `PageSize`/`PageNumber` echoes.

**Acceptance criteria:**
- Tests follow project conventions (xUnit, FluentAssertions, NSubstitute or Moq per existing test patterns).
- Coverage of the new handler is at or above 80% per the project's testing standard.
- Existing test suite continues to pass: `dotnet test` is green.

### FR-6: No behavioural change at the HTTP boundary
The HTTP response payload of `GET /api/manufacture-stock-taking/history` must be JSON-equivalent to today's response (same field names, same types, same defaults, same pagination semantics). The renaming is an internal namespace/type refactor; existing API consumers calling the URL directly with unchanged query strings continue to deserialize the response into structurally identical shapes.

**Acceptance criteria:**
- A JSON diff between the response from the current main branch and the response after this change, for an identical request, shows no differences in field names, ordering of items, or computed values.

## Non-Functional Requirements

### NFR-1: Performance
No regression. The new handler executes the same in-memory LINQ over `product.StockTakingHistory` that the Catalog handler executes today. Median request latency MUST stay within ±10% of the current baseline (informal check against staging is sufficient — no formal benchmark required).

### NFR-2: Security
Authorization behavior is preserved. The `[FeatureAuthorize(Feature.Manufacture_MaterialInventory)]` attribute on the controller continues to gate the endpoint at the same access level. No changes to authentication, authorization, or data exposure.

### NFR-3: Architecture & module boundaries
After this change, `grep "Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.API/Controllers/ManufactureStockTakingController.cs` returns no matches. The controller's MediatR dispatch surface is entirely Manufacture-owned. This is the primary success signal.

### NFR-4: Backwards compatibility
The HTTP URL, query-string parameter names, and JSON response field names are preserved. Existing clients (the React frontend and any future API consumers) continue to function without changes beyond a regenerated typed client.

## Data Model
No database schema changes. The new handler reads from the same `CatalogAggregate.StockTakingHistory` collection that the Catalog handler reads from today, via the `IManufactureCatalogSource.GetByIdAsync(productCode, cancellationToken)` cross-module contract that `SubmitManufactureStockTakingHandler` already uses.

New in-memory DTOs (Manufacture-owned):
- `GetManufactureStockTakingHistoryRequest` — query parameters envelope.
- `GetManufactureStockTakingHistoryResponse : BaseResponse` — paginated result envelope.
- `ManufactureStockTakingHistoryItemDto` — per-record projection (structurally identical to `StockTakingHistoryItemDto` in the Catalog module).

## API / Interface Design

### HTTP endpoint (unchanged URL, refactored internals)
```
GET /api/manufacture-stock-taking/history
  ?productCode={string, required, max 50 chars}
  &pageNumber={int, default 1}
  &pageSize={int, default 20}
  &sortBy={string, optional, one of: date|code|type|amountnew|amountold|user; default "date"}
  &sortDescending={bool, default true}

200 OK
{
  "items": [
    { "id": int, "type": "Manufacture|Receive|...", "code": "...",
      "amountNew": number, "amountOld": number, "date": "ISO-8601",
      "user": "string|null", "error": "string|null", "difference": number }
  ],
  "totalCount": int,
  "pageNumber": int,
  "pageSize": int,
  "totalPages": int
}

400 ProductNotFound (via BaseResponse error envelope)
401/403 (unchanged auth)
```

### MediatR contract (Manufacture-owned, new)
```csharp
namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureStockTakingHistory;

public class GetManufactureStockTakingHistoryRequest
    : IRequest<GetManufactureStockTakingHistoryResponse> { /* same fields as existing Catalog request */ }

public class GetManufactureStockTakingHistoryResponse : BaseResponse
{
    public List<ManufactureStockTakingHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

### MediatR contract (Catalog-owned, unchanged)
`GetStockTakingHistoryRequest` / `GetStockTakingHistoryResponse` stay where they are and continue to be served by `StockTakingController`.

## Dependencies
- MediatR (existing).
- `IManufactureCatalogSource` — Manufacture-owned cross-module abstraction over the Catalog repository, already in use by `SubmitManufactureStockTakingHandler`. The new handler reuses it.
- AutoMapper (existing) — only if the Manufacture handler chooses to use AutoMapper for the DTO projection; manual projection is equally acceptable since the projection is trivial.
- OpenAPI/NSwag toolchain — auto-runs on build to regenerate `frontend/src/api/generated/api-client.ts`.

## Out of Scope
- Removing the Catalog-side endpoint `GET /api/StockTaking/history` or its handler.
- Consolidating the two endpoints into one (e.g. removing the Manufacture surface and migrating the frontend hook to call the Catalog endpoint). This was Option A in the brief; the team can revisit it later as a separate refactor if the duplication becomes a maintenance burden.
- Adding Manufacture-specific business filters to the history view (e.g. restricting to `ProductType.Material`). The current behavior is product-code-scoped without type filtering, and that behavior is preserved verbatim.
- Renaming the underlying domain field `CatalogAggregate.StockTakingHistory` or modifying any Domain layer code.
- Changes to the `SubmitManufactureStockTaking` use case or its handler.
- Performance optimization of the in-memory LINQ-over-history query.

## Open Questions
None.

## Status: COMPLETE