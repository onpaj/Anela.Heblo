## Module
Transport Boxes (Logistics)

## Finding
"A box code must be unique among active (non-terminal) boxes" is enforced by two independent, unreconciled checks:

- `TransportBoxRepository.IsBoxCodeActiveAsync` (`backend/src/Anela.Heblo.Persistence/Logistics/TransportBoxes/TransportBoxRepository.cs:96-115`) defines "active" as an explicit allow-list — `{ New, Opened, InTransit, Received, Reserve }` (lines 98-105). **`Quarantine` and `Error` are missing from this list.** This is the check `ChangeTransportBoxStateHandler.HandleNewToOpened` uses (`ChangeTransportBoxStateHandler.cs:227-237`) when a box is assigned its code via the New→Opened transition — a path independently reachable from `TransportBoxDetail.tsx`'s `handleBoxNumberSubmit` (lines 172-203), not just from barcode scanning.
- `OpenOrResumeBoxByCodeHandler` (the barcode-scan flow used by the Warehouse Terminal) defines "busy" the opposite way — a deny-list: anything **except** `Closed`/`Stocked` (`OpenOrResumeBoxByCodeHandler.cs:62`). This correctly treats `Quarantine` and `Error` boxes as busy.
- There is no DB-level uniqueness constraint on `Code` (`TransportBoxConfiguration.cs` has none) — the application-layer check is the only thing preventing two simultaneously-active boxes sharing a code.

## Why it matters (concrete failure scenario)
1. Box A holds code `B001` in `Quarantine` state (e.g. QA hold).
2. An operator opens a new box and assigns it code `B001` from `TransportBoxDetail.tsx` (New→Opened transition).
3. `HandleNewToOpened` calls `IsBoxCodeActiveAsync("B001")` → `false` (Quarantine isn't in the active-state list) → assignment **succeeds**. Two `TransportBox` rows now share code `B001`.
4. Warehouse staff scan barcode `B001` at the terminal. `GetByCodeAsync` (`TransportBoxRepository.cs:117-131`, ordered non-Closed-first then by `Id` descending) returns the newer, unrelated box — not the quarantined one the physical label refers to.
5. All subsequent scan-driven actions (fill, receive) silently apply to the wrong aggregate. No error is ever raised.

This is a duplicated invariant that has already drifted: `Quarantine` was added to `TransportBoxState` without updating `IsBoxCodeActiveAsync`'s active-state allow-list, while the sibling deny-list check in `OpenOrResumeBoxByCodeHandler` was written correctly. The two enforcement points can desync again the next time a state is added, because neither derives from a single shared definition.

## Suggested direction
Define "is this box in a non-terminal/active state" once (e.g. a predicate on `TransportBoxState`, or compose the existing `TransportBox.IsInTransit`/`IsInReserve`/`IsInQuarantine` properties) and have both `IsBoxCodeActiveAsync` and `OpenOrResumeBoxByCodeHandler` consume that single definition.
