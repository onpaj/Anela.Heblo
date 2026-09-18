# Implementation: relocate-get-packing-materials-list

## What was implemented

Co-located the `GetPackingMaterialsListRequest`/`GetPackingMaterialsListResponse` MediatR
types with their handler under `UseCases/GetPackingMaterialsList/`, removing them from the
shared `Contracts/` folder, per the task-context steps.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs` — new file, `GetPackingMaterialsListRequest : IRequest<GetPackingMaterialsListResponse>`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs` — new file, `GetPackingMaterialsListResponse : BaseResponse` with `Materials` list
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/GetPackingMaterialsListRequest.cs` — deleted (old combined Request+Response file)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` — removed now-unused `using ...Contracts;`
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — added `using ...UseCases.GetPackingMaterialsList;`, kept the existing `Contracts` using (still needed for other types)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs` — removed now-unused `using ...Contracts;`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` — removed now-unused `using ...Contracts;`

## Tests

No new tests were needed — this is a pure namespace/file relocation with no behavior change.
Existing tests exercise the moved types:
- `GetPackingMaterialsListHandlerTests.cs`
- `PackingMaterialsListQueryCountTests.cs`

## How to verify

```bash
export MSBUILDDISABLENODEREUSE=1
dotnet build Anela.Heblo.sln            # 0 errors
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~PackingMaterialsListQueryCountTests|FullyQualifiedName~GetPackingMaterialsListHandlerTests" -p:UseSharedCompilation=false
```

Result: `dotnet build` succeeded with 0 errors (pre-existing warnings only, unrelated to this
change). `dotnet test` with the filter above: Total tests: 4, Passed: 4
(`GetPackingMaterialsListHandlerTests` x3, `PackingMaterialsListQueryCountTests` x1).
`dotnet format --include <touched files>` reported no changes needed.

## Notes

- The repo's `Anela.Heblo.API.csproj` has a `GenerateAccessMatrix` MSBuild target
  (`BeforeTargets="Build"`, Debug-only) that shells out to a nested `dotnet run` via an
  `<Exec>` task. Without `MSBUILDDISABLENODEREUSE=1` set for the whole process tree, that
  nested `dotnet run` spawns its own MSBuild/VBCSCompiler node-reuse server that keeps the
  Exec task's stdout pipe open, so the parent build hangs forever waiting for EOF. Always
  set `MSBUILDDISABLENODEREUSE=1` (and ideally `-p:UseSharedCompilation=false`) before
  running `dotnet build`/`dotnet test` against this solution in this environment.
- No deviations from the task-context plan.

## PR Summary
Moved the `GetPackingMaterialsListRequest`/`GetPackingMaterialsListResponse` MediatR types out of the shared `PackingMaterials/Contracts/` folder into their own `UseCases/GetPackingMaterialsList/` folder, alongside the handler that owns them — per issue #4164's architecture finding. Pure relocation, no behavior change; the controller and both affected test files were updated to point at the new namespace and drop the now-unused `Contracts` using.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListRequest.cs` — new
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListResponse.cs` — new
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/GetPackingMaterialsListRequest.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` — dropped unused using
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — added using for the new namespace
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs` — dropped unused using
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` — dropped unused using

## Status
DONE
