# Implementation: retype-handler-groupby-dispatch

## What was implemented
Updated `GetDailyConsumptionBreakdownHandler` to dispatch on the `ConsumptionGroupBy`
enum instead of a runtime-validated string. Removed the now-dead `ValidGroupByValues`
field and the unreachable string-validation block at the top of `Handle` (invalid
`groupBy` values are now rejected at ASP.NET Core model binding, before `Handle` runs).
Replaced the string-based `switch` used for dispatch with a `switch` on the
`ConsumptionGroupBy` enum, and updated the four remaining places that put `GroupBy` into
`GetDailyConsumptionBreakdownResponse` to call `.ToString()` since the response DTO's
`GroupBy` property remains a `string`. `BuildGroupByMaterial`, `BuildGroupByProduct`, and
`BuildGroupByOrder` were left untouched, as specified.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs` — removed `ValidGroupByValues` field and the runtime validation block; changed the dispatch switch to match on `ConsumptionGroupBy` (throwing `ArgumentOutOfRangeException` for the unreachable default); changed the empty-consumptions early return, the success return, and the catch-block error return to use `request.GroupBy.ToString()`.

## Tests
None for this task (mechanical retype only; no test files were touched).

## How to verify
Run `dotnet build backend/src/Anela.Heblo.Application` from the worktree root.
Expected: `Build succeeded.` with 0 errors and no new warnings (139 pre-existing
warnings remain, none referencing this file).

## Notes
No deviations from the task spec. `ConsumptionGroupBy` resolves without a new `using`
because it lives in `Anela.Heblo.Application.Features.PackingMaterials.Contracts`,
already imported at the top of the file.

## PR Summary
Retypes `GetDailyConsumptionBreakdownHandler`'s dispatch logic to switch on the
`ConsumptionGroupBy` enum and drops the now-unreachable string validation, converting
back to `string` only at the response-DTO boundary via `.ToString()`.

## Status
DONE
