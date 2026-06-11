# Architecture Review: Move FileSystemPrintQueueSink to a Dedicated Adapter Project

## Skip Design: true

Backend-only structural refactor. No UI components, screens, or visual decisions.

## Architectural Fit Assessment

The proposal is correct and minimal. The current placement of `FileSystemPrintQueueSink` in the Application layer is a one-off Clean Architecture violation: the two sibling sinks already live in dedicated adapter projects (`Anela.Heblo.Adapters.Azure`, `Anela.Heblo.Adapters.Cups`), and the project's own `docs/architecture/filesystem.md` explicitly assigns I/O concerns to outer rings. The new `Anela.Heblo.Adapters.FileSystem` project slots cleanly into the existing `backend/src/Adapters/` directory and the solution's `Adapters` solution folder.

Integration points:
- `Anela.Heblo.API` composition root (`Extensions/ServiceCollectionExtensions.cs` — `AddPrintQueueSink` switch on `ExpeditionList:PrintSink`).
- Three test sites: `Anela.Heblo.Tests` (unit + DI regression), `Anela.Heblo.Adapters.Shoptet.Tests` (integration fixture).
- Solution file `Anela.Heblo.sln` and project file references in `Anela.Heblo.API.csproj`, `Anela.Heblo.Tests.csproj`, and `Anela.Heblo.Adapters.Shoptet.Tests.csproj`.

No interface changes. No configuration changes. Behavior parity is achievable as a pure relocation + namespace rename.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application (no I/O — pure orchestration)
  Shared/Printing/
    IPrintQueueSink.cs                  ◄── interface stays here
  Features/ExpeditionList/
    PrintPickingListOptions.cs           ◄── options stay here (key = "ExpeditionList")
    ExpeditionListModule.cs              ◄── binds options
    (Services/FileSystemPrintQueueSink.cs is REMOVED)

Anela.Heblo.Adapters.FileSystem  ◄── NEW project, sibling of Adapters.Cups / Adapters.Azure
  Anela.Heblo.Adapters.FileSystem.csproj
  FileSystemAdapterServiceCollectionExtensions.cs   ◄── AddFileSystemPrintQueueSink(this IServiceCollection)
  Features/ExpeditionList/
    FileSystemPrintQueueSink.cs                     ◄── implementation, byte-identical body

Anela.Heblo.API
  Extensions/ServiceCollectionExtensions.cs
    case default: services.AddFileSystemPrintQueueSink();   ◄── single-line swap at line 428

Backend tests
  Anela.Heblo.Tests       ── using updates + add Adapters.FileSystem project reference
  Adapters.Shoptet.Tests  ── using update + add Adapters.FileSystem project reference
```

### Key Design Decisions

#### Decision 1: New dedicated `Adapters.FileSystem` project (not a fold-in)

**Options considered:**
- A. New `Anela.Heblo.Adapters.FileSystem` project (mirror Cups shape).
- B. Drop the sink into an existing self-contained adapter (e.g., `Adapters.Cups`).
- C. Move it into `Anela.Heblo.Persistence`.

**Chosen approach:** A.

**Rationale:** Cups and Azure are dedicated per-technology adapters; folding filesystem I/O into either creates a misleading dependency (Cups depends on `SharpIppNext`, Azure on `Azure.Storage.Blobs`) and surprises future maintainers. `Persistence` is reserved for EF Core / DbContext concerns per the architecture doc. A dedicated adapter is the lightest option that preserves the pattern.

#### Decision 2: Extension shape — `AddFileSystemPrintQueueSink(this IServiceCollection)`, no `IConfiguration` parameter

**Options considered:**
- A. `AddFileSystemPrintQueueSink(this IServiceCollection services)` — pure registration.
- B. `AddFileSystemPrintQueueSink(this IServiceCollection services, IConfiguration configuration)` — mirrors `AddAzurePrintQueueSink` / `AddCupsPrinting`.

**Chosen approach:** A.

**Rationale:** `PrintPickingListOptions` is already bound to the `ExpeditionList` section by `ExpeditionListModule` in the Application layer (`PrintPickingListOptions.ConfigurationKey = "ExpeditionList"`). The sink consumes `IOptions<PrintPickingListOptions>` and needs no further configuration binding of its own. Taking `IConfiguration` would be dead weight and would obscure where the option lives. Azure's extension takes `IConfiguration` only because it constructs a `BlobContainerClient` — the filesystem adapter has no analogous concern.

#### Decision 3: Lifetime stays `Scoped`

**Options considered:** Scoped (current) vs Singleton (Azure's choice).

**Chosen approach:** `Scoped`.

**Rationale:** NFR-1 mandates behavior parity. The sink is stateless and could be a Singleton, but changing the lifetime is out of scope and would diverge from the prior registration; keep it exactly as it was.

#### Decision 4: Extension class naming follows Cups (`FileSystemAdapterServiceCollectionExtensions`)

The Azure adapter uses the slightly different name `AzureAdapterModule`. Naming is split across the two existing adapters, so either form is defensible. The Cups form (`*AdapterServiceCollectionExtensions`) reads as a stock `IServiceCollection` extension class, matches the spec, and is the better forward-looking convention for adapters that only register services.

## Implementation Guidance

### Directory / Module Structure

New files (create):
```
backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/
├── Anela.Heblo.Adapters.FileSystem.csproj
├── FileSystemAdapterServiceCollectionExtensions.cs
└── Features/
    └── ExpeditionList/
        └── FileSystemPrintQueueSink.cs
```

Deleted files:
```
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs
```

(Leave the `Services/` folder in place — other files may live there.)

### Interfaces and Contracts

- **Unchanged**: `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink`, `Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions`, configuration key `ExpeditionList:PrintSink`, configuration key `ExpeditionList:PrintQueueFolder`.
- **Moved**: `FileSystemPrintQueueSink` from `Anela.Heblo.Application.Features.ExpeditionList.Services` to `Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList`. Same constructor signature, same `SendAsync` body.
- **New**: `Anela.Heblo.Adapters.FileSystem.FileSystemAdapterServiceCollectionExtensions.AddFileSystemPrintQueueSink(this IServiceCollection services)` returning `IServiceCollection`. Single statement: `services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>(); return services;`.

### .csproj shape (target — mirror Cups, minus its dependencies)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.FileSystem</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

No `PackageReference` entries needed: `Microsoft.Extensions.Options.Abstractions` (for `IOptions<T>`) and `Microsoft.Extensions.Logging.Abstractions` (for `ILogger<T>`) flow transitively through the Application project reference. Only add explicit packages if `dotnet build` actually complains.

### Project reference updates

| Project | Change |
|---------|--------|
| `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` | Add `<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />` |
| `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` | Add `<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />` |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` | Add `<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />` (this project does not yet reference any sink adapter — the spec missed this) |

### Composition root edit (`Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`)

Exact change at line 428 (inside the `default` branch of `AddPrintQueueSink`'s switch on `ExpeditionList:PrintSink`):
```diff
-                services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
+                services.AddFileSystemPrintQueueSink();
```
Also drop the now-unused `using Anela.Heblo.Application.Features.ExpeditionList.Services;` if no other type from that namespace is referenced in the file (grep before deleting — `ExpeditionListModule` lives directly under `Features/ExpeditionList`, not `.Services`). Add `using Anela.Heblo.Adapters.FileSystem;`.

### Solution file edit (`Anela.Heblo.sln` at repo root — not under `backend/`)

Use `dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj --solution-folder Adapters` from the repo root. This generates a fresh GUID and adds the entry to the existing `Adapters` solution folder (GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`).

### Test file updates

| File | Edit |
|------|------|
| `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;`. No other change. |
| `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;`. The `FileSystem_ResolvesFileSystemPrintQueueSink` test asserts `Assert.IsType<FileSystemPrintQueueSink>(sink)` — must still pass after the using swap. |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;`. Project ref update (above) is what makes this compile. |

### Data Flow

Unchanged from current. At request time:
1. Use case in `Anela.Heblo.Application.Features.ExpeditionList` produces PDF file paths.
2. It resolves `IPrintQueueSink` from DI.
3. When `ExpeditionList:PrintSink` is `"FileSystem"` or unset, DI returns the `FileSystemPrintQueueSink` instance (now from the FileSystem adapter project).
4. The sink reads `IOptions<PrintPickingListOptions>.Value.PrintQueueFolder`, ensures the directory exists, copies each input file by base name.

No layering boundary is crossed by data; only the assembly that holds the implementation moves outward.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec mis-names the config keys (`PrintQueue:Mode` → actual `ExpeditionList:PrintSink`; `PrintPickingList:PrintQueueFolder` → actual `ExpeditionList:PrintQueueFolder`; `"Azure"` → actual `"AzureBlob"`). Implementer following the spec literally could touch the wrong keys. | High | See Specification Amendments below. |
| Spec mis-names the solution file path (`backend/Anela.Heblo.sln` → actual `Anela.Heblo.sln` at repo root). | Medium | Use `dotnet sln Anela.Heblo.sln add … --solution-folder Adapters` from repo root. |
| `ShoptetIntegrationTestFixture` references `FileSystemPrintQueueSink` directly but `Anela.Heblo.Adapters.Shoptet.Tests.csproj` has no project reference to `Adapters.FileSystem` — spec calls for "only using update" and would leave a broken build. | Medium | Add the project reference to `Anela.Heblo.Adapters.Shoptet.Tests.csproj` as listed above. |
| `using Anela.Heblo.Application.Features.ExpeditionList.Services;` left dangling in `ServiceCollectionExtensions.cs` after the swap — analyzers may warn. | Low | Grep the file for other `.Services` consumers; remove if none. |
| Solution GUID collision if hand-editing the `.sln`. | Low | Always use `dotnet sln add`; don't hand-paste a GUID. |
| Hidden third reference site missed by spec. | Low | `grep -rn FileSystemPrintQueueSink backend/` after the move — should match only the new adapter file, the test files, the `CombinedPrintQueueSinkRegistrationTests` assertion, and the API switch case. (Confirmed today: those are the only sites.) |

## Specification Amendments

1. **FR-1 / NFR-2 — configuration key names are wrong.** Replace every reference to `PrintQueue:Mode` with `ExpeditionList:PrintSink`. Replace every reference to `PrintPickingList:PrintQueueFolder` with `ExpeditionList:PrintQueueFolder`. The valid `PrintSink` values are `"FileSystem"`, `"AzureBlob"` (not `"Azure"`), `"Cups"`, `"Combined"`, plus unset (defaults to `FileSystem`). `PrintPickingListOptions.ConfigurationKey = "ExpeditionList"`.

2. **FR-1 — solution file location.** Solution is `Anela.Heblo.sln` at repo root, not `backend/Anela.Heblo.sln`.

3. **FR-3 — exact line.** The `default` branch is a single line at `ServiceCollectionExtensions.cs:428`, not "`427-429`".

4. **FR-4 — additional project reference required.** `Anela.Heblo.Adapters.Shoptet.Tests.csproj` currently does not reference any sink adapter; the using update alone will not compile. Add `<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />` to that csproj as part of FR-4.

5. **FR-1 — `PackageReference` set is over-specified.** The new adapter needs no explicit `PackageReference` entries; `IOptions<T>` and `ILogger<T>` flow transitively from `Anela.Heblo.Application`. Add packages only if `dotnet build` actually complains. Drop the explicit `Microsoft.Extensions.Options.ConfigurationExtensions` requirement — the adapter does not bind configuration sections.

6. **FR-5 — preferred wording for the doc note.** Add one bullet under "Component Placement Rules → Application Layer" in `docs/architecture/filesystem.md` along the lines of: "Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`."

## Prerequisites

None. No migrations, no infrastructure, no config changes, no feature flag. Implementation can start immediately.

Validation gates per `CLAUDE.md`: `dotnet build` and `dotnet format` from `backend/`, plus `dotnet test` (the three touched test classes — `FileSystemPrintQueueSinkTests`, `CombinedPrintQueueSinkRegistrationTests`, `ShoptetIntegrationTestFixture` consumers — must remain green).