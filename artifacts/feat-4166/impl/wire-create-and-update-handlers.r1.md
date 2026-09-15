# Implementation: wire-create-and-update-handlers

## What was implemented

Refactored `CreatePackingMaterialHandler` and `UpdatePackingMaterialHandler` to build
their `PackingMaterialDto` via the shared `PackingMaterialMapper.ToDto` helper (added in
the prior `add-packing-material-mapper` task) instead of manually constructing the DTO
field-by-field, per the task-context spec.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs` — added the mapper's `using`, replaced the manual `PackingMaterialDto` construction with `PackingMaterialMapper.ToDto(createdMaterial, forecastedDays: null)`.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` — same change, `PackingMaterialMapper.ToDto(material, forecastedDays: null)`.

No test files were added — per the task-context, the existing
`PackingMaterialCrudHandlerTests.UpdatePackingMaterial_UpdatesMaterialAndReturnsSuccess_WhenMaterialExists`
test already asserts `response.Material.Id`/`.Name` on the updated DTO and serves as the
regression guard; `CreatePackingMaterialHandler` has no dedicated handler test and this
task does not add one (out of scope, pure refactor).

## How to verify

```
cd /path/to/repo
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests" --no-build
```
Result: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8` — same test count/names as
before this task's edits, confirming no behavior change.

Also verified:
- `dotnet build Anela.Heblo.sln` — 0 errors, 0 warnings.
- `dotnet format Anela.Heblo.sln --no-restore --verify-no-changes` — no files flagged
  (the spec's note about a possibly-unused `Enums` using did not apply; both files still
  reference `ConsumptionType`/enum members elsewhere).
- `dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~PackingMaterials" --no-build`
  — `Passed! - Failed: 0, Passed: 80, Skipped: 0, Total: 80` (full PackingMaterials suite,
  spans all tasks completed so far in this plan).

## Notes

`UpdatePackingMaterialQuantityHandler` and `GetPackingMaterialsListHandler` are out of
scope for this task and are covered by the next `wire-quantity-and-list-handlers` task.
No deviations from the task-context spec.

## Notes on recovery

This task's code edits (both handler files) had already been implemented correctly by a
prior session that died before writing this summary, running the reviewer step, or
committing — its uncommitted working-tree edits matched the task-context spec verbatim.
This pass verified that diff against the spec, then ran the full verification suite
(targeted regression test, build, format, full PackingMaterials suite) to confirm, and is
now completing the developer-task output contract the prior attempt never finished.

## PR Summary

Wires `CreatePackingMaterialHandler` and `UpdatePackingMaterialHandler` to the shared
`PackingMaterialMapper`, removing two of the four duplicated manual DTO-construction
sites identified in the arch-review finding.

### Changes
- `CreatePackingMaterialHandler.cs` — use `PackingMaterialMapper.ToDto`
- `UpdatePackingMaterialHandler.cs` — use `PackingMaterialMapper.ToDto`

## Status
DONE
