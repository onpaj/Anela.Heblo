---
process: flow-product-composition-order
kind: workflow
module: catalog
summary: Staff reorder a product's recipe ingredients and tag them with phase letters on the catalog Composition tab; Heblo writes the order and phase straight into the Flexi bill of materials (kusovník).
owns:
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/**
verified_at: "5e993f9e2"
related: []
---

# Recipe ingredient order and phases (Flexi BoM)

## Purpose
A recipe's ingredient list is used on the shop floor in a fixed sequence, often in phases
(A = water phase, B = oil phase, …). Staff set that sequence once in Heblo; it is stored in Flexi
on the BoM lines so every consumer sees the same order — the catalog Composition tab, the
manufacture batch calculator and the semi-product recipe PDF (both in the Manufacture module).
The MCP tool `GetProductComposition` returns the same list.

## Trigger
User action on the catalog detail → **Composition** tab (`CompositionTab.tsx`): drag ingredients
into order, optionally type a phase letter, save. Request
`PUT /api/Catalog/{productCode}/composition/order` with `order[] = {ingredientProductCode,
sortOrder, phaseLabel}`; permission Products_Catalog write. Reading:
`GET /api/Catalog/{productCode}/composition`.

## Data flow
1. Load the product's manufacture template from Flexi (`IManufactureClient
   .GetManufactureTemplateAsync`, Flexi `kusovnik`); none → response `UpdatedCount = 0`, nothing
   written.
2. Map each ingredient code to its BoM line id; codes not in the BoM are skipped (logged).
3. For each remaining line, **one Flexi call per line** (`IBoMClient.UpdateBoMItemAsync`): set the
   line order to `sortOrder` and the line's `nameC` field to the phase letter (empty string when
   none).
4. Invalidate Heblo's cached template for that product so the next read shows the new order.
5. Response: number of lines updated.

## Logic & formulas
- Phase label: trimmed and upper-cased; kept only if it is a single letter A–Z, otherwise sent
  as empty (clears the phase).
- Validation: product code ≤ 50 chars; ingredient codes unique within the request, each ≤ 50
  chars.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| Flexi connection (`FlexiBeeSettings`) | secrets | ERP API |

## Runtime facts
None.

## Known quirks
- **Not atomic.** Lines are updated one by one; a failure half-way leaves Flexi with a partly
  reordered BoM and the request fails — save again to finish.
- **Live ERP, no undo.** The previous order/phase is not stored anywhere in Heblo.
- **The phase lives in a Flexi name field** (`nameC` on the BoM line), so anyone editing that
  field in Flexi changes the phase Heblo shows.
- An ingredient code missing from the BoM is silently skipped; `UpdatedCount` is the only hint.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateProductCompositionOrder/UpdateProductCompositionOrderHandler.cs` — mapping, phase normalisation
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Manufacture/FlexiManufactureClient.cs` — `SetBomItemsOrderAndPhaseAsync`, `GetManufactureTemplateAsync`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductComposition/GetProductCompositionHandler.cs` — read side
- `frontend/src/components/catalog/detail/tabs/CompositionTab.tsx` — UI
