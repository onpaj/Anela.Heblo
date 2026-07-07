# Implementation: relocate-service-and-rewire-di

## What was implemented
Moved `AzureBlobStorageService` out of the Application layer into the `Anela.Heblo.Adapters.Azure`
project (new namespace `Anela.Heblo.Adapters.Azure.Features.FileStorage`), added a new
`AddAzureBlobStorageService(IServiceCollection, IHostEnvironment)` extension to `AzureAdapterModule`
that owns the `BlobServiceClient` factory (moved verbatim, including the fallback-warning log
message) and the `IBlobStorageService` singleton binding, stripped those same registrations and the
Azure `using`s out of `FileStorageModule`, removed the `Azure.Storage.Blobs` package reference from
`Anela.Heblo.Application.csproj` (it was already present in `Anela.Heblo.Adapters.Azure.csproj`), and
wired the new extension into `Program.cs` immediately after `AddApplicationServices`. Behavior is
byte-for-byte identical — only namespace/location/DI wiring changed, no method bodies were touched.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — moved here via `git mv` from `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`; namespace changed to `Anela.Heblo.Adapters.Azure.Features.FileStorage`, added `using Anela.Heblo.Application.Features.FileStorage;` so `FileStorageModule.FileDownloadClientName` still resolves. No method bodies changed.
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — added `AddAzureBlobStorageService(IServiceCollection, IHostEnvironment)` extension with the `BlobServiceClient` factory (moved verbatim from `FileStorageModule`) and the `IBlobStorageService` singleton binding; added the required `using`s (`Anela.Heblo.Adapters.Azure.Features.FileStorage`, `Anela.Heblo.Application.Features.FileStorage`, `Anela.Heblo.Domain.Features.FileStorage`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging`).
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — removed the `using Anela.Heblo.Application.Features.FileStorage.Services;` and `using Azure.Storage.Blobs;` lines, removed the `BlobServiceClient` factory registration block, and removed the `IBlobStorageService` binding + its comment. File now matches the target content specified in the task exactly (verified line-by-line).
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — removed the `<PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />` line.
- `backend/src/Anela.Heblo.API/Program.cs` — added `builder.Services.AddAzureBlobStorageService(builder.Environment);` immediately after `AddApplicationServices(...)`, before `AddScoped<ISmartsuppWebhookMetrics, ...>()`.

## Tests
None — this task intentionally does not touch tests. Task 2 (`update-filestorage-tests`) updates the
two test files that reference the old namespace/location; the test project (`Anela.Heblo.Tests`) does
not compile until that task runs. Confirmed the production-only build (API project, which transitively
builds Application and Adapters.Azure) succeeds independent of the test project.

## How to verify
```bash
cd backend
# 1. Moved file exists, old path gone
ls src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs
ls src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs  # expect "No such file"

# 2. Production build succeeds
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj   # Build succeeded, 0 Error(s)

# 3. No Azure SDK references left in Application
grep -rn "Azure.Storage\|BlobServiceClient\|BlobContainerClient" src/Anela.Heblo.Application  # empty

# 4. Package reference moved correctly
grep -n "Azure.Storage.Blobs" src/Anela.Heblo.Application/Anela.Heblo.Application.csproj  # empty
grep -n "Azure.Storage.Blobs" src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj  # one match

# 5. Program.cs wiring
grep -n "AddAzureBlobStorageService" src/Anela.Heblo.API/Program.cs  # exactly one match, right after AddApplicationServices

# 6. Formatting clean (no repo-root .sln lives under backend/, so format was run per-project)
dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
dotnet format src/Adapters/Anela.Heblo.Adapters.Azure/Anela.Heblo.Adapters.Azure.csproj --verify-no-changes
dotnet format src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
```
All of the above were run during implementation and passed with no findings/changes.

## Notes
- Deviation from the plan's literal verification command: the plan says to run
  `dotnet format --verify-no-changes` "from `backend/`", but there is no `.sln`/`.csproj` directly in
  `backend/` (the solution file `Anela.Heblo.sln` lives at the repo root and includes the
  `Anela.Heblo.Tests` project, which does not compile until Task 2). Running `dotnet format` against
  that solution would fail to load the workspace due to the test project's compile errors. Instead I
  ran `dotnet format <csproj> --verify-no-changes` individually against the three touched projects
  (`Anela.Heblo.Application`, `Anela.Heblo.Adapters.Azure`, `Anela.Heblo.API`) — all three reported no
  changes needed, satisfying the intent of acceptance criterion 6 without requiring the test project to
  compile.
- The `dotnet build` output includes an unrelated warning: the post-build `AccessMatrixGen` MSBuild
  target (`Anela.Heblo.API.csproj` line 113) exits with code 134 (MSB3073 warning, not an error). This
  is pre-existing tooling behavior in this sandboxed environment, unrelated to this change — the build
  still reports "Build succeeded." / "0 Error(s)".
- `using Microsoft.Extensions.Logging;` was kept in `FileStorageModule.cs` per the task's note (still
  potentially used elsewhere / left as-is for a surgical diff) — `dotnet format` confirmed it is not
  flagged as an unused using, so no further removal was needed.
- The now-empty `Services/` folder under `Anela.Heblo.Application/Features/FileStorage/` was left as-is
  (git does not track empty directories, so there is nothing to clean up).
- `artifacts/feat-3484/state.json` had a pre-existing unstaged modification (present before I started
  any work) — I deliberately did not stage or commit it, per the instruction that `artifacts/` is not
  something this task touches.

## Status
DONE
