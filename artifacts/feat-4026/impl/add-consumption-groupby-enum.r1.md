# Implementation: add-consumption-groupby-enum

## What was implemented
Added a new `ConsumptionGroupBy` enum to the PackingMaterials module's contracts, as task 1 of the 6-task KISS refactor tracked in GitHub issue #4026 that replaces runtime string validation of the "group by" parameter with a compile-time enum. This unit only adds the enum type itself — no callers were retyped or touched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs` — new enum `ConsumptionGroupBy` with values `Material`, `Product`, `Order`, in namespace `Anela.Heblo.Application.Features.PackingMaterials.Contracts`.

## Tests
None — pure addition of an unused enum type, nothing to test yet. The enum is not yet referenced by any request/handler/controller (those are separate follow-up units in the same refactor).

## How to verify
1. `cd backend && dotnet build src/Anela.Heblo.Application` — expect `Build succeeded.` with 0 errors (139 pre-existing warnings unrelated to this change are expected).
2. Inspect the new file at `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs` and confirm it matches the exact content specified in the task.
3. `git log -1 --stat` on branch `feature/4026-Arch-Review-Packingmaterials-Groupby-Parameter-Use` shows a single-file commit adding this enum.

## Notes
No deviations from the task instructions. The enum is currently unused in the codebase by design — subsequent tasks in the #4026 refactor (retyping the request DTO, handler, controller, and updating tests) will consume it. No other files were touched.

## PR Summary
This change adds the `ConsumptionGroupBy` enum (`Material`, `Product`, `Order`) to the PackingMaterials module's Contracts folder, laying the groundwork for the KISS refactor in issue #4026 that will replace runtime string validation of the packing materials consumption "group by" query parameter with a compile-time enum. This is task 1 of 6 in that refactor; no existing request, handler, controller, or test code was modified — the enum is not yet wired up anywhere.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/ConsumptionGroupBy.cs` — new enum with values Material, Product, Order.

## Status
DONE
