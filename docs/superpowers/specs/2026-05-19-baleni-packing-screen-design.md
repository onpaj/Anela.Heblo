# Balení — Packing Screen Design

**Date:** 2026-05-19
**Module:** Balení (`/baleni/baleni`)
**Status:** Approved design — ready for implementation planning

## Context

The Balení module is a landscape touch-PC PWA added in PR #1398. It currently ships a
shell only: a home screen with three tiles, each routing to a `BaleniPlaceholder`
("Brzy k dispozici"). The `/baleni/baleni` tile is the packing station.

This design replaces that placeholder with a real packing screen. A packer at the
station scans an order's barcode (the Shoptet order number) and immediately sees what
the order contains, who it is for, how it ships, and whether it needs a cooling pack —
so they can pack it correctly without leaving the station or scrolling.

Scope of this first version is **display only**: scan an order, show its information.
No "confirm packed" / status-update action yet.

## Goals

- Scan an order number → load and display that order's header and items.
- Everything fits on **one landscape screen, no scrolling**, even for large orders.
- Show product photos when there is room; drop them for large orders.
- Clearly flag whether the package should be cooled.

## Non-goals

- No "mark as packed" or any Shoptet status mutation.
- No camera-based scanning (hardware barcode scanner inputs as keyboard).

## User flow

1. Packer opens `/baleni/baleni`. Screen shows an empty state: "Naskenujte číslo objednávky".
2. Packer scans an order barcode. The barcode value equals the Shoptet order code.
3. Frontend calls `GET /api/shoptet-orders/{code}/packing`, shows a loading state.
4. On success, the order panel renders: order number, customer name, shipping method,
   cooling badge, and the item list.
5. Scanning another order replaces the displayed order.

## Layout

Landscape, single screen, no scroll. The page renders inside the existing
`BaleniLayout` (shared header: back button, "Heblo Balení", `UserProfile`).

**Top bar** (within the packing page):
- Left — order meta block: `Objednávka {code}`, customer full name, `Doprava: {shipping method name}`, and a cooling badge.
- Right — the barcode scan input (always focused).

**Item area** (fills remaining height):
- **Photo grid** — 2-column grid, each item shows thumbnail + name + quantity. Used when item count ≤ `PHOTO_ITEM_LIMIT`.
- **Dense list** — 2-column text list (name + quantity), no photos. Used when item count > `PHOTO_ITEM_LIMIT`.

`PHOTO_ITEM_LIMIT` is a named constant, default `12`, tunable.

Cooling badge states: `❄ Chlazení L1`, `❄ Chlazení L2`, `Bez chlazení`.

## Frontend

Replace the placeholder route at `/baleni/baleni` with a new `BaleniPacking` page.

- **New:** `frontend/src/components/baleni/BaleniPacking.tsx` — page: empty state, scan handling, loading/error states, order panel.
- Extract sub-components as the file grows (target < 300 lines each), e.g. `PackingOrderPanel`, `PackingItemGrid`, `PackingItemList`.
- **Reuse:** `ScanInput` (`frontend/src/components/terminal/ScanInput.tsx`) as-is — props `autoFocusOnMount`, `refocusOnBlur`, `allowKeyboardToggle` enabled so it keeps focus for the next scan; `loading` set during fetch; Enter submits.
- **API call:** new hook following the project pattern; URL built as `${apiClient.baseUrl}${relativeUrl}` (absolute URL rule).
- **Routing:** update `App.tsx` Balení route group to render `BaleniPacking` at `/baleni/baleni`.

## Backend

New endpoint to fetch a single packing order by Shoptet order code.

- **Endpoint:** `GET /api/shoptet-orders/{code}/packing` on `ShoptetOrdersController`
  (`backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs`).
- **Query/handler:** `GetPackingOrderQuery` + `GetPackingOrderHandler` (MediatR), new
  use-case folder under `Anela.Heblo.Application/Features/ShoptetOrders/`.

Handler orchestration:
1. `IEshopOrderClient.GetExpeditionOrderDetailAsync(code)` — order header + line items.
2. **Cooling** — reuse the carrier-cooling matrix logic currently in
   `ShoptetApiExpeditionListSource` (`MapToExpeditionOrder`, `ResolveCarrierCooling`,
   `ApplyEnrichment`, currently `private`/`internal static`). Extract these into a
   shared static helper (e.g. `ExpeditionOrderMapper`) so the picking list and the
   packing handler use one code path — no duplicated logic. Inputs:
   `ICarrierCoolingRepository` (carrier matrix) and per-product `Cooling` from
   `ICatalogRepository`. Outputs: order `CarrierCooling` level and `IsCooled`.
3. **Images** — per item, `ICatalogRepository.GetByIdAsync(productCode)` → `Image`
   field (already synced from the Shoptet CSV stock export).

**Item filtering & sets:** the existing `MapOrderItems` mapper is reused as-is. It keeps
only `product`/`gift`/`product-set` lines (shipping, billing, discount-coupon lines are
dropped) and **expands product sets into their component products**, marking each
component with `IsFromSet` and `SetName`. This matches the picking list and yields
accurate per-component cooling.

**Response DTO** — a plain class (not a C# record — OpenAPI generator rule):
- `code` — order number
- `customerName` — full name
- `shippingMethodName` — shipping method name from the order (`shipping.name`)
- `cooling` — `Cooling` enum (`None` / `L1` / `L2`)
- `isCooled` — bool
- `items[]` — each: `name`, `quantity`, `imageUrl` (nullable), `setName` (nullable —
  the parent set's name when the item is a set component)

## Error handling

- Order not found / Shoptet 404 → backend returns 404; frontend shows
  "Objednávka nenalezena" inline, scan input stays focused for retry.
- Shoptet API / network failure → frontend shows "Nepodařilo se načíst objednávku"
  with a retry hint.
- During fetch → `ScanInput` `loading` prop disables input and shows a spinner.
- Missing product image → item cell falls back to a placeholder thumbnail.
- Missing per-product cooling → treated as `Cooling.None`.
- Backend logs detailed error context; frontend shows user-friendly messages only.

## Testing

- **Backend unit** — `GetPackingOrderHandler`: cooling computation, item filtering,
  image mapping, order-not-found path. Mock `IEshopOrderClient`,
  `ICarrierCoolingRepository`, `ICatalogRepository`.
- **Frontend unit** — `BaleniPacking`: empty state, rendered order, photo-grid vs.
  dense-list switch at `PHOTO_ITEM_LIMIT`, loading and error states.
- **E2E** — `frontend/test/e2e/baleni/`: scan a fixture order, assert order info
  renders. Runs in the nightly suite.

## Validation before completion

- BE: `dotnet build` + `dotnet format`
- FE: `npm run build` + `npm run lint`
- All touched tests pass
- E2E: `./scripts/run-playwright-tests.sh` against staging
