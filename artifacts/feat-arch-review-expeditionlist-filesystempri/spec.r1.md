```markdown
# Specification: Move FileSystemPrintQueueSink to a Dedicated Adapter Project

## Summary
Relocate `FileSystemPrintQueueSink` out of the Application layer into a new `Anela.Heblo.Adapters.FileSystem` adapter project to align with Clean Architecture boundaries and match the placement pattern already used by the Azure and CUPS sink implementations. This is a pure restructuring change with no behavior or configuration surface changes.

## Background
`backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` is the only concrete `IPrintQueueSink` implementation that currently lives in the Application layer. It performs direct filesystem I/O (`Directory.CreateDirectory`, `File.Copy`, `Path.Combine`, `Path.GetFileName`) — infrastructure concerns that, per Clean Architecture and the project's own `docs/architecture/filesystem.md`, must live in an outer adapter ring.

The two sibling sinks are already placed correctly:
- `AzureBlobPrintQueueSink` → `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- `CupsPrintQueueSink` → `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`

The filesystem sink is the lone outlier. It was caught by the daily arch-review routine on 2026-06-07. Fixing the inconsistency removes a layering violation, makes the Application project free of I/O code, and ensures a future maintainer scanning Application sees only orchestration, not file copies.

## Functional Requirements

### FR-1: Create new `Anela.Heblo.Adapters.FileSystem` adapter project
Add a new .NET project at `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/` with the same shape as `Anela.Heblo.Adapters.Cups` (smallest, self-contained sibling): `net8.0`, nullable enabled, implicit usings enabled, root namespace `Anela.Heblo.Adapters.FileSystem`. It references `Anela.Heblo.Application` so it can implement `IPrintQueueSink` and consume `PrintPickingListOptions`.

**Acceptance criteria:**
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj` exists with `<TargetFramework>net8.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<RootNamespace>Anela.Heblo.Adapters.FileSystem</RootNamespace>`.
- The project references `..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj` and only the `Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Logging.Abstractions` packages it actually needs (no Azure, CUPS, or other adapter dependencies).
- The project is added to the solution file `backend/Anela.Heblo.sln` under the existing `Adapters` solution folder.
- `dotnet build` succeeds for the whole solution.

### FR-2: Move `FileSystemPrintQueueSink` into the new adapter
Move the class from `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` to `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs`. Change the namespace to `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList`. The class body — constructor signature, `SendAsync` implementation, logging behavior, and the `PrintQueueFolder` option lookup — must remain byte-identical aside from namespace and using directives.

**Acceptance criteria:**
- File no longer exists at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`.
- File exists at `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs` with namespace `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList`.
- The class still implements `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink`.
- `SendAsync` behavior is unchanged: warns and returns when `PrintQueueFolder` is empty, otherwise creates the directory and copies each input file by its file name. No new try/catch, no new normalization, no new logging.
- `IPrintQueueSink` and `PrintPickingListOptions` remain in `Anela.Heblo.Application/Shared/Printing/` — they are not moved.

### FR-3: Register the sink via an adapter extension method
Following the pattern set by `Anela.Heblo.Adapters.Cups` (`CupsAdapterServiceCollectionExtensions`) and `Anela.Heblo.Adapters.Azure` (`AddAzurePrintQueueSink`), expose a `AddFileSystemPrintQueueSink(this IServiceCollection services)` extension in the new adapter project. The composition root (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`) calls this extension in the `default` branch of the `PrintQueue:Mode` switch instead of registering `FileSystemPrintQueueSink` directly.

**Acceptance criteria:**
- A public static class (e.g., `FileSystemAdapterServiceCollectionExtensions`) in `Anela.Heblo.Adapters.FileSystem` namespace exposes `AddFileSystemPrintQueueSink(this IServiceCollection services)`.
- The extension registers `IPrintQueueSink` → `FileSystemPrintQueueSink` as `Scoped`, matching the prior registration lifetime.
- `Anela.Heblo.API` adds a project reference to `Anela.Heblo.Adapters.FileSystem` (it already references the Azure and Cups adapters).
- The `default` branch in `ServiceCollectionExtensions.cs:427-429` calls `services.AddFileSystemPrintQueueSink();` instead of `services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();`.
- The Combined mode does not need changes — it does not use `FileSystemPrintQueueSink`.

### FR-4: Update test references
The three test files that reference `FileSystemPrintQueueSink` must be updated to consume the new namespace. Test logic stays unchanged; only `using` directives change.

**Acceptance criteria:**
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` imports the type from `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList`. Assertions and arrange/act sections are unchanged.
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` updates its using (or fully-qualified reference) to the new namespace.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` updates its using to the new namespace.
- `Anela.Heblo.Tests.csproj` adds a project reference to `Anela.Heblo.Adapters.FileSystem` (since the unit tests instantiate the type directly).
- All previously passing tests still pass: `dotnet test` is green.

### FR-5: Optional location guard against regression
Leave a single-line note in `docs/architecture/filesystem.md` (or the closest existing location-rules doc) recording that all `IPrintQueueSink` implementations belong in adapter projects under `backend/src/Adapters/`. This is a documentation-only addition; skip if a sufficient note already exists.

**Acceptance criteria:**
- Either the docs already say this and no change is required, OR a one-line note is added in the appropriate section.
- No further enforcement (analyzers, ArchUnit-style tests) is in scope.

## Non-Functional Requirements

### NFR-1: Behavior parity
Zero runtime behavior change. Same DI lifetime (Scoped). Same `PrintQueueFolder` configuration key. Same log messages and severity. Same exception surface (filesystem exceptions still bubble up — no new swallow/rethrow). Same throughput characteristics (synchronous `File.Copy` in a `Task.CompletedTask`-returning method).

### NFR-2: Configuration compatibility
The `PrintQueue:Mode` selector in `appsettings.*.json` continues to use the same string values (`"FileSystem"`, `"Azure"`, `"Cups"`, `"Combined"`). `PrintPickingList:PrintQueueFolder` continues to be the configured path. No migration steps for ops.

### NFR-3: Build and CI
`dotnet build` and `dotnet format` must pass after the change. The new project is included in the existing build pipeline through the solution file — no CI/workflow YAML changes expected.

### NFR-4: Test coverage
Existing `FileSystemPrintQueueSinkTests` continues to cover the sink's behavior. Coverage percentage for the moved code should be unchanged (the tests follow the type into the new project).

## Data Model
None. This change moves a single stateless service class. No entity, DTO, table, or persisted contract is touched.

## API / Interface Design

### Public surface that stays put
- `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` — unchanged.
- `Anela.Heblo.Application.Shared.Printing.PrintPickingListOptions` — unchanged.
- `appsettings.*.json` keys `PrintQueue:Mode` and `PrintPickingList:PrintQueueFolder` — unchanged.

### Public surface that moves
- `FileSystemPrintQueueSink` — moves from `Anela.Heblo.Application.Features.ExpeditionList.Services` to `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList`. Any external code (none currently) that imports the old namespace would need the using updated; within this repo only test files and the API composition root reference it.

### New public surface
- `Anela.Heblo.Adapters.FileSystem.FileSystemAdapterServiceCollectionExtensions.AddFileSystemPrintQueueSink(this IServiceCollection services)` — thin DI registration helper, mirroring the Cups and Azure equivalents.

## Dependencies
- `.NET 8 SDK` (already required).
- `Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Logging.Abstractions` NuGet packages (transitively available via the Application project reference, but referenced explicitly if needed for the registration extension).
- No external services, third-party SDKs, or feature flags.
- Existing `Anela.Heblo.Application` project (referenced from the new adapter).

## Out of Scope
- Refactoring the `IPrintQueueSink` interface or `PrintPickingListOptions`.
- Adding new sink implementations (e.g., SMB, FTP, network share).
- Changing the `PrintQueue:Mode` switch in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` beyond the single `default` branch update.
- Adding retry, transactionality, idempotency, or error-handling improvements to file copies (the sink today silently overwrites failures — that stays as-is).
- Cross-adapter consolidation (e.g., a shared `Adapters.Common` project).
- Analyzer or ArchUnit-style enforcement to prevent future regressions.
- Renaming the existing `Features/ExpeditionList/Services` folder in the Application project (other unrelated services may still live there).
- Touching the `CombinedPrintQueueSink` in `Anela.Heblo.API/Features/ExpeditionList/`.

## Open Questions
None.

## Status: COMPLETE
```