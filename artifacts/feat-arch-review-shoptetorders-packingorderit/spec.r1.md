# Specification: Separate Internal `PackingOrderItem` from Public API DTO

## Summary
The `PackingOrderItem` class in `IPackingOrderClient.cs` serves dual roles: an internal Application-layer contract between client adapters and handlers, and a public API DTO serialized to JSON. This conflation violates the project's DTO isolation rule and has already caused leakage of internal fields (`WeightGrams`) into the public API. This work splits the type so the internal contract and the public DTO can evolve independently.

## Background
Per `docs/architecture/development_guidelines.md`, DTOs live in the `contracts/` (or use-case) folder of their owning module and must not be shared across module or layer boundaries. The current implementation breaks this rule in two places:

1. `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` (lines 47–60) declares `PackingOrderItem` as the return shape of the `IPackingOrderClient` adapter contract. The class carries the comment "A single line on the packing screen. Also serialized in the API response."
2. `GetPackingOrderResponse.Items` (`Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderResponse.cs:25`) directly references the same type, exposing it via the public REST contract.
3. `ScanOrderData.Items` (`Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderResponse.cs:37`) — a different module — also re-uses the same `PackingOrderItem`, expanding the coupling across module boundaries.

Concrete evidence of leakage:
- `PackingOrderItem.WeightGrams` was added for backend shipment-weight calculations (`ScanPackingOrderHandler.cs:102`, `ResetOrderShipmentHandler.cs:58`). It is now serialized to clients (`frontend/src/api/generated/api-client.ts:30127` exposes `weightGrams?: number`).
- The frontend already maintains a divergent hand-written `PackingOrderItem` in `frontend/src/api/hooks/useScanPackingOrder.ts:11` that **excludes** the weight field. The two clients of the type have already drifted.

Without separation, any future internal field (e.g. cost basis, internal supplier code, stock IDs) automatically becomes part of the public API, and any public-API rename forces a churn on every adapter implementation.

## Functional Requirements

### FR-1: Introduce module-local API DTOs

Introduce dedicated DTOs that represent the public API shape of a packing-order line item. Each use case owns its own DTO — DTOs must not be shared across modules, per `development_guidelines.md`.

**Required new types:**

1. `PackingOrderItemDto` in `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/`, with fields:
   - `string Name`
   - `int Quantity`
   - `string? ImageUrl`
   - `string? SetName`
2. `ScanPackingOrderItemDto` in `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/`, with the same four fields.

Both DTOs MUST be C# `class`es (not records) per the project rule that OpenAPI-generated DTOs cannot use C# `record` (see `CLAUDE.md`).

**Acceptance criteria:**
- `PackingOrderItemDto` exists in the GetPackingOrder use-case folder and contains only the four fields above.
- `ScanPackingOrderItemDto` exists in the ScanPackingOrder use-case folder and contains only the four fields above.
- Neither DTO contains `WeightGrams` nor any other internal field.
- Both are public classes with public get/set properties.
- Both files compile under `dotnet build`.

### FR-2: Keep internal `PackingOrderItem` as the adapter contract

The internal `PackingOrderItem` (in `IPackingOrderClient.cs`) MUST continue to carry `WeightGrams` because it is consumed by `ScanPackingOrderHandler` (`ScanPackingOrderHandler.cs:102`) and `ResetOrderShipmentHandler` (`ResetOrderShipmentHandler.cs:58`) for shipment-weight calculations. Its visibility scope shrinks: it is now strictly an Application-layer adapter contract, not an API surface.

**Acceptance criteria:**
- `PackingOrderItem` remains in `Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs`.
- `PackingOrderItem.WeightGrams` is retained.
- The XML doc comment on `PackingOrderItem` is updated to remove the "Also serialized in the API response" sentence and is reworded to state it is an internal Application contract only (mirroring the existing wording on `PackingOrder`: "Internal contract — not an API DTO").
- No file outside the backend Application/Adapter assemblies references `PackingOrderItem`.

### FR-3: Replace public-DTO references in response types

Both response types must reference the new DTOs instead of the internal `PackingOrderItem`.

**Required edits:**
- `GetPackingOrderResponse.cs:25`: change `List<PackingOrderItem> Items` to `List<PackingOrderItemDto> Items`.
- `ScanPackingOrderResponse.cs:37`: change `List<PackingOrderItem> Items` to `List<ScanPackingOrderItemDto> Items` (on the `ScanOrderData` class).
- Remove now-unused `using Anela.Heblo.Application.Features.ShoptetOrders;` import from `ScanPackingOrderResponse.cs` if no other reference remains.

**Acceptance criteria:**
- Neither `GetPackingOrderResponse` nor `ScanOrderData` references the internal `PackingOrderItem` type.
- `dotnet build` succeeds with no warnings about unused imports.

### FR-4: Map internal → DTO in handlers

Each handler that returns one of the public responses MUST perform an explicit mapping from `PackingOrderItem` to its module-local DTO. The mapping drops `WeightGrams` and copies the remaining four fields verbatim.

**Required edits:**
- `GetPackingOrderHandler.cs:56`: replace `Items = order.Items` with a projection: `Items = order.Items.Select(i => new PackingOrderItemDto { Name = i.Name, Quantity = i.Quantity, ImageUrl = i.ImageUrl, SetName = i.SetName }).ToList()`.
- `ScanPackingOrderHandler` (the call site that builds `ScanOrderData.Items`): apply the same projection to `ScanPackingOrderItemDto`. The handler MUST continue to use `order.Items[i].WeightGrams` for the existing weight-calculation logic at `ScanPackingOrderHandler.cs:102` — only the output projection changes.

**Acceptance criteria:**
- Both handlers compile and produce response objects whose `Items` collections contain DTO instances (not internal `PackingOrderItem`).
- Weight calculations in `ScanPackingOrderHandler` and `ResetOrderShipmentHandler` are functionally unchanged.
- An xUnit test for each handler asserts that `WeightGrams` does NOT appear on the serialized response (see FR-7).

### FR-5: Regenerate the OpenAPI TypeScript client

The frontend OpenAPI-generated client (`frontend/src/api/generated/api-client.ts`) is auto-generated on build (per `docs/development/api-client-generation.md`). After backend changes, the generated `PackingOrderItem` TypeScript class MUST no longer contain `weightGrams`.

**Acceptance criteria:**
- After running the standard build, `frontend/src/api/generated/api-client.ts` no longer contains a `weightGrams` field on any `PackingOrderItem`-related class.
- `npm run build` and `npm run lint` succeed in `frontend/`.

### FR-6: Reconcile the hand-written frontend type

`frontend/src/api/hooks/useScanPackingOrder.ts:11` defines a hand-written `PackingOrderItem` interface that already excludes weight. Its shape now matches the generated DTO exactly, so the hand-written interface should be considered for removal in favor of the generated one — but this is out of scope (see Out of Scope §1) because the hook intentionally uses a custom-fetch flow rather than the generated client.

**Acceptance criteria:**
- The hand-written interface in `useScanPackingOrder.ts` continues to compile and is not modified.
- No new fields are introduced to it as part of this work.

### FR-7: Tests assert API-surface boundary

Add or update tests to lock in the contract boundary so future regressions are caught.

**Required tests:**
1. In `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs`: assert that `GetPackingOrderResponse.Items[0]` is of type `PackingOrderItemDto` and that the type does NOT have a `WeightGrams` property (use reflection: `typeof(PackingOrderItemDto).GetProperty("WeightGrams").Should().BeNull()`).
2. In `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`: same assertion for `ScanPackingOrderItemDto`.
3. Existing tests in those files that construct or assert `PackingOrderItem` must be updated to use the new DTO types where they assert on response shape, and continue using the internal `PackingOrderItem` where they exercise the `IPackingOrderClient` mock.

**Acceptance criteria:**
- All affected test files compile and pass under `dotnet test`.
- Each handler has at least one test that fails if `WeightGrams` (or any other field beyond the four allowed) is re-added to the public DTO.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected. The added projection is O(n) over the items list (typical order: <50 items), runs once per request, and replaces a reference assignment that the JSON serializer was already iterating.

### NFR-2: Security
Reducing the public API surface is a defensive improvement: internal fields like `WeightGrams` (and any future internal-only fields) cannot accidentally leak to clients. No new auth, validation, or input-handling code is introduced. Existing endpoint authorization is unchanged.

### NFR-3: Backward compatibility
The public API loses one field: `weightGrams` on each `PackingOrderItem` entry of `GET /api/.../packing-orders/{code}` and `POST /api/packaging/orders/{code}/scan` responses. The frontend's hand-written interface already does not consume this field (`useScanPackingOrder.ts:11`), and the only generated-client consumer would be the OpenAPI client — verify no production code path reads `.weightGrams` (Open Question OQ-1). All other field names, types, and nullability remain unchanged.

### NFR-4: Code quality
- All new DTOs follow `csharp-coding-style.md`: explicit access modifiers, nullable reference types, classes (not records) for OpenAPI compatibility per `CLAUDE.md`.
- The internal `PackingOrderItem` comment is updated for accuracy.
- No new abstractions or factory layers introduced — direct in-handler projection only, per the project's "surgical changes" principle.

## Data Model

Two layers, kept structurally identical except for the internal `WeightGrams` field:

**Internal (adapter contract, unchanged shape):**
```
PackingOrderItem (Features/ShoptetOrders/IPackingOrderClient.cs)
├── Name: string
├── Quantity: int
├── ImageUrl: string?
├── SetName: string?
└── WeightGrams: int          ← internal only, kept
```

**Public (module-local DTOs, new):**
```
PackingOrderItemDto (Features/ShoptetOrders/UseCases/GetPackingOrder/)
├── Name: string
├── Quantity: int
├── ImageUrl: string?
└── SetName: string?

ScanPackingOrderItemDto (Features/Packaging/UseCases/ScanPackingOrder/)
├── Name: string
├── Quantity: int
├── ImageUrl: string?
└── SetName: string?
```

The two DTOs are intentionally structurally identical today but live in their owning modules and may evolve independently (e.g. ScanPackingOrder could later add packaging-specific fields).

## API / Interface Design

**No URL, verb, status code, or auth changes.**

Affected endpoints (response payloads change shape only — one removed field):
- `GET` GetPackingOrder endpoint → `GetPackingOrderResponse` (`Items` items lose `weightGrams`).
- `POST /api/packaging/orders/{orderCode}/scan` → `ScanPackingOrderResponse.Order.Items` items lose `weightGrams`.

The OpenAPI spec regenerates automatically from the response types. Verify the regenerated spec reflects the smaller object shape.

## Dependencies
- No new packages, services, or external dependencies.
- Touches: `Features/ShoptetOrders/IPackingOrderClient.cs`, `Features/ShoptetOrders/UseCases/GetPackingOrder/*`, `Features/Packaging/UseCases/ScanPackingOrder/*`, related tests, and the auto-regenerated `frontend/src/api/generated/api-client.ts`.
- The `ShoptetApiPackingOrderClient` adapter (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`) is **not** modified — it continues to build internal `PackingOrderItem` instances with `WeightGrams`.

## Out of Scope
1. Refactoring the hand-written `PackingOrderItem` interface in `frontend/src/api/hooks/useScanPackingOrder.ts` to use the generated client. The hook uses a bespoke fetch path for reasons unrelated to this finding.
2. Wider audit of other Application-layer types that may also dual-role (e.g. `PackingOrder` itself, `ScanShipmentPackage`). This spec is limited to `PackingOrderItem` as identified in the brief.
3. Renaming, restructuring, or adding fields to either the internal type or the DTOs. The mapping is field-for-field with the single deliberate omission of `WeightGrams`.
4. Introducing a generic mapper (AutoMapper / Mapster). The project favors hand-written projections per the surgical-changes principle.
5. Migrating `PackingOrderItem` to a `Contracts/` folder. The brief allows this option "if the module grows" but is not requested now.

## Open Questions

None.

## Status: COMPLETE