All 9 tasks complete. Here is the implementation summary:

---

# Implementation: Relocate FileSystemPrintQueueSink to Adapter Project

## What was implemented

Moved `FileSystemPrintQueueSink` out of `Anela.Heblo.Application` (where it violated Clean Architecture by performing I/O) into a new dedicated `Anela.Heblo.Adapters.FileSystem` adapter project, matching the placement pattern of the Azure and CUPS sink siblings. Zero behavior change — pure relocation with namespace update.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj` — new adapter project (net8.0, nullable, implicit usings, references Application only)
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs` — relocated class, namespace updated, body byte-identical
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs` — new DI extension: `AddFileSystemPrintQueueSink()` registers as Scoped
- `Anela.Heblo.sln` — new project added under existing "Adapters" solution folder
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — added FileSystem adapter project reference
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — line 428 swapped from inline `AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>()` to `AddFileSystemPrintQueueSink()`; using directives updated
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — added FileSystem adapter project reference
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` — using updated
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — using updated
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` — added FileSystem adapter project reference
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` — using updated
- `docs/architecture/filesystem.md` — one-line placement rule bullet added under Application Layer section
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` — **deleted**

## Tests

- `FileSystemPrintQueueSinkTests` (3 tests) — all pass after namespace update
- `CombinedPrintQueueSinkRegistrationTests` — all pass after namespace update
- `Anela.Heblo.Tests` full suite — 4689 passed (38 pre-existing TestContainers/Docker failures unrelated to this change)
- Shoptet test project builds cleanly

## How to verify

```bash
dotnet build Anela.Heblo.sln --nologo           # 0 errors
cd backend && dotnet format --verify-no-changes --no-restore && cd ..   # 0 changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
grep -rn "FileSystemPrintQueueSink" backend/    # exactly 5 source files
```

## Notes

- The `dotnet sln add --solution-folder Adapters` command created a duplicate "Adapters" folder; was caught by code quality review and corrected by editing the `.sln` file to use the existing folder GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`.
- Spec incorrectly named the config keys (`PrintQueue:Mode` → actual `ExpeditionList:PrintSink`) and the solution file location (`backend/Anela.Heblo.sln` → actual `Anela.Heblo.sln` at repo root); all arch-review amendments were applied.
- The 38 test failures in `dotnet test` are pre-existing TestContainers/PostgreSQL Docker infrastructure failures — not introduced by this change.

## PR Summary

Relocated `FileSystemPrintQueueSink` from the Application layer to a new dedicated `Anela.Heblo.Adapters.FileSystem` adapter project to fix a Clean Architecture layering violation — the Application layer should not perform filesystem I/O directly. All `IPrintQueueSink` implementations now live in the outer adapter ring, consistent with the Azure and CUPS siblings.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/` — new adapter project with the relocated sink class and a `AddFileSystemPrintQueueSink()` DI extension
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` — deleted
- `backend/src/Anela.Heblo.API/` — project reference added; composition root `default` branch updated to call the extension method
- `backend/test/Anela.Heblo.Tests/` and `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/` — project references added; using directives updated to new namespace
- `docs/architecture/filesystem.md` — placement rule documented under the Application Layer section

## Status
DONE