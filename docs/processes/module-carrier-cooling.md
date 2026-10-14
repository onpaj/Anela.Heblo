---
process: module-carrier-cooling
kind: module
module: carrier-cooling
summary: Per-carrier cooling rules (carrier × delivery handling → None/L1/L2 + badge text) that decide which parcels get a cooling pack, the "CHLAZENÁ ZÁSILKA" badge on picking lists, the Shoptet CHLAZENE marker and the snowflake at the packing desk.
owns: []
verified_at: "5e993f9e2"
related: []
---

# Carrier cooling (Chlazení)

## Purpose
In warm weather some Anela products must travel with a cooling pack (chladítko). Whether a
parcel gets one depends on two things: **how heat-sensitive the products in it are** (a product
attribute kept in Flexi) and **how the parcel travels** — which carrier and whether it goes to a
box / pick-up point or to the customer's hands. This module stores the second half: a small
settings matrix, edited by expedition staff, that says for each carrier and delivery handling
which product cooling levels get cooled, and what text the cooling badge on the picking list
should say.

The matrix decides, for every order:
- whether the printed picking list (expediční list) shows the frost badge with the cooling text,
- whether Heblo writes the marker `CHLAZENE` into Shoptet additional field 6 of the order,
- whether the packing desk (Balení) shows the big snowflake "Chlazení L1/L2" or "Bez chlazení".

The module itself only reads and writes its own table; all side effects happen in the modules
that consume it (see Dependencies).

### The cooling rule
Each product has a cooling level from Flexi (`None`, `L1`, `L2`). Each matrix row
(carrier × delivery handling) has a carrier cooling level (`None`, `L1`, `L2`). An order is
**cooled** when at least one of its items satisfies

    item.Cooling != None  AND  item.Cooling <= carrierCooling        (None=0, L1=1, L2=2)

| Matrix row set to | Product L1 | Product L2 | Product None |
|---|---|---|---|
| Bez chlazení (None) | not cooled | not cooled | not cooled |
| L1 | **cooled** | not cooled | not cooled |
| L2 | **cooled** | **cooled** | not cooled |

So the row value is a threshold: `L2` cools more products than `L1`. The business meaning of
L1 vs L2 (which products belong where) is not defined in code — it is whatever value staff
enter into Flexi attribute 89 per product. One cooled item makes the whole order cooled.

Rows that do not exist in the table count as `None` (no cooling, default badge text).

## Users & screens
- **Expedition settings → tab Chlazení** — `/customer/expedition-settings?tab=cooling`
  (sidebar item *Nastavení expedice*, next to *Tisk expedice*; `/customer/cooling` redirects here, and the
  weather dashboard tile links to it). Component `CarrierCoolingMatrix`: one card per carrier
  (Zásilkovna, PPL, GLS), one row per delivery handling (*Do ruky* / *Box*), three radio
  buttons (*Bez chlazení*, *L1*, *L2*) and a free-text badge field (placeholder
  `CHLAZENÁ ZÁSILKA`, max 50 chars). Each radio click saves immediately; the text saves on
  blur/Enter; an empty text is saved as null (= default text). No Save button.
  Above the matrix the tab shows the Open-Meteo weather forecast report — that belongs to the
  *weather-forecast* module, not this one; it is purely informational and does not change the
  matrix automatically.
- **API** `CarrierCoolingController` (`api/carrier-cooling`), permission feature
  `Warehouse_Expedition` ("Expedice"):
  - `GET /api/carrier-cooling` (read) → `GetCarrierCoolingMatrixResponse` grouped by carrier.
  - `PUT /api/carrier-cooling` (write access) with `{carrier, deliveryHandling, cooling, coolingText}`.
- No dashboard tile and no MCP tool of its own.

### Which rows exist
The matrix rows are not stored configuration — they are derived on every request from the
hard-coded Shoptet shipping method list (`ShippingMethodRegistry`, via
`IShippingMethodCatalog.GetAvailableDeliveryOptions()`):
- personal pickup (`Osobak`) and every method whose name contains `_EXPORT` are excluded;
- delivery handling is derived from the method name (`ShippingMethodRegistry.ResolveDeliveryHandling`):
  `DO_RUKY` → **NaRuky** (Do ruky); `PARCELSHOP`, `ZPOINT`, `BOX`, `VYDEJNI` → **Box**;
  anything else → no handling (never cooled).

Result today: 6 rows — Zásilkovna, PPL, GLS × NaRuky, Box. The legacy methods, the 2025+
"box & výdejní místa / do ruky" methods and the `…_CHLAZENY` methods of the same carrier and
handling all map to the same row.

## Processes
This module has no process docs: it has no scheduled job, no sync and no external side effect
of its own. Its only user actions are plain CRUD on its own table:
- view the matrix (`GetCarrierCoolingMatrixHandler`) — lists the available rows, filling in the
  stored value or `None` / no text;
- set one row (`SetCarrierCoolingHandler`) — checks the user is signed in (else `Unauthorized`)
  and that the carrier × handling pair is in the available list (else `ValidationError`), then
  upserts the row with `ModifiedBy` = current user id and `ModifiedAt` = UTC now.
  The change applies from the next picking list print / packing scan; nothing is recomputed.

How the matrix is consumed (documented by the owning modules, described here so a reader can
follow the whole chain):

1. **Picking list print** (*expedition-list* module: Hangfire job `print-picking-list` and the
   manual "print expedition list / single order" actions, all through
   `ShoptetApiExpeditionListSource.CreatePickingList`):
   - loads the whole matrix once per run (`ICarrierCoolingRepository.GetAllAsync`);
   - for each order maps `shipping.guid` (Shoptet `GET /api/orders/{code}?include=stockLocation,notes`)
     → shipping method → (carrier, handling) → row → `ExpeditionOrder.CarrierCooling` and
     `ExpeditionOrder.CoolingText`; unknown GUID or no handling → `None` / null;
   - item cooling comes from the Heblo catalog (`CatalogAggregate.Properties.Cooling`);
   - if `IsCooled`, the PDF (`ExpeditionProtocolDocument`) prints a frost-icon badge next to the
     order number with `CoolingText`, or `CHLAZENÁ ZÁSILKA` when the text is empty;
   - after each PDF batch is written, every cooled order gets Shoptet additional field 6 set to
     `CHLAZENE` (`PATCH /api/orders/{code}/notes`, `PickingListBatchProcessor.WriteCoolingMarkersAsync`).
     A failure there is logged as a warning and the print continues.
2. **Packing desk (Balení)** (*packaging* / *shoptet-orders* modules: `POST /api/packaging/orders/{code}/scan`
   and `GET /api/shoptet-orders/{code}/packing`, both via `ShoptetApiPackingOrderClient.GetPackingOrderAsync`):
   - reads the matrix through the cross-module contract `IPackingCarrierCoolingSource`
     (implemented here by `CarrierCoolingPackingCarrierCoolingAdapter`, which passes carrier and
     handling as enum **names** and the level, but not the badge text);
   - computes the same `CarrierCooling` / `IsCooled` as the picking list, with item cooling from
     the catalog via `IPackingProductSource`;
   - the screen (`PackingCoolingIndicator`) shows a large snowflake and "Chlazení {level}" when
     cooled, else a small "Bez chlazení" label. The level shown is the **row's** level, not the
     product's.

## Data owned
- `public."CarrierCoolingSettings"` — one row per (carrier, delivery handling) that a user has
  ever set. Composite key `Carrier` + `DeliveryHandling` (stored as enum names, e.g. `PPL`,
  `NaRuky`), `Cooling` (`None`/`L1`/`L2` as text), `CoolingText` (nullable, varchar 50),
  `ModifiedAt` (`timestamp without time zone`, UTC), `ModifiedBy` (user id, varchar 200).
  No history — an update overwrites the row. Migrations `AddCarrierCoolingSettings`
  (2026-05-18) and `AddCoolingTextToCarrierCoolingSettings` (2026-06-01).

## External systems
None directly. The module never calls Shoptet or Flexi. Indirectly:
- **Shoptet** — the list of shipping methods it offers rows for is a hard-coded copy of the
  anela.cz shipping methods (`ShippingMethodRegistry`, see `docs/integrations/shoptet-api.md`);
  the consumers above read orders from and write the `CHLAZENE` marker to Shoptet.
- **Flexi** — product cooling levels come from Flexi user query 38, attribute id 89
  (`FlexiProductAttributesQueryClient`, values parsed case-insensitively as `None`/`L1`/`L2`,
  anything else → `None`), merged into the catalog by the *catalog* module.

## Dependencies
- **Reads from:** *shoptet-api adapter* `IShippingMethodCatalog` / `ShippingMethodRegistry`
  (which rows exist); *users* `ICurrentUserService` (who changed a row).
- **Read by:** *expedition-list* (picking list PDF badge + Shoptet `CHLAZENE` marker, via
  `ICarrierCoolingRepository` directly) and *packaging* / *shoptet-orders* (packing desk
  snowflake, via `IPackingCarrierCoolingSource`).
- **Combined with:** *catalog* product cooling level (Flexi attribute 89). Both halves are needed
  — a matrix set to L2 does nothing for products whose Flexi attribute is empty.
- **Shown next to:** *weather-forecast* (Open-Meteo report on the same tab).

## Known quirks
- **Validator is never registered.** `CarrierCoolingModule` registers
  `ValidationBehavior<SetCarrierCoolingRequest, …>` but not `SetCarrierCoolingValidator`, and the
  project has no assembly scan, so the validator never runs (only its unit test uses it). The
  handler re-checks the carrier × handling pair, so that rule still holds, but the 50-character
  limit on `CoolingText` is enforced only by the UI `maxLength` and the DB column: a longer text
  sent straight to the API fails in `SaveChanges` (server error) instead of a validation error.
  Found 2026-10-02 while writing this doc.
- **The matrix cannot tell a paid "chlazený balík" from a normal shipment.** `…_CHLAZENY`
  shipping methods (e.g. `PPL_DO_RUKY_CHLAZENY`, `ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA`) resolve to
  the same carrier × handling row as the plain method, so cooling follows the matrix and the
  products, not the customer's choice of a cooled shipping method.
- **Export and personal pickup are never cooled.** Methods with `_EXPORT` in the name (incl.
  `PPL_EXPORT_CHLAZENY`) and `OSOBAK` resolve to no delivery handling, so they have no matrix row
  and are always `None`.
- **Packing desk ignores the badge text.** `IPackingCarrierCoolingSource` carries only the level;
  `CoolingText` appears only on the printed picking list.
- **Stale rows stay in the table.** If a carrier/handling pair disappears from
  `ShippingMethodRegistry`, its stored row is hidden from the screen but remains in the table;
  harmless today because consumers look rows up by pair.
- **The matrix is not seasonal.** Nothing switches it automatically with the weather; staff
  change the radios by hand (the forecast above the matrix is only a hint).
- Empty or whitespace text means "use the default"; the PDF falls back to `CHLAZENÁ ZÁSILKA`
  whenever `CoolingText` is null or whitespace.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs` — DI, cross-module adapter registration
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs` — how the screen's rows are built
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` — save path and its checks
- `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs` — what the packing desk receives
- `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs` — entity; `Carriers.cs`, `DeliveryHandling.cs`, `../../Shared/Cooling.cs` — enums
- `backend/src/Anela.Heblo.Persistence/Logistics/CarrierCooling/` — table mapping and upsert
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — endpoints and permissions
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs` / `ShippingMethodCatalog.cs` — which rows exist, name → handling
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` — `IsCooled` rule
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` — `ResolveCarrierCooling` / `ResolveCarrierCoolingText` (consumer)
- `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`, `frontend/src/components/customer/expeditionSettings/CoolingTab.tsx` — the screen
