# Specification: Expose Shipping Address Fields in GetPackingOrder Response

## Summary
The `ShoptetApiPackingOrderClient` already computes a normalized shipping address (street, city, zip) for each packing order, but the `GetPackingOrderHandler` discards those fields when mapping to `GetPackingOrderResponse`. This spec defines the surface-level changes needed to surface the existing data through the `GET /api/shoptet-orders/{code}/packing` endpoint without altering any upstream normalization logic.

## Background
`ShoptetApiPackingOrderClient.GetPackingOrderAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs:99-118`) constructs a `PackingOrder` with `ShippingStreet`, `ShippingCity`, and `ShippingZip` derived via `CombineStreetAndHouseNumber` and `NormalizeAddressField`. The MediatR handler `GetPackingOrderHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs:41-57`) projects every other `PackingOrder` field into the response DTO but omits these three. Consequently, the generated TypeScript client (`frontend/src/api/generated/api-client.ts:35061-35120`) has no shipping-address fields, blocking any frontend or integration consumer from displaying the delivery address.

The gap is not currently caught by tests: `GetPackingOrderHandlerTests` neither arranges nor asserts shipping-address fields. This is a pure plumbing fix — the data already exists, it is simply not being forwarded.

## Functional Requirements

### FR-1: Add shipping address fields to GetPackingOrderResponse
Add three optional, nullable string properties to the `GetPackingOrderResponse` DTO:
- `ShippingStreet`
- `ShippingCity`
- `ShippingZip`

These must be regular C# properties on a class (per project rule: DTOs are classes, never records, to keep OpenAPI client generation stable).

**Acceptance criteria:**
- `GetPackingOrderResponse` declares `public string? ShippingStreet { get; set; }`, `public string? ShippingCity { get; set; }`, and `public string? ShippingZip { get; set; }`.
- All three properties are nullable so orders without delivery addresses (personal pickup, missing data) serialize as `null` rather than failing.
- After build, the regenerated TypeScript client `GetPackingOrderResponse` interface includes the three new optional string fields.

### FR-2: Map shipping address fields in the handler
`GetPackingOrderHandler.Handle` must project the three new properties from the `PackingOrder` domain object onto the response DTO alongside the existing field mappings.

**Acceptance criteria:**
- The handler's response construction includes `ShippingStreet = order.ShippingStreet`, `ShippingCity = order.ShippingCity`, `ShippingZip = order.ShippingZip`.
- No transformation or normalization is performed in the handler — the adapter is the single source of truth for address formatting.
- When `PackingOrder` has null shipping fields (e.g., adapter could not derive them), the response carries nulls through faithfully.

### FR-3: Test coverage for the new mapping
Extend `GetPackingOrderHandlerTests` so the mapping is exercised by at least one test case.

**Acceptance criteria:**
- At least one existing test (or a new one) arranges a `PackingOrder` with non-null `ShippingStreet`, `ShippingCity`, and `ShippingZip` values and asserts those values appear on the returned `GetPackingOrderResponse`.
- At least one test path covers the null case (a `PackingOrder` with null shipping fields produces a response with null shipping fields) — this may be folded into an existing "minimal data" test if one exists, otherwise added.
- All `GetPackingOrderHandlerTests` continue to pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. The change is three additional property assignments inside an existing mapping block; no extra I/O, allocations of consequence, or computation.

### NFR-2: Security / Data sensitivity
Delivery addresses are personally identifiable information (PII). The endpoint `GET /api/shoptet-orders/{code}/packing` already returns customer-level data (per the existing response shape) and is gated by the same auth as other ShoptetOrders endpoints. No new authorization changes are required; the new fields inherit the existing endpoint's protection.

### NFR-3: API compatibility
Adding optional nullable fields to a response DTO is a non-breaking change for existing consumers. The generated TypeScript client will gain optional fields; existing callers that ignore them remain functional.

### NFR-4: Validation gates
Before completion, run:
- `dotnet build` (must succeed)
- `dotnet format` (no diff)
- `dotnet test` for the `GetPackingOrderHandlerTests` project (all green)
- `npm run build` in `frontend/` to confirm the regenerated TypeScript client compiles
- `npm run lint` in `frontend/` (no new issues)

## Data Model
No persistence-layer changes. The domain entity `PackingOrder` already carries the three fields. Only the application-layer DTO `GetPackingOrderResponse` is extended.

```
PackingOrder (domain, unchanged)
  ├─ ShippingStreet : string?
  ├─ ShippingCity   : string?
  └─ ShippingZip    : string?

GetPackingOrderResponse (DTO, extended)
  ├─ {existing fields, unchanged}
  ├─ ShippingStreet : string?   ← NEW
  ├─ ShippingCity   : string?   ← NEW
  └─ ShippingZip    : string?   ← NEW
```

## API / Interface Design
Endpoint: `GET /api/shoptet-orders/{code}/packing`
Behavior: unchanged.
Response payload: gains three optional string fields. Example fragment:

```json
{
  "...existing fields...": "...",
  "shippingStreet": "Hlavní 123/4",
  "shippingCity": "Praha",
  "shippingZip": "110 00"
}
```

When the underlying Shoptet order has no delivery address (e.g., personal pickup), the fields are emitted as `null`.

The generated TypeScript client `GetPackingOrderResponse` interface will gain three optional `string | undefined` properties.

## Dependencies
- `Anela.Heblo.Adapters.ShoptetApi` — no changes; provides the populated `PackingOrder`.
- `Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder` — the only namespace touched by the implementation.
- OpenAPI / NSwag TypeScript client generation — runs on build; will pick up the new fields automatically.

## Out of Scope
- Frontend UI changes to display the shipping address. This spec only restores the data to the API contract; consuming and rendering it is a separate task.
- Any change to `ShoptetApiPackingOrderClient` or its normalization helpers (`CombineStreetAndHouseNumber`, `NormalizeAddressField`).
- Adding country, recipient name, company, or other address components beyond street, city, and zip — only the three fields already computed by the adapter are in scope.
- Backfilling shipping addresses for historical orders, caching, or any data-migration work.
- Changes to other ShoptetOrders endpoints or DTOs.

## Open Questions
None.

## Status: COMPLETE