# Architecture Review: Expose Shipping Address Fields in GetPackingOrder Response

## Skip Design: true

No UI/UX work in scope — this change extends an existing API response DTO with three optional fields. The spec explicitly defers any frontend rendering of the address as out-of-scope.

## Architectural Fit Assessment

The change aligns cleanly with the existing Clean Architecture + Vertical Slice layout and **introduces zero new architectural concepts**.

Verified state in this branch:
- `PackingOrder` (internal application contract, `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs:18-45`) already declares `ShippingStreet`, `ShippingCity`, `ShippingZip` as `string?`.
- `ShoptetApiPackingOrderClient` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs:99-118`) already normalizes and populates them.
- A sibling consumer in the same slice — `ScanPackingOrderHandler` (`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs:167-169`) — already reads them off `PackingOrder` and forwards them downstream. This is a confirmed precedent for surfacing exactly these three fields out of this slice.
- The only gap is the mapping in `GetPackingOrderHandler.Handle` and the corresponding fields on `GetPackingOrderResponse`.

Layer boundaries to respect:
- **Adapter (`Anela.Heblo.Adapters.ShoptetApi`)** is the single source of truth for address normalization (`CombineStreetAndHouseNumber`, `NormalizeAddressField`). No address logic moves up.
- **Application use-case slice (`Features/ShoptetOrders/UseCases/GetPackingOrder`)** owns the request, handler, response, and any view-shaped types for this endpoint. New fields belong on `GetPackingOrderResponse` here.
- **OpenAPI → TypeScript client** is generated on build; no manual frontend client edits.

Integration points: one handler method, one DTO class, one test file. No persistence, no DI wiring, no new module references.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────┐        ┌────────────────────────────────────────────┐
│ Shoptet REST API           │  HTTP  │ Adapter: ShoptetApiPackingOrderClient      │
│ (DeliveryAddress.Street,   │ ─────► │   - CombineStreetAndHouseNumber()          │
│  HouseNumber, City, Zip)   │        │   - NormalizeAddressField()                │
└────────────────────────────┘        │   builds PackingOrder { Shipping*  ✅ }    │
                                      └──────────────────────┬─────────────────────┘
                                                             │ PackingOrder
                                                             ▼
                                      ┌────────────────────────────────────────────┐
                                      │ Application: GetPackingOrderHandler        │
                                      │   maps PackingOrder → GetPackingOrderRsp   │
                                      │   ◀── add: ShippingStreet/City/Zip mapping │
                                      └──────────────────────┬─────────────────────┘
                                                             │ GetPackingOrderResponse
                                                             ▼
                                      ┌────────────────────────────────────────────┐
                                      │ API: GET /api/shoptet-orders/{code}/packing│
                                      │   serializes DTO → JSON                    │
                                      └──────────────────────┬─────────────────────┘
                                                             │ OpenAPI / NSwag (build)
                                                             ▼
                                      ┌────────────────────────────────────────────┐
                                      │ Generated TS client (api-client.ts)        │
                                      │   GetPackingOrderResponse gains 3 optionals│
                                      └────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where new properties live
**Options considered:**
A. Add fields directly to `GetPackingOrderResponse` (flat).
B. Introduce a nested `ShippingAddress` value object on the response.

**Chosen approach:** A — flat properties on `GetPackingOrderResponse`.

**Rationale:** The existing internal `PackingOrder` contract already exposes them flat. Sibling DTOs in the slice (`GetPackingOrderResponse` itself: `CustomerName`, `ShippingMethodName`, `CustomerNote`, `EshopNote`) are flat scalar fields. Introducing a nested object now would diverge from precedent without a concrete consumer demanding it and would alter the generated TS surface beyond a non-breaking additive change. YAGNI applies — the spec says only three fields, all out-of-scope items are explicitly deferred.

#### Decision 2: DTO type — class vs record
**Options considered:** Use C# `record`; use `class` with `{ get; set; }` properties.

**Chosen approach:** Extend the existing `class` with `public string? ShippingStreet { get; set; }` etc.

**Rationale:** Project-wide rule (`CLAUDE.md`, `docs/architecture/development_guidelines.md`): DTOs are classes, never records, because NSwag-generated OpenAPI clients mishandle record constructor parameter order. `GetPackingOrderResponse` is already a `class : BaseResponse` — matching existing style is required, not optional.

#### Decision 3: Nullability + serialization
**Options considered:** Required non-null strings with empty-string defaults; optional `string?` with `null` when missing.

**Chosen approach:** `string?` (nullable, defaults to `null`).

**Rationale:** The adapter already returns `null` for personal-pickup orders and unparseable addresses (see `NormalizeAddressField` returning null for whitespace). Mirroring that with `null` end-to-end preserves "no address" semantics; an empty string would lose that signal. JSON will serialize null, and the generated TS client will type the field as `string | undefined`, consistent with existing optional properties (`CustomerNote`, `EshopNote`).

#### Decision 4: Mapping site
**Options considered:** Map in the handler; introduce a dedicated mapper / extension method.

**Chosen approach:** Three additional property assignments inside the existing object initializer in `GetPackingOrderHandler.Handle`.

**Rationale:** The handler currently inlines the entire mapping in one object initializer (`GetPackingOrderHandler.cs:41-57`). Introducing a mapper for three additional lines would not match the slice's style and would add an abstraction the slice does not currently need (KISS, YAGNI). Stay surgical.

## Implementation Guidance

### Directory / Module Structure

No new files. Touch exactly:

```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/
  ├─ GetPackingOrderResponse.cs        ← add 3 properties
  └─ GetPackingOrderHandler.cs         ← add 3 mapping lines in object initializer

backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/
  └─ GetPackingOrderHandlerTests.cs    ← extend mapping test + add null-case assertion
```

Build output side-effect (do not edit manually):
```
frontend/src/api/generated/api-client.ts  ← regenerated by NSwag on build
```

### Interfaces and Contracts

`GetPackingOrderResponse` (final shape, after change):

```csharp
public class GetPackingOrderResponse : BaseResponse
{
    public GetPackingOrderResponse() { }
    public GetPackingOrderResponse(ErrorCodes errorCode, Dictionary<string, string>? @params = null)
        : base(errorCode, @params) { }

    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }
    public PackingEligibility Eligibility { get; set; } = new();
    public string? CustomerNote { get; set; }
    public string? EshopNote { get; set; }
    public string? ShippingStreet { get; set; }   // NEW
    public string? ShippingCity { get; set; }     // NEW
    public string? ShippingZip { get; set; }      // NEW
    public List<PackingOrderItem> Items { get; set; } = new();
}
```

`GetPackingOrderHandler.Handle` mapping addition (inside the existing initializer at lines 41-57):

```csharp
ShippingStreet = order.ShippingStreet,
ShippingCity   = order.ShippingCity,
ShippingZip    = order.ShippingZip,
```

Do **not**:
- Touch `IPackingOrderClient.cs` or `PackingOrder` (the internal contract already has the fields).
- Touch `ShoptetApiPackingOrderClient` or its helpers.
- Add a new mapper class, extension method, or AutoMapper profile.
- Reformat or reorder existing properties.

### Data Flow

For `GET /api/shoptet-orders/{code}/packing`:

1. Controller dispatches `GetPackingOrderRequest` via MediatR.
2. `GetPackingOrderHandler` calls `IPackingOrderClient.GetPackingOrderAsync(code, ct)`.
3. `ShoptetApiPackingOrderClient` calls Shoptet, reads `detail.DeliveryAddress ?? detail.BillingAddress`, normalizes street + house number, city, zip into `PackingOrder.Shipping*` (existing behavior, unchanged).
4. Handler projects `PackingOrder → GetPackingOrderResponse`, **now including** `ShippingStreet`, `ShippingCity`, `ShippingZip` (new).
5. ASP.NET Core serializes the DTO to JSON via the existing pipeline (camelCase via configured options).
6. On `npm run build`, NSwag regenerates `GetPackingOrderResponse` in the TS client with three new optional string fields.

Null-path: when `detail.DeliveryAddress` and `detail.BillingAddress` are both null, the adapter sets all three fields to `null`. The handler forwards the nulls unchanged. JSON emits `null`; TS client receives `undefined`/`null`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting `npm run build` step leaves stale TS client; consumers won't see new fields | Low | Spec's validation gates already require `npm run build` and `npm run lint`. The generated file is committed, so PR diff makes a missed regeneration obvious. |
| Test arranges `PackingOrder` without shipping fields (defaults to null) and a future regression silently drops the mapping | Medium | FR-3 mandates at least one test arranges non-null shipping values and asserts they appear on the response. Add an assertion to the existing positive-path test (`Handle_OrderFound_ReturnsMappedResponse`) rather than a new test, keeping the suite small. |
| PII (street/city/zip) exposed via API | Low | Endpoint inherits the same Entra ID auth as all other ShoptetOrders endpoints; same authorization context as customer name already returned. No new attack surface. Verify no new logging of these fields. |
| OpenAPI generator surprises (e.g., capitalization, optional vs required) | Low | `CustomerNote` and `EshopNote` are already `string?` and serialize as optional in the existing TS client — the new fields will follow the same proven path. |
| Mapping drifts again as response evolves | Low | Existing pattern is inline object-initializer mapping. Long-term mitigation (if it recurs) would be a dedicated mapper, but not for this change. |

## Specification Amendments

None required. The spec is accurate against the verified codebase. Two minor clarifications worth noting (not changes):

1. **Sibling precedent exists.** `ScanPackingOrderHandler` (`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs:167-169`) already consumes `PackingOrder.Shipping*` and uses `string.IsNullOrEmpty` to normalize empties to null before forwarding. `GetPackingOrderHandler` does not need that guard — the adapter's `NormalizeAddressField` already returns null for whitespace, and `GetPackingOrderResponse` directly mirrors `PackingOrder`'s nullable strings rather than feeding a downstream consumer with stricter semantics. Keep the mapping a plain `Shipping* = order.Shipping*`.
2. **Test additions are minimal.** The existing `Handle_OrderFound_ReturnsMappedResponse` test is the natural home for the positive assertion (extend the arranged `PackingOrder` with the three fields and add three `.Should().Be(...)` lines). `Handle_WhenOrderIsInPackingState_ReturnsEligibleWithNullWarning` already arranges a minimal `PackingOrder` with default-null shipping fields and can absorb three null assertions to cover FR-3's null-case requirement without creating a new test.

## Prerequisites

None. No migrations, no infrastructure, no configuration, no Key Vault secrets, no feature flags. The change can begin immediately and ships as a single atomic PR covering:
- `GetPackingOrderResponse.cs` (3 properties)
- `GetPackingOrderHandler.cs` (3 mapping lines)
- `GetPackingOrderHandlerTests.cs` (assertions on existing tests)
- Regenerated `api-client.ts` (build artifact, committed)