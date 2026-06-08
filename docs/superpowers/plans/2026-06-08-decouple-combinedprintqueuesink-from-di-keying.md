# Decouple CombinedPrintQueueSink from DI Keying Conventions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `CombinedPrintQueueSink` out of `Anela.Heblo.Application` into `Anela.Heblo.API`, strip its `[FromKeyedServices]` attributes, and replace the `"Combined"` switch arm's concrete-type registration with an explicit factory delegate that resolves the two keyed sinks itself.

**Architecture:** Pure layering correction — the composition root (API) owns DI keying knowledge; the Application layer drops all `Microsoft.Extensions.DependencyInjection` coupling. The composite class becomes `internal sealed` in `Anela.Heblo.API.Features.ExpeditionList`, mirroring the existing `Anela.Heblo.API.Features.Users` vertical-slice precedent. Runtime behavior is identical (sequential Azure→CUPS dispatch, fail-fast, `.ToList()` materialization preserved).

**Tech Stack:** .NET 8, C# 12, `Microsoft.Extensions.DependencyInjection` (`AddScoped` factory + `GetRequiredKeyedService`), xUnit + Moq for tests. `InternalsVisibleTo("Anela.Heblo.Tests")` already declared in `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj:13`.

---

## File Plan

### Files created
- `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` — new home for the class. Namespace: `Anela.Heblo.API.Features.ExpeditionList`. `internal sealed`. Constructor takes two plain `IPrintQueueSink` params (no attributes). Behavior verbatim from the deleted Application-layer copy.
- `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs` — new DI-resolution regression test (FR-5). Asserts that `ExpeditionList:PrintSink=Combined` resolves a `CombinedPrintQueueSink` and that the keyed `"azure"`/`"cups"` slots resolve to `AzureBlobPrintQueueSink` / `CupsPrintQueueSink`. Also covers `FileSystem` as a sanity guard.

### Files modified
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — add `using Anela.Heblo.API.Features.ExpeditionList;`; replace the `"Combined"` switch arm body to use an inline factory lambda over `GetRequiredKeyedService<IPrintQueueSink>("azure"|"cups")`.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` — swap `using Anela.Heblo.Application.Features.ExpeditionList.Services;` → `using Anela.Heblo.API.Features.ExpeditionList;`. The constructor call `new CombinedPrintQueueSink(_azureSink.Object, _cupsSink.Object)` already uses positional params and needs no change.

### Files deleted
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs` — deleted after the new file compiles and the test `using` is updated. The peer file `FileSystemPrintQueueSink.cs` keeps the folder populated.

### Files untouched (verification only — must keep passing)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`

---

## Task 1: Create the relocated `CombinedPrintQueueSink` in `Anela.Heblo.API`

**Files:**
- Create: `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`

The folder `backend/src/Anela.Heblo.API/Features/ExpeditionList/` does not yet exist; the file write creates it. The namespace `Anela.Heblo.API.Features.ExpeditionList` matches the only existing API-project feature-folder precedent (`Anela.Heblo.API.Features.Users`). The class is `internal sealed`; the test project can see it via the existing `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` declaration at `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj:13`. No DI-attribute imports — the `using Microsoft.Extensions.DependencyInjection;` directive from the old file is intentionally absent.

- [ ] **Step 1.1: Write the new class file**

Write the file exactly:

```csharp
using Anela.Heblo.Application.Shared.Printing;

namespace Anela.Heblo.API.Features.ExpeditionList;

internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    private readonly IPrintQueueSink _azureSink;
    private readonly IPrintQueueSink _cupsSink;

    public CombinedPrintQueueSink(IPrintQueueSink azureSink, IPrintQueueSink cupsSink)
    {
        _azureSink = azureSink;
        _cupsSink = cupsSink;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.ToList();
        await _azureSink.SendAsync(paths, cancellationToken);
        await _cupsSink.SendAsync(paths, cancellationToken);
    }
}
```

- [ ] **Step 1.2: Verify the file compiles in isolation (build still succeeds — the old copy is also present, but they live in different namespaces so there is no clash)**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds with zero errors and zero new warnings. Both `Anela.Heblo.Application.Features.ExpeditionList.Services.CombinedPrintQueueSink` and `Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink` now exist — no compile conflict because they are fully qualified by namespace, and the DI registration at `ServiceCollectionExtensions.cs:419` still resolves the Application-layer one for now.

- [ ] **Step 1.3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs
git commit -m "refactor: add CombinedPrintQueueSink in API layer without DI attributes"
```

---

## Task 2: Rewrite the `"Combined"` DI registration to use a factory delegate

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (lines 1–30 imports area; the `"Combined"` arm at lines 413–420)

Two edits: (a) add the `using` for the new namespace; (b) replace the `"Combined"` arm body so the factory directly resolves the two keyed sinks and `new`s up the API-layer `CombinedPrintQueueSink`. Keep the existing inline comment about the `AddAzurePrintQueueSink` side effect — it explains why the keyed `"azure"` registration is *also* needed (the side effect registers a non-keyed `IPrintQueueSink` that we are overriding via the last-wins factory below). The pre-existing `using Anela.Heblo.Application.Features.ExpeditionList.Services;` stays — it still resolves `FileSystemPrintQueueSink` in the `default` arm.

- [ ] **Step 2.1: Add the using directive for the new namespace**

Edit the imports block near the top of `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`. After the existing line `using Anela.Heblo.Application.Features.ExpeditionList.Services;` (currently line 26), add:

```csharp
using Anela.Heblo.API.Features.ExpeditionList;
```

Use the `Edit` tool:

```
old_string: using Anela.Heblo.Application.Features.ExpeditionList.Services;
new_string: using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.API.Features.ExpeditionList;
```

- [ ] **Step 2.2: Rewrite the `"Combined"` switch arm to use a factory delegate**

Replace the existing arm. Use the `Edit` tool with this exact mapping:

`old_string`:

```csharp
            case "Combined":
                // AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect;
                // it is unused here — the last non-keyed registration (CombinedPrintQueueSink) wins.
                services.AddAzurePrintQueueSink(configuration);
                services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
                services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                services.AddScoped<IPrintQueueSink, CombinedPrintQueueSink>();
                break;
```

`new_string`:

```csharp
            case "Combined":
                // AddAzurePrintQueueSink registers a non-keyed IPrintQueueSink as a side effect;
                // it is unused here — the last non-keyed registration (the factory below) wins.
                services.AddAzurePrintQueueSink(configuration);
                services.AddKeyedScoped<IPrintQueueSink, AzureBlobPrintQueueSink>("azure");
                services.AddKeyedScoped<IPrintQueueSink, CupsPrintQueueSink>("cups");
                services.AddScoped<IPrintQueueSink>(provider =>
                {
                    var azure = provider.GetRequiredKeyedService<IPrintQueueSink>("azure");
                    var cups = provider.GetRequiredKeyedService<IPrintQueueSink>("cups");
                    return new CombinedPrintQueueSink(azure, cups);
                });
                break;
```

(Comment updated to "the factory below" because after this edit the line registering `CombinedPrintQueueSink` is the factory, not a concrete-type registration.)

- [ ] **Step 2.3: Verify the build still succeeds and the registration now points at the API-layer class**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds, zero new warnings. At this point the factory inside `ServiceCollectionExtensions.cs` constructs `Anela.Heblo.API.Features.ExpeditionList.CombinedPrintQueueSink` (the new file from Task 1). The old Application-layer copy is still on disk but is no longer referenced anywhere — Task 3 deletes it.

- [ ] **Step 2.4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: wire CombinedPrintQueueSink via factory delegate at composition root"
```

---

## Task 3: Delete the old `CombinedPrintQueueSink` in the Application layer and update its test's `using`

**Files:**
- Delete: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` (line 1 only)

The test file's constructor call `new CombinedPrintQueueSink(_azureSink.Object, _cupsSink.Object)` already uses positional parameters — only the `using` directive changes. After this task, a grep for `FromKeyedServices` under `backend/src/Anela.Heblo.Application/` must return zero matches (FR-1 acceptance criterion).

- [ ] **Step 3.1: Update the test's `using` to point at the new namespace**

Use the `Edit` tool:

```
old_string: using Anela.Heblo.Application.Features.ExpeditionList.Services;
new_string: using Anela.Heblo.API.Features.ExpeditionList;
```

(File: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs`, line 1.)

- [ ] **Step 3.2: Delete the old Application-layer file**

Run: `rm backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs`

- [ ] **Step 3.3: Verify both invariants — old file gone, no `FromKeyedServices` left in Application**

Run: `ls backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs`
Expected: `ls: ...: No such file or directory` (exit code 2).

Run via the `Grep` tool: pattern `FromKeyedServices`, path `backend/src/Anela.Heblo.Application/`
Expected: zero matches. (This is the FR-1 acceptance check.)

- [ ] **Step 3.4: Verify the existing four `CombinedPrintQueueSinkTests` still pass against the relocated class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSinkTests"`
Expected: 4 tests pass — `SendAsync_BothSucceed_CallsBothSinksWithSamePaths`, `SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates`, `SendAsync_AzureSucceedsCupsThrows_ExceptionPropagates`, `SendAsync_SinglePassEnumerable_BothSinksReceiveAllPaths`.

- [ ] **Step 3.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/CombinedPrintQueueSink.cs backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs
git commit -m "refactor: remove CombinedPrintQueueSink from Application layer"
```

---

## Task 4: Write the failing DI-resolution test (FR-5)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs`

The folder `backend/test/Anela.Heblo.Tests/API/` already exists (it currently contains a `HealthChecks` subfolder). Place the new test file directly under `API/`. The test asserts the FR-5 contract: `ExpeditionList:PrintSink=Combined` resolves a `CombinedPrintQueueSink`, the keyed `"azure"` slot resolves an `AzureBlobPrintQueueSink`, the keyed `"cups"` slot resolves a `CupsPrintQueueSink`, and `ExpeditionList:PrintSink=FileSystem` still resolves a `FileSystemPrintQueueSink` (regression guard).

We deliberately do **not** call `services.AddCupsPrinting(configuration)` from the test — `AddPrintQueueSink` already calls it internally. Both `AddCupsPrinting` and `AddAzurePrintQueueSink` are lazy: they register `IOptions<...>` and resolve config values only when the sink instance is constructed (`AzureBlobPrintQueueSink` reads `BlobConnectionString` on first use; `CupsPrintingService` reads `ServerUrl` on first use). The test constructs the sinks via `GetRequiredService` / `GetRequiredKeyedService` — that *will* materialize them, so we must supply minimum non-empty config values that satisfy any constructor-time validation.

Inspect both adapters before writing the test:

- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` — `AzureBlobPrintQueueSink` requires `BlobContainerClient`, which is built from `PrintPickingListOptions.BlobConnectionString` + `BlobContainerName`. A real Azure connection string is required for `BlobContainerClient` to parse. Use the documented Azurite/devstorage development connection string (offline-safe, never connects).
- `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs` — `CupsPrintQueueSink` (via `ICupsPrintingService`) reads `CupsOptions.ServerUrl` only at print time, not at construction. Empty defaults are fine for resolution.

- [ ] **Step 4.1: Write the test file**

Write the file exactly:

```csharp
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.API;

public class CombinedPrintQueueSinkRegistrationTests
{
    // Azurite development storage connection string — never actually connects, just parses.
    private const string DevelopmentBlobConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private static ServiceProvider BuildProvider(string printSink)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExpeditionList:PrintSink"] = printSink,
                ["ExpeditionList:BlobConnectionString"] = DevelopmentBlobConnectionString,
                ["ExpeditionList:BlobContainerName"] = "expedition-lists",
                ["ExpeditionList:PrintQueueFolder"] = "/tmp",
                ["Cups:ServerUrl"] = "http://localhost:631",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Configure<PrintPickingListOptions>(
            configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddPrintQueueSink(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Combined_ResolvesCombinedPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var sink = scope.ServiceProvider.GetRequiredService<IPrintQueueSink>();

        // Assert
        Assert.IsType<CombinedPrintQueueSink>(sink);
    }

    [Fact]
    public void Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var azure = scope.ServiceProvider.GetRequiredKeyedService<IPrintQueueSink>("azure");

        // Assert
        Assert.IsType<AzureBlobPrintQueueSink>(azure);
    }

    [Fact]
    public void Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var cups = scope.ServiceProvider.GetRequiredKeyedService<IPrintQueueSink>("cups");

        // Assert
        Assert.IsType<CupsPrintQueueSink>(cups);
    }

    [Fact]
    public void FileSystem_ResolvesFileSystemPrintQueueSink()
    {
        // Arrange — regression guard: relocating CombinedPrintQueueSink must not touch the FileSystem arm.
        using var provider = BuildProvider("FileSystem");
        using var scope = provider.CreateScope();

        // Act
        var sink = scope.ServiceProvider.GetRequiredService<IPrintQueueSink>();

        // Assert
        Assert.IsType<FileSystemPrintQueueSink>(sink);
    }
}
```

- [ ] **Step 4.2: Run the four new tests and confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests"`
Expected: 4 tests pass — `Combined_ResolvesCombinedPrintQueueSink`, `Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink`, `Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink`, `FileSystem_ResolvesFileSystemPrintQueueSink`.

If any test fails to resolve a sink because of missing config (most likely: `BlobContainerClient` parse error), inspect the exact message and add the missing key to the in-memory dictionary inside `BuildProvider`. Do **not** comment out the assertion — failing to resolve in DI is the bug class FR-5 is guarding against, so a resolution failure here means either (a) the factory in Task 2 is wrong, or (b) the test's config is too thin.

- [ ] **Step 4.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/API/CombinedPrintQueueSinkRegistrationTests.cs
git commit -m "test: add DI-resolution regression test for Combined print sink wiring"
```

---

## Task 5: Final invariant checks and full validation

**Files:** (verification only — no edits)

This is the final gate. The four checks below correspond directly to the spec's acceptance criteria and the arch-review's "final grep gate" (Prerequisites #5). Run them sequentially.

- [ ] **Step 5.1: `dotnet format` produces zero diff**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0, zero diff. If it suggests changes, run without `--verify-no-changes` and commit the formatting fixes as a separate commit.

- [ ] **Step 5.2: Full solution build succeeds**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: build succeeds, zero new warnings.

- [ ] **Step 5.3: Full backend test suite passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: all tests pass. At minimum the following four test classes must be green: `CombinedPrintQueueSinkTests` (4 tests, unchanged behavior — they verify the relocated class), `CombinedPrintQueueSinkRegistrationTests` (4 new tests from Task 4), `ExpeditionListServicePrintSinkTests` (no expected change; uses `IPrintQueueSink` mocks).

- [ ] **Step 5.4: Architectural invariants — `FromKeyedServices` purged from Application, keys live in one file**

Via the `Grep` tool: pattern `FromKeyedServices`, path `backend/src/Anela.Heblo.Application/`
Expected: zero matches.

Via the `Grep` tool: pattern `"azure"|"cups"` (use `-o`), path `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
Expected: matches only inside the `AddPrintQueueSink` method body (the existing `case "Cups":` arm at line ~409 and the new `"Combined"` arm). No occurrences should appear elsewhere in the project.

- [ ] **Step 5.5: No commit needed — verification only**

If all four checks above pass, the refactor is complete. The work history is three behavior-preserving commits (Task 1 add, Task 2 wire, Task 3 delete) plus one test-adding commit (Task 4). Push the branch and open a PR.

---

## Self-Review Notes

- **Spec coverage:** FR-1 covered by Task 1 (no attributes) + Task 3 (delete old file) + Step 5.4 (grep gate). FR-2 covered by Task 1 + Task 3. FR-3 covered by Task 2. FR-4 covered by the unchanged behavioral tests in `CombinedPrintQueueSinkTests` (re-run in Step 3.4 and Step 5.3) — body is preserved verbatim from the deleted file. FR-5 covered by Task 4. NFR-4 covered by Step 5.1 + 5.2. NFR-5 holds trivially — no `appsettings*.json` files are touched.
- **Amendments from arch-review:** Amendment 1 (relocation target = `Anela.Heblo.API/Features/ExpeditionList/`) applied in Task 1. Amendment 2 (test placement under `backend/test/Anela.Heblo.Tests/API/`; no new `InternalsVisibleTo` needed) applied in Task 4. Amendment 3 (carry the side-effect comment forward) applied in Step 2.2 — comment updated to mention "the factory below" since the line below the comment is now a factory rather than a concrete-type registration.
- **No placeholders:** every code block is the literal file/edit content. No "TODO", no "similar to above".
- **Type consistency:** the class name `CombinedPrintQueueSink`, its constructor parameter names (`azureSink`, `cupsSink`), and the keyed strings (`"azure"`, `"cups"`) match across Tasks 1, 2, and 4. The factory call shape in Step 2.2 matches the constructor shape declared in Step 1.1.
