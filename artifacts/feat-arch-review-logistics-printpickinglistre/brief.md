## Module
Logistics

## Finding
`PrintPickingListRequest` and `PrintPickingListResult` live in `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` (lines 1–26 and 1–10 respectively), but both are operation-level DTOs with application and infrastructure concerns:

- `PrintPickingListRequest` carries `SendToPrinter` (I/O decision), `ChangeOrderState` (workflow side-effect), and `DefaultCarriers` (application configuration) — none of these are domain concepts.
- `PrintPickingListResult` returns `ExportedFiles` (file paths) and `OrderIds` — output data for a use case, not a domain value object.
- `IPickingListSource` (same folder, line 1–9) depends on both types and therefore also anchors application behaviour in the domain.

Domain should contain entities, value objects, aggregate roots, and pure domain service ports. Request/response shapes for application operations belong in the Application layer.

## Why it matters
Placing application-layer types in Domain inverts the dependency rule: the domain ends up knowing about application-specific flags (`SendToPrinter`, `ChangeOrderState`). Any future infrastructure change (e.g. switching from file-based to queue-based printing) would force edits to Domain, which should be the most stable layer.

## Suggested fix
Move `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` to `backend/src/Anela.Heblo.Application/Features/Logistics/` (under `Contracts/` or a `Picking/` subfolder). Update the single namespace reference in any handler that currently imports them from the Domain path.

---
_Filed by daily arch-review routine on 2026-05-28._