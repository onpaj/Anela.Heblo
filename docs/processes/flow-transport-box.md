---
process: flow-transport-box
kind: workflow
module: logistics
summary: Moves finished goods from the manufacturing warehouse (sklad výroby) to the e-shop warehouse in numbered transport boxes; filling consumes manufactured inventory, receiving stages Shoptet stock-up operations, and a 1-minute task closes the box once Shoptet confirms.
owns:
  - backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/RemoveItemFromBox/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/CreateNewTransportBox/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/**
  - backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs
  - backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
  - backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/**
  - backend/src/Anela.Heblo.API/Controllers/TransportBoxController.cs
verified_at: "5e993f9e2"
related:
  - feed-stock-up
---

# Transport boxes (Transportní boxy)

## Purpose
Finished products leave production and travel to the e-shop warehouse in reusable crates
labelled `B001`…`B999` (transport boxes, *transportní boxy*). Heblo tracks what is in each box,
where it is, and makes sure the e-shop stock in Shoptet goes up by exactly the box contents when
the box is received. While a box is on its way its contents count as "in transport" in the
catalog stock (*Zásoby produktů*), so nothing is counted twice or lost in between.

Users: warehouse staff on the mobile terminal (`/terminal/box-fill`, `/terminal/box-check`,
`/terminal/receive`) and in the desktop app (`/logistics/transport-boxes` list + detail,
`/logistics/receive-boxes`). Dashboard tiles "Boxy v přepravě", "Boxy přijaté", "Boxy v chybě".

## Trigger
User-driven; no Hangfire job. One in-process BackgroundRefresh task finishes the flow:
`ITransportBoxCompletionService.CompleteReceivedBoxesAsync` — every 1 min, first run 1:30 after
start, hydration tier 2.

States (`TransportBoxState`, stored as string in `TransportBoxes.State`) and who moves them:

| From | To | Who / where | Notes |
|---|---|---|---|
| — | New | desktop "new box" (`POST /api/transport-boxes`) | no code yet |
| New | Opened | desktop: enter box number (`PUT …/{id}/state` with `BoxCode`) | code must be free; see side effects |
| — | Opened | terminal box-fill: scan box label (`POST …/open-by-code`) | creates the box directly in Opened, or resumes an Opened box with that code |
| Opened | New | desktop "reset" | clears items and code, returns consumed inventory |
| Opened | InTransit | desktop / terminal box-fill "send" | box must have items |
| Opened | Reserve | desktop, with a location (`Kumbal`, `Relax`, `SkladSkla`) | goods parked in a side store |
| Opened | Quarantine | desktop | goods held back |
| InTransit / Reserve / Quarantine | Opened | desktop "revert" | clears location |
| InTransit / Reserve / Quarantine | Received | `/terminal/receive` or `/logistics/receive-boxes` | stages stock-up operations |
| Received | Stocked | **system only** — completion task | all stock-up operations Completed |
| Received | Error | **system only** — completion task | any operation Failed, or none exist |
| Error | Stocked | desktop (edge case, manual) | no re-check of operations |
| Received | Closed | desktop (edge case) | |
| New / Stocked | Closed | desktop; also automatic, see below | |

`InSwap` exists in the enum but no transition uses it. The UI greys out `systemOnly`
transitions (New→Opened via the button, Received→Stocked); the API itself does not refuse them.

## Data flow
1. **Fill** (`AddItemToBoxHandler`, box must be Opened). The item picked on the terminal or desktop
   comes from the manufactured-goods inventory (*sklad výroby*, table
   `ManufacturedProductInventoryItems`). If `SourceInventoryId` is given, Logistics calls the
   Manufacture-owned `ManufactureInventoryReservationAdapter.TryConsumeAsync`, which decrements
   that inventory row (and logs it) unless stock is short; `AllowNegativeStock` (the terminal
   "overdraft" sheet) lets it go below zero. The box row in `TransportBoxItems` is merged with an
   existing one of the same product + lot + expiration + source inventory, else a new row is added.
   Both changes are committed in one `SaveChanges`.
2. **Remove** (`RemoveItemFromBoxHandler`, Opened only): partial or full; the removed amount is
   given back to the source inventory row (`RestoreAsync`). The same restore runs for every item
   when an Opened box is reset to New.
3. **Every state change** appends a row to `TransportBoxStateLogs` (state, time UTC, user, and the
   error text for Error) and sets `LastStateChanged`.
4. **Receive** → `ReceivedSideEffect` groups the box items by product code and stages one
   `StockUpOperations` row per product, `BOX-{boxId:000000}-{productCode}`, amount = sum rounded
   half away from zero to whole pieces, source `TransportBox`. The stock-up rows and the box state
   are saved in the same `SaveChanges`. Pushing them to Shoptet is the `feed-stock-up` process.
5. **Complete** (`TransportBoxCompletionService`, every minute): for every Received box, oldest
   `LastStateChanged` first, read its `TransportBox` stock-up operations: all Completed → Stocked
   (user "System"); any Failed → Error with
   "`N stock-up operation(s) failed. Document numbers: …`"; none at all → Error "No stock-up
   operations found for this box"; still Pending/Submitted → wait for the next tick.
6. **Catalog stock**: Catalog's `ICatalogTransportSource` is implemented here
   (`LogisticsCatalogTransportSourceAdapter`) and read by the Catalog BackgroundRefresh tasks
   `ICatalogRepository.RefreshTransportData` and `RefreshReserveData` (every 5 min). Per product
   code it sums item amounts (each item truncated to int) of boxes in
   - transport = **Opened, InTransit, Received**
   - reserve = Reserve
   - quarantine = Quarantine.

## Logic & formulas
- **Box code**: `B` + 3 digits, case-insensitive, stored upper-case. A code is "occupied" by any
  box not in Closed or Stocked (`TransportBoxStateRules`, the single definition); a second live box
  with the same code is refused (`TransportBoxDuplicateActiveBoxFound`). No DB unique constraint
  exists — the application check is the only guard.
- **Reopening a code on desktop** (New→Opened, `NewToOpenedSideEffect`) also moves every
  Stocked box with that code to Closed. The terminal `open-by-code` path does not.
- **Terminal open-by-code**: an Opened box with that code is resumed; a box in another occupying
  state is an error; otherwise a new box is created directly in Opened.
- Amounts in the box are decimal pieces (`double`, min 0.01 on add); stock-up and catalog
  transport figures are whole pieces (rounded on receive, truncated in the catalog sum).
- Receiving is allowed only from InTransit, Reserve or Quarantine (`isReceivable` on the
  by-code lookup). The by-code lookup also shows each item's e-shop stock from the catalog cache.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `BackgroundRefresh:ITransportBoxCompletionService:CompleteReceivedBoxesAsync` | every 00:01:00, delay 00:01:30, tier 2, enabled | Received → Stocked/Error |
| `BackgroundRefresh:ICatalogRepository:RefreshTransportData` | every 00:05:00, tier 1 | catalog reads the transport pool from here |
| `BackgroundRefresh:ICatalogRepository:RefreshReserveData` | every 00:05:00, tier 1 | catalog reads the reserve + quarantine pools |
| Permission `warehouse.logistics.read` / `.write` | — | `TransportBoxController` read / mutations (`Feature.Warehouse_Logistics`) |

## Runtime facts
None.

## Known quirks
- **An Error box stays Error after its operation is retried.** The completion task reads only
  Received boxes; staff must move it Error → Stocked by hand, and that move does not check the
  operations.
- **Received → Closed skips the Shoptet confirmation.** The staged operations are still pushed
  by `feed-stock-up`; only the box stops waiting for them.
- **A received box still counts as "in transport"** in catalog stock until it is Stocked, and
  Shoptet stock rises as soon as each operation completes — for up to one completion tick plus one
  5-minute catalog refresh the goods can appear in both e-shop stock and transport.
- **Opened boxes count as in transport too**, so goods are in the transport pool while still
  being packed (already deducted from *sklad výroby*).
- **Empty boxes can reach Error.** Only Opened→InTransit checks for items; an empty box sent to
  Reserve or Quarantine and then received stages no operations and goes straight to Error.
- **Old Stocked boxes are only closed by the desktop path.** Reusing a label via the terminal
  leaves the previous Stocked box Stocked indefinitely (harmless for code uniqueness, which
  Stocked releases, but it clutters the list).
- `TransportBox.ConfirmTransit` (re-typed box number check) is never called; sending uses the plain transition.
- `DefaultReceiveState` is stored on every box and exposed in the DTO but never read; `InSwap` is
  never used.
- History: before `TransportBoxStateRules` (issue #3887) a Quarantine box's code could be assigned
  to a second box, and every scan then hit the wrong box — see
  `memory/gotchas/transport-box-code-uniqueness-single-definition.md`.
- `docs/features/complete-received-boxes-job.md` describes the completion step as a job; it is a
  BackgroundRefresh task, not a Hangfire job.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` — state machine, allowed transitions, code format
- `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBoxStateRules.cs` — code occupancy rule
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` — dispatch, side effects, inventory restore on reset
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ReceivedSideEffect.cs` — BOX- stock-up operations
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/NewToOpenedSideEffect.cs` — duplicate check, closing old Stocked boxes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — terminal box-fill entry
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` — consume from sklad výroby
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` — inventory consume/restore
- `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs` — Received → Stocked/Error
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs` — transport/reserve/quarantine pools for Catalog
- `backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs` — queries
- `frontend/src/components/terminal/box-fill/BoxFillWorkflow.tsx`, `frontend/src/components/terminal/TransportBoxReceive.tsx`, `frontend/src/components/pages/TransportBoxList.tsx` — screens
