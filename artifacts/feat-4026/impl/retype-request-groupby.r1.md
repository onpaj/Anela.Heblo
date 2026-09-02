# Implementation: retype-request-groupby

## What was implemented
Retyped `GetDailyConsumptionBreakdownRequest.GroupBy` from `string` to the `ConsumptionGroupBy` enum (added in the previous task), with a default of `ConsumptionGroupBy.Material`. This is task 2 of 6 in the #4026 KISS refactor. As documented in the task spec, this intentionally leaves the Application project in a known-failing build state — `GetDailyConsumptionBreakdownHandler.cs` still treats `request.GroupBy` as a `string` and will be fixed in the next task (`retype-handler-groupby-dispatch`).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownRequest.cs` — `GroupBy` property retyped from `string` (default `"material"`) to `ConsumptionGroupBy` (default `ConsumptionGroupBy.Material`); added `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;`.

## Tests
None in this task — the file has no independent behavior to test (it's a DTO property type change). The follow-up tasks (`retype-handler-groupby-dispatch`, `update-groupby-tests`) cover the resulting behavior.

## How to verify
1. `dotnet build backend/src/Anela.Heblo.Application` — expected to **fail** at this point in the plan, with exactly 9 errors, all in `GetDailyConsumptionBreakdownHandler.cs` (lines 31, 38, 49, 53, 55, 56, 57, 65, 78), all `ConsumptionGroupBy`/`string` type-mismatch errors (`CS1503`, `CS0029`, `CS7036`). No errors in any other file.
2. Confirm the request file's `GroupBy` property is `ConsumptionGroupBy GroupBy { get; set; } = ConsumptionGroupBy.Material;`.
3. `git log -1 --stat` on the branch shows a single-file commit.

## Notes
No deviations. Verified the build fails with exactly the expected error set (9 errors, all confined to `GetDailyConsumptionBreakdownHandler.cs`, all type-mismatch errors from the `ConsumptionGroupBy` retype) — nothing unrelated broke. Per the task spec, this intermediate failing-build commit is acceptable because the fix lands in the very next task within the same PR before merge.

## PR Summary
Retypes `GetDailyConsumptionBreakdownRequest.GroupBy` from `string` to the new `ConsumptionGroupBy` enum, as task 2 of 6 in the #4026 KISS refactor replacing runtime string validation of the packing materials "group by" query parameter with compile-time enum safety. This intentionally leaves `GetDailyConsumptionBreakdownHandler.cs` failing to build — it still treats `GroupBy` as a `string` — which the next task in this same PR fixes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownRequest.cs` — `GroupBy` retyped to `ConsumptionGroupBy`.

## Status
DONE
