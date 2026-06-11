# Relocate FileSystemPrintQueueSink to a Dedicated Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `FileSystemPrintQueueSink` from `Anela.Heblo.Application` to a new `Anela.Heblo.Adapters.FileSystem` adapter project so all `IPrintQueueSink` implementations live in the outer adapter ring, matching the Azure and CUPS sinks. No behavior change.

**Architecture:** Create a new sibling adapter project at `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/` (mirroring the smallest existing adapter, `Anela.Heblo.Adapters.Cups`). Move the existing sink class verbatim — only the namespace changes. Expose an `AddFileSystemPrintQueueSink` DI extension that the composition root calls from the `default` branch of the `AddPrintQueueSink` switch. Update three test files and two `.csproj` test references to consume the new namespace.

**Tech Stack:** .NET 8, C#, xUnit, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`.

---

## Source-of-truth facts (verified against the worktree on 2026-06-10)

These facts override anything in the spec that contradicts them — the arch-review identified them as spec corrections.

- Solution file is at the repo root: `Anela.Heblo.sln` (NOT `backend/Anela.Heblo.sln`).
- Config keys for the sink selector and queue folder are `ExpeditionList:PrintSink` and `ExpeditionList:PrintQueueFolder` (NOT `PrintQueue:Mode` / `PrintPickingList:PrintQueueFolder`).
- Valid `PrintSink` values: `"FileSystem"`, `"AzureBlob"` (NOT `"Azure"`), `"Cups"`, `"Combined"`, plus unset (defaults to FileSystem).
- The `default` branch in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` is a single line at **line 428**.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` currently does NOT reference any sink adapter project. The using update alone will not compile — the new adapter project reference must be added.
- `ServiceCollectionExtensions.cs` line 26 has `using Anela.Heblo.Application.Features.ExpeditionList.Services;`. The only consumer of that namespace in the file is `FileSystemPrintQueueSink` at line 428 (verified by grep — `ExpeditionListService`/`IExpeditionListService` are not used in this file). Once the swap is made, this `using` directive must be removed and `using Anela.Heblo.Adapters.FileSystem;` added.
- Other files in the `Application/Features/ExpeditionList/Services/` folder (`ExpeditionListService.cs`, `IExpeditionListService.cs`) stay put — the folder must remain.
- Sibling adapter `Anela.Heblo.Adapters.Cups` is the architectural template for the new project (smallest, self-contained, registers via a `*AdapterServiceCollectionExtensions` class).

---

## File Structure

### New files (3)

```
backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/
├── Anela.Heblo.Adapters.FileSystem.csproj                    (Task 1)
├── FileSystemAdapterServiceCollectionExtensions.cs           (Task 4)
└── Features/
    └── ExpeditionList/
        └── FileSystemPrintQueueSink.cs                       (Task 3)
```

### Deleted files (1)

```
backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs   (Task 3)
```

Leave the `Services/` folder in place — `ExpeditionListService.cs` and `IExpeditionListService.cs` still live there.

### Modified files (8)

| File | Edit | Task |
|------|------|------|
| `Anela.Heblo.sln` (repo root) | Add adapter project to `Adapters` solution folder | Task 2 |
| `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` | Add `ProjectReference` to new adapter | Task 5 |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Swap line 428 + adjust usings | Task 5 |
| `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` | Add `ProjectReference` to new adapter | Task 6 |
| `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` | Replace one `using` directive | Task 6 |
| `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` | Replace one `using` directive | Task 6 |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj` | Add `ProjectReference` to new adapter | Task 7 |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` | Replace one `using` directive | Task 7 |
| `docs/architecture/filesystem.md` | Add a one-line placement rule | Task 8 |

---

## Task 1: Create the new adapter project file

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj`

This is a pure scaffolding step — no code yet, only the project skeleton. Mirrors `Anela.Heblo.Adapters.Cups.csproj` but drops the CUPS-specific package references because the FileSystem adapter only needs what flows transitively from `Anela.Heblo.Application` (`IOptions<T>`, `ILogger<T>`, `IServiceCollection`).

- [ ] **Step 1: Verify the target directory does not yet exist**

Run:
```bash
ls backend/src/Adapters/Anela.Heblo.Adapters.FileSystem 2>&1 || echo "OK: directory does not exist"
```
Expected: `OK: directory does not exist` (or `ls: ...: No such file or directory`).

- [ ] **Step 2: Create the .csproj file**

Path: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj`

Content (exact):
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

Rationale: No `PackageReference` entries — `IOptions<T>` (from `Microsoft.Extensions.Options.Abstractions`) and `ILogger<T>` (from `Microsoft.Extensions.Logging.Abstractions`) flow transitively via the Application project reference. Add explicit `PackageReference` entries only if `dotnet build` complains in a later task.

- [ ] **Step 3: Verify the project file is well-formed**

Run from repo root:
```bash
dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj
```
Expected: `Restore complete` with no errors. (The project has only one item in `ItemGroup` and no source files yet, but restore still validates the XML and resolves the project reference.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj
git commit -m "chore: scaffold Anela.Heblo.Adapters.FileSystem project"
```

---

## Task 2: Add the new project to the solution

**Files:**
- Modify: `Anela.Heblo.sln` (repo root — let `dotnet sln` edit it)

Adding to the solution must happen *before* trying to build the solution, otherwise the project would be ignored. Use `dotnet sln add` so the GUID and project type are generated correctly — do NOT hand-edit the `.sln` file.

- [ ] **Step 1: Add the project to the `Adapters` solution folder**

Run from repo root:
```bash
dotnet sln Anela.Heblo.sln add backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj --solution-folder Adapters
```
Expected output: `Project ... was added to the solution.`

- [ ] **Step 2: Confirm the project landed under the Adapters folder**

Run:
```bash
grep "Anela.Heblo.Adapters.FileSystem" Anela.Heblo.sln
```
Expected: at least two matching lines — one `Project(...)` declaration and one entry under the `NestedProjects` section that maps the new project's GUID to the Adapters folder GUID `{4B6F17C3-0A57-487A-BE8C-1808B40EC604}`.

- [ ] **Step 3: Confirm the solution still builds cleanly with the empty adapter**

Run from repo root:
```bash
dotnet build Anela.Heblo.sln --nologo
```
Expected: `Build succeeded.` with `0 Error(s)`. The adapter project has zero source files, which is a valid (empty assembly) build.

- [ ] **Step 4: Commit**

```bash
git add Anela.Heblo.sln
git commit -m "chore: add Anela.Heblo.Adapters.FileSystem to solution"
```

---

## Task 3: Move the FileSystemPrintQueueSink class into the new adapter

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`
- Test: existing `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` covers behavior parity — it will fail to compile after the move until its `using` is updated in Task 6.

This is a pure relocation. The class body — constructor, `SendAsync` implementation, `PrintQueueFolder` lookup, log messages, lifetime semantics — must remain byte-identical. Only the namespace and the location change.

- [ ] **Step 1: Create the new file**

Path: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs`

Content (exact — namespace is the only change vs. the original at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`):
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;

namespace Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;

public class FileSystemPrintQueueSink : IPrintQueueSink
{
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<FileSystemPrintQueueSink> _logger;

    public FileSystemPrintQueueSink(
        IOptions<PrintPickingListOptions> options,
        ILogger<FileSystemPrintQueueSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var folder = _options.Value.PrintQueueFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("PrintQueueFolder is not configured. Skipping printer queue copy.");
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(folder);

        foreach (var f in filePaths)
        {
            var fileName = Path.GetFileName(f);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", f);
                continue;
            }

            File.Copy(f, Path.Combine(folder, fileName));
        }

        return Task.CompletedTask;
    }
}
```

Note: The new file adds `using Anela.Heblo.Application.Features.ExpeditionList;` to qualify `PrintPickingListOptions` (which the original picked up via its own namespace). `Anela.Heblo.Application.Shared.Printing` is still needed for `IPrintQueueSink`.

- [ ] **Step 2: Delete the old file**

Run:
```bash
git rm backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs
```
Expected: `rm 'backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs'`.

- [ ] **Step 3: Verify the Services folder still contains the other two files**

Run:
```bash
ls backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/
```
Expected:
```
ExpeditionListService.cs
IExpeditionListService.cs
```
(The folder is intentionally not deleted — `ExpeditionListService` continues to live in the Application layer.)

- [ ] **Step 4: Verify the build now fails — the API and tests still reference the old namespace**

Run from repo root:
```bash
dotnet build Anela.Heblo.sln --nologo 2>&1 | head -60
```
Expected: build FAILS. Look for `CS0246`/`CS0234`-style errors mentioning `FileSystemPrintQueueSink` in:
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs`
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs`

This is intentional: tasks 5, 6, and 7 will fix each consumer in turn. Confirm those four files appear and nothing else does (which would mean an unexpected fifth consumer was missed).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs
git commit -m "refactor: relocate FileSystemPrintQueueSink to Adapters.FileSystem"
```

Note: this commit intentionally leaves the build broken — the next commits make it green. The split lets a reviewer see the relocation as a discrete event.

---

## Task 4: Add the DI extension method to the new adapter

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs`

Mirrors `CupsAdapterServiceCollectionExtensions` shape: a static `IServiceCollection` extension class whose single method registers the sink with the same lifetime it used to have (`Scoped`). No `IConfiguration` parameter — `PrintPickingListOptions` is already bound by `ExpeditionListModule` in the Application layer.

- [ ] **Step 1: Create the extension class**

Path: `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs`

Content (exact):
```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.FileSystem;

public static class FileSystemAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filesystem-based <see cref="IPrintQueueSink"/> implementation.
    /// PrintPickingListOptions is bound by ExpeditionListModule in the Application layer,
    /// so this extension takes no IConfiguration parameter.
    /// </summary>
    public static IServiceCollection AddFileSystemPrintQueueSink(this IServiceCollection services)
    {
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        return services;
    }
}
```

Rationale recap:
- `Scoped` lifetime matches the prior registration (NFR-1 mandates parity).
- No `IConfiguration` parameter: the sink consumes `IOptions<PrintPickingListOptions>`, which `ExpeditionListModule` already binds. Adding `IConfiguration` here would be dead weight (the Azure extension only takes it because it constructs a `BlobContainerClient`).
- Class name `FileSystemAdapterServiceCollectionExtensions` follows the Cups convention rather than Azure's `AzureAdapterModule` form — it reads as a stock `IServiceCollection` extension class and is the better forward-looking convention for adapters that only register services.

- [ ] **Step 2: Build the adapter project in isolation to confirm it compiles**

Run from repo root:
```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Anela.Heblo.Adapters.FileSystem.csproj --nologo
```
Expected: `Build succeeded.` with `0 Error(s)`. If `Microsoft.Extensions.DependencyInjection` is not transitively available, the error will say so explicitly — in that case, add `<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />` to the `.csproj` and re-run.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs
git commit -m "feat(adapters): add AddFileSystemPrintQueueSink DI extension"
```

---

## Task 5: Wire the new extension into the API composition root

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (add project reference)
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (swap registration + adjust usings)

The API composition root must reference the new adapter project before it can call the extension. The `default` branch at line 428 — the only consumer of the old sink namespace in this file — flips from inline `AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>()` to the new extension call.

- [ ] **Step 1: Add the project reference to Anela.Heblo.API.csproj**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, locate the existing adapter `ProjectReference` block (around line 68–69):
```xml
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Azure\Anela.Heblo.Adapters.Azure.csproj" />
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Cups\Anela.Heblo.Adapters.Cups.csproj" />
```

Insert immediately after the `Adapters.Cups` line (preserves alphabetical ordering near peers):
```xml
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />
```

Final block excerpt should read:
```xml
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Azure\Anela.Heblo.Adapters.Azure.csproj" />
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.Cups\Anela.Heblo.Adapters.Cups.csproj" />
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />
        <ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.SendGrid\Anela.Heblo.Adapters.SendGrid.csproj" />
```

- [ ] **Step 2: Replace the old `using` with the new namespace in ServiceCollectionExtensions.cs**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`:

Find line 26:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

Replace with:
```csharp
using Anela.Heblo.Adapters.FileSystem;
```

Verification: search the file for any other reference to `Application.Features.ExpeditionList.Services`. As of 2026-06-10 there are none — `FileSystemPrintQueueSink` at the old line 428 is the only consumer of the old namespace in this file (other types in the `.Services` namespace like `ExpeditionListService` and `IExpeditionListService` are not referenced here). Run to double-check:

```bash
grep -n "ExpeditionList\.Services\|ExpeditionListService\|IExpeditionListService" backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
```
Expected: no matches after the using swap.

- [ ] **Step 3: Swap the registration line in the `default` branch**

Find line 428 in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`:
```csharp
            default: // "FileSystem" or unset
                services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
                break;
```

Replace the body of the `default` branch so the file reads:
```csharp
            default: // "FileSystem" or unset
                services.AddFileSystemPrintQueueSink();
                break;
```

(`AddFileSystemPrintQueueSink` is now resolvable via the `using Anela.Heblo.Adapters.FileSystem;` added in Step 2.)

- [ ] **Step 4: Confirm the API project compiles**

Run from repo root:
```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --nologo
```
Expected: `Build succeeded.` with `0 Error(s)`.

If the compiler emits CS8019 ("Unnecessary using directive") or analyzer warnings about the new `using Anela.Heblo.Adapters.FileSystem;`, ignore — the project compiles without `WarningsAsErrors` and the extension is used on the new line.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(api): register FileSystem sink via Adapters.FileSystem extension"
```

---

## Task 6: Update Anela.Heblo.Tests references and usings

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`

The unit-test project instantiates `FileSystemPrintQueueSink` directly and asserts `Assert.IsType<FileSystemPrintQueueSink>(sink)`. Once the type moves, the test project needs to reference the new adapter and switch its `using` directives.

- [ ] **Step 1: Add the project reference to Anela.Heblo.Tests.csproj**

In `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, locate the existing adapter `ProjectReference` block (around lines 49–50):
```xml
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Azure\Anela.Heblo.Adapters.Azure.csproj" />
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Cups\Anela.Heblo.Adapters.Cups.csproj" />
```

Insert immediately after the `Adapters.Cups` line:
```xml
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />
```

- [ ] **Step 2: Update the using in FileSystemPrintQueueSinkTests.cs**

In `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs`:

Find line 2:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

Replace with:
```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
```

Leave `using Anela.Heblo.Application.Features.ExpeditionList;` on line 1 alone — `PrintPickingListOptions` still lives in that namespace.

- [ ] **Step 3: Update the using in CombinedPrintQueueSinkRegistrationTests.cs**

In `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`:

Find line 6:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

Replace with:
```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
```

Leave line 5 (`using Anela.Heblo.Application.Features.ExpeditionList;`) alone — `PrintPickingListOptions` reference at line 39 still needs it.

- [ ] **Step 4: Run the touched test classes — they should pass**

Run from repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileSystemPrintQueueSinkTests|FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests" --nologo
```
Expected: all tests pass. Specifically:
- `FileSystemPrintQueueSinkTests.SendAsync_ValidFiles_CopiesFilesToOutputFolder` — PASS
- `FileSystemPrintQueueSinkTests.SendAsync_OutputFolderDoesNotExist_CreatesItAndCopiesFiles` — PASS
- `FileSystemPrintQueueSinkTests.SendAsync_PrintQueueFolderNotConfigured_DoesNotThrow` — PASS
- `CombinedPrintQueueSinkRegistrationTests.FileSystem_ResolvesFileSystemPrintQueueSink` — PASS (the `Assert.IsType<FileSystemPrintQueueSink>(sink)` resolves to the relocated type because the `using` directive now points to the adapter namespace)
- `CombinedPrintQueueSinkRegistrationTests.Combined_*` tests — PASS (Combined branch is untouched)

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs
git commit -m "test: point Anela.Heblo.Tests at Adapters.FileSystem namespace"
```

---

## Task 7: Update Anela.Heblo.Adapters.Shoptet.Tests references and using

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs`

The Shoptet integration fixture wires up its own DI container with `FileSystemPrintQueueSink` as the print sink. The Shoptet test project today does NOT reference any sink adapter — the using update on its own will not compile. Add the project reference here.

- [ ] **Step 1: Add the project reference to Anela.Heblo.Adapters.Shoptet.Tests.csproj**

In `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`, locate the existing `ProjectReference` block (lines 34–39):
```xml
    <ItemGroup>
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Shoptet\Anela.Heblo.Adapters.Shoptet.csproj" />
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
        <ProjectReference Include="..\..\src\Anela.Heblo.API\Anela.Heblo.API.csproj" />
        <ProjectReference Include="..\..\src\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
    </ItemGroup>
```

Insert after the existing adapter lines so adapter references stay grouped:
```xml
    <ItemGroup>
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.FileSystem\Anela.Heblo.Adapters.FileSystem.csproj" />
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.Shoptet\Anela.Heblo.Adapters.Shoptet.csproj" />
        <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.ShoptetApi\Anela.Heblo.Adapters.ShoptetApi.csproj" />
        <ProjectReference Include="..\..\src\Anela.Heblo.API\Anela.Heblo.API.csproj" />
        <ProjectReference Include="..\..\src\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
    </ItemGroup>
```

- [ ] **Step 2: Update the using in ShoptetIntegrationTestFixture.cs**

In `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs`:

Find line 6:
```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

Replace with:
```csharp
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
```

Leave line 5 (`using Anela.Heblo.Application.Features.ExpeditionList;`) alone — `PrintPickingListOptions` reference at line 41 still needs it.

- [ ] **Step 3: Confirm the Shoptet test project compiles**

Run from repo root:
```bash
dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --nologo
```
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs
git commit -m "test(shoptet): reference Adapters.FileSystem for sink fixture"
```

---

## Task 8: Document the placement rule

**Files:**
- Modify: `docs/architecture/filesystem.md`

Add a single line under the "Application Layer (`Anela.Heblo.Application`)" section of the "Component Placement Rules" so the next maintainer who scans this doc sees the rule that was just violated. This is documentation only — no analyzer or ArchUnit enforcement is in scope.

- [ ] **Step 1: Add the bullet to the Application Layer section**

In `docs/architecture/filesystem.md`, find the existing bullet list under `### Application Layer (`Anela.Heblo.Application`):` (around lines 148–158). The list ends with:
```markdown
- **Shared/Rag/**: Cross-module RAG **application/infrastructure** types — options base classes, helpers, shared services (`RagFeatureOptions`, `OneDriveFolderMapping`, `IRagQueryExpander`). Distinct from `Domain/Shared/Rag/`, which holds Domain-layer RAG types
```

Append immediately after that bullet (preserving the existing line above and the blank line + horizontal rule below):
```markdown
- **I/O placement rule**: Concrete `IPrintQueueSink` implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.
```

- [ ] **Step 2: Sanity-check the doc still renders**

Run:
```bash
head -170 docs/architecture/filesystem.md | tail -25
```
Expected: the new bullet appears at the bottom of the Application Layer list, immediately above the `---` horizontal rule that begins the Infrastructure Layer section.

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/filesystem.md
git commit -m "docs(arch): note I/O placement rule for IPrintQueueSink implementations"
```

---

## Task 9: Final validation gates

**Files:** none modified — verification only.

Run the project's standard validation gates from `CLAUDE.md`: `dotnet build`, `dotnet format`, and `dotnet test`. If any gate fails, fix and re-run before declaring the change complete.

- [ ] **Step 1: Full solution build**

Run from repo root:
```bash
dotnet build Anela.Heblo.sln --nologo
```
Expected: `Build succeeded.` with `0 Error(s)` and `0 Warning(s)` (or, at most, pre-existing warnings unrelated to this change).

- [ ] **Step 2: dotnet format check**

Run from the `backend/` directory:
```bash
cd backend && dotnet format --verify-no-changes --no-restore && cd ..
```
Expected: exit code 0 with no diff. If format flags an issue in any of the new or modified files, run `dotnet format` (without `--verify-no-changes`) in `backend/` to apply the fix, then re-run the verify command. Stage the formatting changes into the existing relevant commit only if they are trivial whitespace; otherwise create a small `style: dotnet format` follow-up commit.

- [ ] **Step 3: Run the full test suite for the touched projects**

Run from repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --nologo
```
Expected: both runs report `Passed!` with no failures. Pre-existing skipped/integration tests stay as-is — this change does not enable or disable any test.

- [ ] **Step 4: Confirm no stray references to the old type location remain**

Run:
```bash
grep -rn "Application\.Features\.ExpeditionList\.Services\.FileSystemPrintQueueSink\|Application\.Features\.ExpeditionList\.Services;" backend/ docs/
```
Expected: zero matches. (The `Services` folder still contains `ExpeditionListService` and `IExpeditionListService`, but no code or doc should still import `FileSystemPrintQueueSink` from the old namespace.)

Also run:
```bash
grep -rn "FileSystemPrintQueueSink" backend/
```
Expected matches (4 sites — the only sites that should exist after this change):
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/Features/ExpeditionList/FileSystemPrintQueueSink.cs` (the relocated type)
- `backend/src/Adapters/Anela.Heblo.Adapters.FileSystem/FileSystemAdapterServiceCollectionExtensions.cs` (DI extension)
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/FileSystemPrintQueueSinkTests.cs` (unit tests)
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` (DI regression test)
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` (Shoptet integration fixture)

(That is 5 matches across 5 files. The composition root `ServiceCollectionExtensions.cs` is intentionally *not* in the list — after Task 5, the API references the type only through the extension method, not by name.)

- [ ] **Step 5: No further commit — validation only**

If everything is green, the relocation is complete. The earlier per-task commits already cover all changes. If any validation surfaced a fix, commit it as a small `fix:` follow-up.

---

## Spec Coverage Check (self-review)

| Spec section | Plan task(s) |
|---|---|
| FR-1: New `Anela.Heblo.Adapters.FileSystem` adapter project | Tasks 1, 2 |
| FR-2: Move `FileSystemPrintQueueSink` to the new adapter | Task 3 |
| FR-3: `AddFileSystemPrintQueueSink` DI extension + composition root swap | Tasks 4, 5 |
| FR-4: Update test references | Tasks 6, 7 |
| FR-5: Optional location-guard documentation note | Task 8 |
| NFR-1: Behavior parity (Scoped lifetime, identical SendAsync body, identical logs) | Task 3 (body verbatim), Task 4 (`Scoped`), Task 6 (existing tests re-run unchanged) |
| NFR-2: Configuration compatibility (same `ExpeditionList:PrintSink` / `ExpeditionList:PrintQueueFolder`) | Task 5 (default branch swap touches no other configuration) |
| NFR-3: Build + CI (`dotnet build`, `dotnet format`) | Task 9 |
| NFR-4: Test coverage parity | Tasks 6, 7 (existing tests follow the type into the new project) |

Spec amendments from arch-review applied: corrected config keys (Task 5 swap), corrected sln location (Task 2 runs from repo root), corrected line number (line 428 in Task 5), added missing Shoptet test csproj reference (Task 7), dropped over-specified `PackageReference` (Task 1 csproj has none), preferred docs wording (Task 8).

## Status: COMPLETE
