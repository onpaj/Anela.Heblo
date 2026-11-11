# Implementation: create-filestorage-contracts-folder

## What was implemented
Moved `DownloadFromUrlRequest` and `DownloadFromUrlResponse` out of FileStorage's
internal `UseCases/DownloadFromUrl/` namespace into a new
`Application/Features/FileStorage/Contracts/` folder/namespace, matching the
`Contracts/` convention already used by Manufacture, Catalog, CatalogDocuments,
Journal, Marketing, and PackingMaterials. Repointed every production-code `using`
that referenced the old namespace. This is a namespace/folder-move only — no
logic, field, or DI wiring changes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlRequest.cs` — moved via `git mv` from `UseCases/DownloadFromUrl/`, namespace updated to `...FileStorage.Contracts`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Contracts/DownloadFromUrlResponse.cs` — moved via `git mv` from `UseCases/DownloadFromUrl/`, namespace updated to `...FileStorage.Contracts`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — added `using Anela.Heblo.Application.Features.FileStorage.Contracts;` (handler itself stays in its original namespace)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Validators/DownloadFromUrlRequestValidator.cs` — `using` repointed to `Contracts`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — `using` repointed to `Contracts`
- `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs` — `using` repointed to `Contracts`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` — `using` repointed to `Contracts` (the cross-module Catalog caller identified in the issue)

## Tests
No test files touched in this task — the five test files still referencing the
old namespace are updated in the next task
(`update-tests-for-contracts-namespace`), per the task-context split.

## How to verify
```bash
ls backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/
# -> only DownloadFromUrlHandler.cs

grep -rn "using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;" backend/src/
# -> no matches

dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
# -> Build succeeded, 0 Error(s) (154 pre-existing warnings, none new)
```

## Notes
- The handler's own namespace declaration (`Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl`) is unchanged by design — only the DTOs moved, per the task spec.
- Test project was intentionally not built in this task; it is covered by the next task in the plan.
- Followed the task-context file exactly; no deviations.
