---
process: flow-block-order
kind: workflow
module: shoptet-orders
summary: API action that puts a Shoptet order on hold — checks it is in an allowed state, moves it to the configured "blocked" state and appends the reason to the order's internal remark.
owns:
  - backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/**
verified_at: "5e993f9e2"
related: []
---

# Block order processing

## Purpose
Stops an e-shop order from moving further through the warehouse pipeline (e.g. a problem with
payment, stock or the customer's request) by switching its Shoptet status to a "blocked" state
and writing the reason into the internal staff remark (Interní poznámka, `eshopRemark`) so
everyone looking at the order in Shoptet sees why. Heblo stores nothing.

## Trigger
On demand: `PATCH /api/shoptet-orders/{code}/block` with body `{"note": "<reason>"}`.
Requires permission feature `Warehouse_Expedition` (controller-level `FeatureAuthorize`).
Returns `204 No Content` on success.

**No caller in the repo**: neither the frontend nor any MCP tool calls this endpoint (checked
at the verified commit). It is reachable only by a direct API call.

## Data flow
1. `GET /api/orders/{code}` (Shoptet) → current `status.id`.
2. If the status is **not** in `ShoptetOrders:AllowedBlockSourceStateIds` → stop, return error
   `ShoptetOrderInvalidSourceState` (2101) with `orderCode` and `currentStatusId`
   (UI text "Objednávku nelze zablokovat – není ve povoleném stavu").
3. `PATCH /api/orders/{code}/status` `{"data":{"statusId": BlockedStatusId}}`.
4. Append the note: `GET /api/orders/{code}?include=notes` → `PATCH /api/orders/{code}/notes`
   with `eshopRemark = "<old remark>\n<note>"` (or just `<note>` when empty).

## Logic & formulas
- Repo config: allowed source states `[-1, 35, 3, 32, -2]`, blocked state `35`. Only `-2`
  (Vyřizuje se) is named in the repo; the names of `-1`, `3`, `32` and `35` are not documented
  (look them up via `GET /api/eshop?include=orderStatuses`). `35` being both a source and the
  target means blocking an already-blocked order succeeds again and appends another note.
- Any exception in steps 1–3 (order not found, Shoptet error) → logged, `InternalServerError`.
  There is no dedicated "not found" error here.
- Step 4 failure → logged as Warning; the request still succeeds (order blocked, no note).
- The note is not validated — an empty `note` appends an empty line.
- Unblocking is not implemented in Heblo; it is done in the Shoptet admin.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `ShoptetOrders:AllowedBlockSourceStateIds` | `[-1, 35, 3, 32, -2]` (`appsettings.json`; code default `[]`) | States from which blocking is allowed |
| `ShoptetOrders:BlockedStatusId` | `35` (`appsettings.json`; code default `0`) | State assigned when blocking |

## Runtime facts
- `PATCH /api/orders/{code}/notes` overwrites `eshopRemark` and round-trips correctly — `docs/integrations/shoptet-api.md` §3.6 — verified 2026-05-25 against the test store.

## Known quirks
- Dead endpoint: no frontend or MCP caller exists, so the feature is unused from the Heblo UI.
- Array config from later providers is merged index-wise (memory note
  `gotcha_config_binder_appends_arrays`, 2026-09-21): a Key Vault/App Settings override with
  fewer than five allowed states keeps the trailing repo entries (overriding indexes 0–1 with
  `26, -2` yields `[26, -2, 3, 32, -2]`).
- History: first version wrote an "internal note" field, then `POST /api/orders/{code}/history`
  (#479), finally the read-modify-write of `eshopRemark` (#525). A concurrent staff edit of the
  remark between the GET and PATCH is lost.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/BlockOrderProcessing/BlockOrderProcessingHandler.cs` — rules
- `backend/src/Anela.Heblo.API/Controllers/ShoptetOrdersController.cs` — `BlockOrder` endpoint
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — Shoptet calls
