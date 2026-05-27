# Decouple ExpeditionListArchive from ExpeditionList — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate every compile-time reference from `Anela.Heblo.Application.Features.ExpeditionListArchive` (and its API controller) into `Anela.Heblo.Application.Features.ExpeditionList` by (a) moving the misplaced `POST run-fix` endpoint into a new `ExpeditionListController`, (b) promoting `IPrintQueueSink` into `Anela.Heblo.Application.Shared.Printing`, (c) giving the Archive module its own `ExpeditionListArchiveOptions`, and (d) fencing the new boundary with a `ModuleBoundariesTests` rule.

**Architecture:** Pure refactor. `IPrintQueueSink` becomes a shared abstraction at `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` (parallel to `Application.Shared.WebSearch.IWebSearchClient`). The Archive module gets its own `ExpeditionListArchiveOptions` bound from a new top-level `ExpeditionListArchive` config section, with a `BlobContainerName` field that mirrors the value used by `ExpeditionList`. The `run-fix` action moves to `Anela.Heblo.API/Controllers/ExpeditionListController.cs` at `POST /api/expedition-list/run-fix`. The frontend hook is repointed; the regenerated OpenAPI client method renames from `expeditionListArchive_RunFix` to `expeditionList_RunFix`. A new `ModuleBoundaryRule` in `ModuleBoundariesTests.Rules()` locks the boundary down.

**Tech Stack:** .NET 8, C# nullable reference types, MediatR, MVC controllers, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection`, xUnit + Moq for backend tests, NSwag for OpenAPI → TypeScript client generation, React + TanStack Query + Jest for frontend. No new NuGet packages, no DB migrations.

---

## File Structure

### New files
| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs` | Shared print-queue sink contract. Moved from `Application.Features.ExpeditionList.Services`. Same signature (`Task SendAsync(IEnumerable<string>, CancellationToken)`). Consumed by `ExpeditionListService` and `ReprintExpeditionListHandler`; implemented by `FileSystemPrintQueueSink`, `CombinedPrintQueueSink`, `CupsPrintQueueSink`, `AzureBlobPrintQueueSink`. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveOptions.cs` | Archive-owned options class. Single field: `string BlobContainerName` defaulting to `"expedition-lists"`. Bound from config section `"ExpeditionListArchive"`. |
| `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs` | New controller for `ExpeditionList`-owned HTTP actions. Single action: `POST /api/expedition-list/run-fix`. `[Authorize]`, `[ApiController]`, `[Route("api/expedition-list")]`. Constructor-injects `IMediator`. |

### Files deleted
| Path | Reason |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs` | Moved to `Application/Shared/Printing/IPrintQueueSink.cs` (same content, new namespace). |

### Files modified — backend production code
| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` | Remove `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;`. Remove the `[HttpPost("run-fix")]` action (lines 56–62). Keep `GetDates`, `GetByDate`, `Download`, `Reprint` untouched. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` | Change signature from `AddExpeditionListArchiveModule(this IServiceCollection services)` to `AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)`. Replace usings: drop `Anela.Heblo.Application.Features.ExpeditionList` and `Anela.Heblo.Application.Features.ExpeditionList.Services`; add `Anela.Heblo.Application.Shared.Printing`. Bind `services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey))`. Update the explicit handler factory to inject `IOptions<ExpeditionListArchiveOptions>` and resolve the shared `IPrintQueueSink`. |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Change `services.AddExpeditionListArchiveModule();` to `services.AddExpeditionListArchiveModule(configuration);`. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` | Replace usings: drop `Anela.Heblo.Application.Features.ExpeditionList` and `Anela.Heblo.Application.Features.ExpeditionList.Services`; add `Anela.Heblo.Application.Shared.Printing`. Change constructor parameter `IOptions<PrintPickingListOptions> options` → `IOptions<ExpeditionListArchiveOptions> options`. Body unchanged. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` | Drop `using Anela.Heblo.Application.Features.ExpeditionList;`. Change ctor parameter `IOptions<PrintPickingListOptions>` → `IOptions<ExpeditionListArchiveOptions>`. Body unchanged. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` | Drop `using Anela.Heblo.Application.Features.ExpeditionList;`. Change ctor parameter `IOptions<PrintPickingListOptions>` → `IOptions<ExpeditionListArchiveOptions>`. Body unchanged. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` | Drop `using Anela.Heblo.Application.Features.ExpeditionList;`. Change ctor parameter `IOptions<PrintPickingListOptions>` → `IOptions<ExpeditionListArchiveOptions>`. Body unchanged. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs` | Update namespace import: `using Anela.Heblo.Application.Shared.Printing;` is added at the top. Keep the class in its existing namespace `Anela.Heblo.Application.Features.ExpeditionList.Services` — only the *implemented* interface moves. |
| `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` | Update namespace import: add `using Anela.Heblo.Application.Shared.Printing;`. Field/ctor signature unchanged. |
| `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. Keep `using Anela.Heblo.Application.Features.ExpeditionList;` — this is the **producer** side and is allowed to read `PrintPickingListOptions.BlobConnectionString`/`BlobContainerName` to build the `BlobContainerClient`. |

### Files modified — configuration (deployment gate)
| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.API/appsettings.json` | Add `"ExpeditionListArchive": { "BlobContainerName": "expedition-lists" }` at the top level (mirror of `ExpeditionList:BlobContainerName`). |
| `backend/src/Anela.Heblo.API/appsettings.Development.json` | Add `"ExpeditionListArchive": { "BlobContainerName": "expedition-lists-stg" }` (mirror of the Dev override). |
| `backend/src/Anela.Heblo.API/appsettings.Test.json` | Add `"ExpeditionListArchive": { "BlobContainerName": "expedition-lists-stg" }` (mirror of the Test override). |
| `backend/src/Anela.Heblo.API/appsettings.Staging.json` | Add `"ExpeditionListArchive": { "BlobContainerName": "expedition-lists-stg" }` (mirror of the Staging override). |
| `backend/src/Anela.Heblo.API/appsettings.Production.json` | No change needed — `ExpeditionList` block here only overrides `PrintSink`, container name comes from the default in `appsettings.json` (`"expedition-lists"`). The default in `ExpeditionListArchiveOptions` (`"expedition-lists"`) also matches, so production is safe with or without an explicit override. Operator note added in the runbook below. |

### Files modified — backend tests
| Path | Change |
|---|---|
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList;` + `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Features.ExpeditionListArchive;` + `using Anela.Heblo.Application.Shared.Printing;`. Update the ctor wiring: `Options.Create(new PrintPickingListOptions())` → `Options.Create(new ExpeditionListArchiveOptions())`. `ContainerName` constant stays `"expedition-lists"` (matches both defaults). |
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList;` with `using Anela.Heblo.Application.Features.ExpeditionListArchive;`. Update `Options.Create(new PrintPickingListOptions())` → `Options.Create(new ExpeditionListArchiveOptions())`. |
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` | Same swap as above. |
| `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` | Same swap as above. |
| `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. Keep the `using Anela.Heblo.Application.Features.ExpeditionList;` (still needed for `PrintPickingListOptions`, `ExpeditionListService`). |
| `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. Keep `using Anela.Heblo.Application.Features.ExpeditionList;` (still needed for `PrintPickingListOptions`). |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` | Replace `using Anela.Heblo.Application.Features.ExpeditionList.Services;` with `using Anela.Heblo.Application.Shared.Printing;`. Keep `using Anela.Heblo.Application.Features.ExpeditionList;` (`FileSystemPrintQueueSink` lives there). |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Append a new `ModuleBoundaryRule` row for `"ExpeditionListArchive -> ExpeditionList"`. No allowlist needed — the refactor removes every violation. |

### Files modified — frontend
| Path | Change |
|---|---|
| `frontend/src/api/hooks/useExpeditionListArchive.ts` | Change `/api/expedition-list-archive/run-fix` → `/api/expedition-list/run-fix` (one occurrence, line 134). |
| `frontend/src/api/generated/api-client.ts` | Regenerated by NSwag from the new OpenAPI spec. The method `expeditionListArchive_RunFix` is replaced by `expeditionList_RunFix` (and its `process…` partner). Do not hand-edit — let `npm run generate-client` produce it. |

### Files explicitly NOT modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — the *producer* side of the print pipeline still needs all of its fields (`BlobConnectionString`, `BlobContainerName`, `EmailSender`, `PrintQueueFolder`, state-id fields, `PrintSink`).
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/` — handler, request, response stay put; only the controller action that dispatches them moves.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/ExpeditionListModule.cs` — module signature, namespace, and bindings are unchanged.
- `frontend/src/pages/ExpeditionListArchivePage.tsx` — UI is unchanged. Button placement, `handleRunFix`, and the hook import all stay.
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — mocks the hook, never asserts on URLs; no change needed.

---

## Task 1: Move `IPrintQueueSink` to `Application.Shared.Printing`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs`
- Delete: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`
- Modify: `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`
- Modify tests: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`, `CombinedPrintQueueSinkTests.cs`, `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`, `…/Infrastructure/ShoptetIntegrationTestFixture.cs`

This task is mechanical: relocate the interface and update every consumer's `using` directive. Build must stay green at the end.

- [ ] **Step 1.1: Create the new interface file at the shared location**

Write `backend/src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Shared.Printing;

public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
```

Verify before moving on:
- Namespace is exactly `Anela.Heblo.Application.Shared.Printing`.
- Signature matches the previous interface byte-for-byte (no rename, no parameter reorder, no `IAsyncDisposable`-style additions).

- [ ] **Step 1.2: Build the Application project to confirm both copies coexist temporarily**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build **fails** with one or more errors complaining about ambiguous `IPrintQueueSink` references (two interfaces with the same simple name in two namespaces). This is expected and tells us the new file was added correctly. We delete the old one next.

If the build instead **succeeds**, you have not created the new file or the namespace is wrong — fix and retry.

- [ ] **Step 1.3: Delete the old interface file**

```bash
rm backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs
```

- [ ] **Step 1.4: Build again — this time it fails with "type or namespace name 'IPrintQueueSink' could not be found" errors across consumers**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build fails. Errors point to files that still reference the old namespace. List them — they should match the modification list below.

- [ ] **Step 1.5: Update `FileSystemPrintQueueSink.cs` to consume the new namespace**

Open `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs`. Replace the existing file content with:

```csharp
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

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

Only the very first line (`using Anela.Heblo.Application.Shared.Printing;`) is new. The class stays in `Anela.Heblo.Application.Features.ExpeditionList.Services` because the implementation belongs to ExpeditionList (only the *interface* moved).

- [ ] **Step 1.6: Update `ExpeditionListService.cs` — add the new using directive**

In `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`, change the using block at the top from:

```csharp
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

Nothing else in the file changes.

- [ ] **Step 1.7: Update `CombinedPrintQueueSink.cs` — replace the using**

In `backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs`, change line 1 from:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
```

- [ ] **Step 1.8: Update `ServiceCollectionExtensions.cs` — replace the using**

In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, change line 26 from:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
```

Verify the `AddPrintQueueSink` method body (lines ~387–415) still compiles — `IPrintQueueSink`, `FileSystemPrintQueueSink`, `CombinedPrintQueueSink`, `CupsPrintQueueSink`, `AzureBlobPrintQueueSink` should all resolve through the new namespace and their own existing namespaces.

- [ ] **Step 1.9: Update Cups adapter sink — replace the using**

In `backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs`, change line 1 from:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
```

- [ ] **Step 1.10: Update Cups adapter DI extensions — replace the using**

In `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs`, change line 2 from:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
```

- [ ] **Step 1.11: Update Azure adapter sink — replace the using**

In `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs`, change line 1 from:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Services;
```

to:

```csharp
using Anela.Heblo.Application.Shared.Printing;
```

- [ ] **Step 1.12: Update Azure adapter module — replace one using, keep the other**

In `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`, change the usings block:

From:
```csharp
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
```

To:
```csharp
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
```

`using Anela.Heblo.Application.Features.ExpeditionList;` **stays** — this file is the producer side and reads `PrintPickingListOptions.BlobConnectionString` and `BlobContainerName` to build the `BlobContainerClient` (line 20–21). That's allowed.

- [ ] **Step 1.13: Update test files — replace the using in four test files**

For each of these four files, change `using Anela.Heblo.Application.Features.ExpeditionList.Services;` to `using Anela.Heblo.Application.Shared.Printing;`:

1. `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` (line 2)
2. `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs` (line 2)
3. `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` (line 3)
4. `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs` (line 6)

In files 1, 3, and 4, `using Anela.Heblo.Application.Features.ExpeditionList;` (the non-`.Services` one) **stays** — they still reference `PrintPickingListOptions`, `ExpeditionListService`, and/or `FileSystemPrintQueueSink`.

- [ ] **Step 1.14: Build the full backend solution**

```bash
cd backend && dotnet build
```

Expected: build succeeds with zero errors. If any "type or namespace name 'IPrintQueueSink' could not be found" errors remain, the file referenced has not been updated — fix it and retry.

- [ ] **Step 1.15: Verify with grep that no production code under `Application.Features.ExpeditionList.Services` references `IPrintQueueSink`**

```bash
grep -rIn "Application.Features.ExpeditionList.Services" backend/src
```

Expected: matches only inside `Anela.Heblo.Application.Features.ExpeditionList.Services` namespace declarations (the folder's own files) and any other types that legitimately live in that namespace (e.g. `IExpeditionListService`, `FileSystemPrintQueueSink`, `ExpeditionListService`). No `using` directive in any other file should pull from that namespace for `IPrintQueueSink`.

- [ ] **Step 1.16: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs \
  src/Anela.Heblo.Application/Features/ExpeditionList/Services/IPrintQueueSink.cs \
  src/Anela.Heblo.Application/Features/ExpeditionList/Services/FileSystemPrintQueueSink.cs \
  src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs \
  src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs \
  src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs \
  src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs \
  src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs \
  src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs \
  src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs \
  ../test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs \
  ../test/Anela.Heblo.Tests/Features/ExpeditionList/CombinedPrintQueueSinkTests.cs \
  ../test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs \
  ../test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture.cs

git commit -m "refactor: relocate IPrintQueueSink to Application.Shared.Printing"
```

Note: `git add` will stage the deleted file under its old path automatically (the `rm` already removed it from the working tree).

---

## Task 2: Introduce `ExpeditionListArchiveOptions` (Archive-owned config)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveOptions.cs`

The Archive module needs the blob container name without referencing `PrintPickingListOptions`. This task adds the new options type; binding and consumption come in later tasks.

- [ ] **Step 2.1: Create the options class**

Write `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveOptions.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public class ExpeditionListArchiveOptions
{
    public const string ConfigurationKey = "ExpeditionListArchive";

    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

Constraints to verify:
- Namespace is exactly `Anela.Heblo.Application.Features.ExpeditionListArchive`.
- `ConfigurationKey` is the exact string `"ExpeditionListArchive"` (matches the new appsettings section).
- Default value `"expedition-lists"` matches `PrintPickingListOptions.BlobContainerName`'s default — operators without an explicit override behave identically to today.

- [ ] **Step 2.2: Build to confirm the class compiles**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds. No consumer yet.

- [ ] **Step 2.3: Commit**

```bash
cd backend && git add src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveOptions.cs
git commit -m "feat(expedition-list-archive): add ExpeditionListArchiveOptions"
```

---

## Task 3: Wire `ExpeditionListArchiveOptions` into the Archive module and update all four handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs`
- Modify tests: all four `…HandlerTests.cs` files in `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/`

After this task no Archive production file imports `Anela.Heblo.Application.Features.ExpeditionList[.Services]`.

### Step 3.A — Update tests first (RED phase)

Updating tests first proves the new options type is the only thing the handlers need from a configuration source. Each test should fail to compile until the matching handler is updated.

- [ ] **Step 3.1: Update `ReprintExpeditionListHandlerTests.cs`**

Replace `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.ExpeditionListArchive;

public class ReprintExpeditionListHandlerTests
{
    private readonly Mock<IBlobStorageService> _blobStorageServiceMock;
    private readonly Mock<IPrintQueueSink> _cupsSinkMock;
    private readonly ReprintExpeditionListHandler _handler;
    private const string ContainerName = "expedition-lists";

    public ReprintExpeditionListHandlerTests()
    {
        _blobStorageServiceMock = new Mock<IBlobStorageService>();
        _cupsSinkMock = new Mock<IPrintQueueSink>();
        _handler = new ReprintExpeditionListHandler(_blobStorageServiceMock.Object, _cupsSinkMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
    }

    [Fact]
    public async Task Handle_ValidBlobPath_DownloadsAndSendsToCupsSink()
    {
        // Arrange
        var blobPath = "2026-03-25/picking-list-001.pdf";
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var blobStream = new MemoryStream(pdfContent);

        _blobStorageServiceMock
            .Setup(s => s.DownloadAsync(ContainerName, blobPath, default))
            .ReturnsAsync(blobStream);

        _cupsSinkMock
            .Setup(s => s.SendAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(Task.CompletedTask);

        var request = new ReprintExpeditionListRequest { BlobPath = blobPath };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _blobStorageServiceMock.Verify(s => s.DownloadAsync(ContainerName, blobPath, default), Times.Once);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.Is<IEnumerable<string>>(paths => paths.Any()), default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob()
    {
        // Arrange
        var request = new ReprintExpeditionListRequest { BlobPath = "../malicious/path.pdf" };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        _blobStorageServiceMock.Verify(
            s => s.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cupsSinkMock.Verify(
            s => s.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 3.2: Update `DownloadExpeditionListHandlerTests.cs`**

Edit the file: replace `using Anela.Heblo.Application.Features.ExpeditionList;` with `using Anela.Heblo.Application.Features.ExpeditionListArchive;` and replace `Options.Create(new PrintPickingListOptions())` with `Options.Create(new ExpeditionListArchiveOptions())`. Nothing else changes.

The constructor line should now read:

```csharp
_handler = new DownloadExpeditionListHandler(_blobStorageServiceMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
```

- [ ] **Step 3.3: Update `GetExpeditionDatesHandlerTests.cs`**

Same swap as Step 3.2 (`using` + `Options.Create(...)`).

Constructor line:
```csharp
_handler = new GetExpeditionDatesHandler(_blobStorageServiceMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
```

- [ ] **Step 3.4: Update `GetExpeditionListsByDateHandlerTests.cs`**

Same swap.

Constructor line:
```csharp
_handler = new GetExpeditionListsByDateHandler(_blobStorageServiceMock.Object, Options.Create(new ExpeditionListArchiveOptions()));
```

- [ ] **Step 3.5: Build the test project — expect FAIL**

```bash
cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build fails with errors like `CS1503: Argument 3: cannot convert from 'IOptions<ExpeditionListArchiveOptions>' to 'IOptions<PrintPickingListOptions>'` for each of the four handler constructors. This is the RED state — handlers still expect the old type. Next we flip the handlers.

### Step 3.B — Update handlers to consume the new options (GREEN phase)

- [ ] **Step 3.6: Update `ReprintExpeditionListHandler.cs`**

Replace `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` with:

```csharp
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly IPrintQueueSink _cupsSink;
    private readonly string _containerName;

    public ReprintExpeditionListHandler(IBlobStorageService blobStorageService, IPrintQueueSink cupsSink, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _cupsSink = cupsSink;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail("Invalid blob path.");
        }

        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            await using var blobStream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            await using var fileStream = File.OpenWrite(tempFile);
            await blobStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            DeleteTempFile(tempFile);
            throw;
        }

        try
        {
            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static void DeleteTempFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
```

Verify before moving on: zero `using Anela.Heblo.Application.Features.ExpeditionList` directives. The class is in the same namespace as before. The body (Handle + DeleteTempFile) is byte-for-byte identical to the original — only the constructor's parameter type and the usings changed.

- [ ] **Step 3.7: Update `DownloadExpeditionListHandler.cs`**

Replace `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` with:

```csharp
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListHandler : IRequestHandler<DownloadExpeditionListRequest, DownloadExpeditionListResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public DownloadExpeditionListHandler(IBlobStorageService blobStorageService, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<DownloadExpeditionListResponse> Handle(DownloadExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return DownloadExpeditionListResponse.Fail("Invalid blob path.");
        }

        var stream = await _blobStorageService.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
        var fileName = Path.GetFileName(request.BlobPath);

        return new DownloadExpeditionListResponse
        {
            Success = true,
            Stream = stream,
            ContentType = "application/pdf",
            FileName = fileName
        };
    }
}
```

Note: `ExpeditionListArchiveOptions` lives in the same namespace as the handler's enclosing module (`Anela.Heblo.Application.Features.ExpeditionListArchive`), which is a parent namespace of this handler — no `using` directive is needed for it (the compiler walks up the namespace tree). If you prefer to be explicit, add `using Anela.Heblo.Application.Features.ExpeditionListArchive;`. The build is the source of truth.

- [ ] **Step 3.8: Update `GetExpeditionDatesHandler.cs`**

Replace `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` with:

```csharp
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public GetExpeditionDatesHandler(IBlobStorageService blobStorageService, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(_containerName, null, cancellationToken);

        var dates = blobs
            .Select(b => b.Name.Split('/')[0])
            .Where(IsValidDatePrefix)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        var totalCount = dates.Count;
        var pagedDates = dates
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new GetExpeditionDatesResponse
        {
            Dates = pagedDates,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static bool IsValidDatePrefix(string prefix)
    {
        return DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _);
    }
}
```

- [ ] **Step 3.9: Update `GetExpeditionListsByDateHandler.cs`**

Replace `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateHandler : IRequestHandler<GetExpeditionListsByDateRequest, GetExpeditionListsByDateResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public GetExpeditionListsByDateHandler(IBlobStorageService blobStorageService, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return new GetExpeditionListsByDateResponse { Items = new List<ExpeditionListItemDto>() };
        }

        var blobs = await _blobStorageService.ListBlobsAsync(_containerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                ListId = Path.GetFileNameWithoutExtension(b.FileName),
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
}
```

### Step 3.C — Update the Archive module to bind options and resolve the shared sink

- [ ] **Step 3.10: Update `ExpeditionListArchiveModule.cs`**

Replace `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` with:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

        // ReprintExpeditionListHandler needs the keyed "cups" IPrintQueueSink when available
        // (production/staging). In environments where only the non-keyed sink is registered
        // (e.g. FileSystem in development/test), we fall back to the non-keyed registration.
        // This explicit factory overrides MediatR's auto-registration so the correct sink is injected.
        services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
        {
            var blobStorage = provider.GetRequiredService<IBlobStorageService>();
            var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
                ?? provider.GetRequiredService<IPrintQueueSink>();
            var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
            return new ReprintExpeditionListHandler(blobStorage, cupsSink, options);
        });

        return services;
    }
}
```

Verify: zero `using Anela.Heblo.Application.Features.ExpeditionList` directives.

- [ ] **Step 3.11: Update the call site in `ApplicationModule.cs`**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, change line 96 from:

```csharp
        services.AddExpeditionListArchiveModule();
```

to:

```csharp
        services.AddExpeditionListArchiveModule(configuration);
```

This is the only call site (verified via `grep AddExpeditionListArchiveModule backend/src`).

- [ ] **Step 3.12: Build everything**

```bash
cd backend && dotnet build
```

Expected: build succeeds with zero errors.

- [ ] **Step 3.13: Run the touched test suites**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive"
```

Expected: all four `*HandlerTests` pass green.

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionList&FullyQualifiedName!~Archive"
```

Expected: existing `ExpeditionListServicePrintSinkTests`, `ExpeditionListServiceOrderStateTests`, `CombinedPrintQueueSinkTests` all pass.

- [ ] **Step 3.14: Verify by grep that the Archive module has zero `Application.Features.ExpeditionList` imports**

```bash
grep -RIn "using Anela.Heblo.Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive
```

Expected: zero matches. (The `\b` excludes `ExpeditionListArchive`.)

- [ ] **Step 3.15: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs \
  src/Anela.Heblo.Application/ApplicationModule.cs \
  src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs \
  src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs \
  src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs \
  src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs \
  ../test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs \
  ../test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs \
  ../test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs \
  ../test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs

git commit -m "refactor(expedition-list-archive): consume Archive-owned options, drop ExpeditionList imports"
```

---

## Task 4: Add new `ExpeditionListArchive` config sections to appsettings

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Development.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Test.json`
- Modify: `backend/src/Anela.Heblo.API/appsettings.Staging.json`

`appsettings.Production.json` is intentionally untouched — the default (`"expedition-lists"`) in `appsettings.json` flows through, which is what production uses today.

- [ ] **Step 4.1: Add the section to `appsettings.json`**

In `backend/src/Anela.Heblo.API/appsettings.json`, immediately after the `"ExpeditionList": { ... }` block (which ends around line 533), insert a new top-level section. To be precise, find this block:

```json
  "ExpeditionList": {
    "EmailSender": "heblo@anela.cz",
    "PrintQueueFolder": "PDFPrints",
    "SourceStateId": -2,
    "FixSourceStateId": 73,
    "DesiredStateId": 26,
    "SendToPrinterByDefault": true,
    "ChangeOrderStateByDefault": true,
    "PrintSink": "AzureBlob",
    "BlobConnectionString": "DefaultEndpointsProtocol=...",
    "BlobContainerName": "expedition-lists"
  },
```

…and append a sibling section after the closing brace and trailing comma:

```json
  "ExpeditionListArchive": {
    "BlobContainerName": "expedition-lists"
  },
```

Match the existing indentation (2 spaces). Make sure the trailing comma is present on the new block if it is followed by another key (it will be — `"Cups": { … }` is the next key).

- [ ] **Step 4.2: Add the section to `appsettings.Development.json`**

In `backend/src/Anela.Heblo.API/appsettings.Development.json`, find the existing `"ExpeditionList"` block:

```json
  "ExpeditionList": {
    "BlobContainerName": "expedition-lists-stg",
    "PrintSink": "AzureBlob"
  },
```

Add a sibling block after it:

```json
  "ExpeditionListArchive": {
    "BlobContainerName": "expedition-lists-stg"
  },
```

- [ ] **Step 4.3: Add the section to `appsettings.Test.json`**

In `backend/src/Anela.Heblo.API/appsettings.Test.json`, find:

```json
  "ExpeditionList": {
    "BlobConnectionString": "UseDevelopmentStorage=true",
    "BlobContainerName": "expedition-lists-stg"
  },
```

Add a sibling block after it:

```json
  "ExpeditionListArchive": {
    "BlobContainerName": "expedition-lists-stg"
  },
```

- [ ] **Step 4.4: Add the section to `appsettings.Staging.json`**

In `backend/src/Anela.Heblo.API/appsettings.Staging.json`, find:

```json
  "ExpeditionList": {
    "BlobContainerName": "expedition-lists-stg"
  },
```

Add a sibling block after it:

```json
  "ExpeditionListArchive": {
    "BlobContainerName": "expedition-lists-stg"
  },
```

- [ ] **Step 4.5: Verify the JSON files parse**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds. If a JSON file is malformed (missing comma, dangling brace), the build will surface a clear error.

Also visually verify with `python3 -m json.tool` on each modified file — but only as a sanity check, not a hard gate.

- [ ] **Step 4.6: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.API/appsettings.json \
  src/Anela.Heblo.API/appsettings.Development.json \
  src/Anela.Heblo.API/appsettings.Test.json \
  src/Anela.Heblo.API/appsettings.Staging.json

git commit -m "config: add ExpeditionListArchive:BlobContainerName section per environment"
```

---

## Task 5: Move `run-fix` to a new `ExpeditionListController`

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`

This task changes the public HTTP surface. After this task, `POST /api/expedition-list-archive/run-fix` returns 404 and `POST /api/expedition-list/run-fix` returns 200 — but the frontend hook still points at the old URL. Frontend repointing is Task 7.

- [ ] **Step 5.1: Create the new controller**

Write `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/expedition-list")]
public class ExpeditionListController : BaseApiController
{
    private readonly IMediator _mediator;

    public ExpeditionListController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("run-fix")]
    public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(CancellationToken cancellationToken)
    {
        var request = new RunExpeditionListPrintFixRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}
```

Verify before moving on:
- Route is `api/expedition-list` (no trailing slash, lowercase).
- `[Authorize]` is present.
- Derives from `BaseApiController`.
- Action signature matches the one currently in `ExpeditionListArchiveController.RunFix` byte-for-byte (only the enclosing class is new).

- [ ] **Step 5.2: Remove the `run-fix` action from `ExpeditionListArchiveController.cs`**

Open `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` and:

1. Delete line 1 (the `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;` directive).
2. Delete lines 56–62 (the entire `RunFix` action and its `[HttpPost("run-fix")]` attribute).

The resulting file should look like:

```csharp
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/expedition-list-archive")]
public class ExpeditionListArchiveController : BaseApiController
{
    private readonly IMediator _mediator;

    public ExpeditionListArchiveController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("dates")]
    public async Task<ActionResult<GetExpeditionDatesResponse>> GetDates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new GetExpeditionDatesRequest { Page = page, PageSize = pageSize };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("{date}")]
    public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
    {
        var request = new GetExpeditionListsByDateRequest { Date = date };
        var response = await _mediator.Send(request);
        return Ok(response);
    }

    [HttpGet("download/{*blobPath}")]
    public async Task<ActionResult> Download(string blobPath)
    {
        var request = new DownloadExpeditionListRequest { BlobPath = blobPath };
        var response = await _mediator.Send(request);

        if (!response.Success || response.Stream == null)
        {
            return BadRequest(response.ErrorMessage);
        }

        return File(response.Stream, response.ContentType, response.FileName);
    }

    [HttpPost("reprint")]
    public async Task<ActionResult<ReprintExpeditionListResponse>> Reprint([FromBody] ReprintExpeditionListRequest request)
    {
        var response = await _mediator.Send(request);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
```

- [ ] **Step 5.3: Build the API project**

```bash
cd backend && dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: build succeeds. The PostBuild step will (in Debug) regenerate the frontend TypeScript client; that's fine — Task 7 will commit the regenerated file.

- [ ] **Step 5.4: Verify by grep that the Archive controller no longer imports from `ExpeditionList.UseCases`**

```bash
grep -n "Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
```

Expected: zero matches.

```bash
grep -n "Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs
```

Expected: one match — the `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;` directive. That's correct — this controller *is* the ExpeditionList HTTP surface.

- [ ] **Step 5.5: Commit**

```bash
cd backend && git add \
  src/Anela.Heblo.API/Controllers/ExpeditionListController.cs \
  src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs

git commit -m "refactor: move run-fix endpoint to ExpeditionListController at /api/expedition-list/run-fix"
```

---

## Task 6: Add the module-boundary regression fence

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

The fence prevents anyone re-introducing an Archive → ExpeditionList import. Without this, the refactor decays.

- [ ] **Step 6.1: Add the new rule to `ModuleBoundariesTests.Rules()`**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, find the `Rules()` method (around line 94). It returns a `TheoryData<ModuleBoundaryRule>` collection with five `new ModuleBoundaryRule(...)` rows. Append a sixth row, comma-separated, immediately before the closing `}`:

Find:
```csharp
        new ModuleBoundaryRule(
            Name: "Purchase -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Purchase",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: PurchaseAllowlist),
    };
```

Replace with:
```csharp
        new ModuleBoundaryRule(
            Name: "Purchase -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Purchase",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: PurchaseAllowlist),

        new ModuleBoundaryRule(
            Name: "ExpeditionListArchive -> ExpeditionList",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.ExpeditionList",
                "Anela.Heblo.Application.Features.ExpeditionList",
                "Anela.Heblo.Persistence.ExpeditionList",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),
    };
```

Notes on the rule:
- `InspectedNamespacePrefix` matches the consumer module's namespace exactly. Since the test uses `StartsWith`, types under `…ExpeditionListArchive.UseCases.*`, `…ExpeditionListArchive.Contracts.*`, etc. are all inspected.
- The three forbidden prefixes match exactly how every other rule in this file is shaped (Domain, Application, Persistence). The Domain and Persistence prefixes may not have any types yet — that's fine; the rule fires only when a reference exists.
- Allowlist is empty: the refactor removes every violation. If, after running, an unexpected residual reference surfaces (e.g. a compiler-generated state machine pulling in `PrintPickingListOptions`), only then consider an allowlist entry — and only with a comment explaining why it's out of scope.

- [ ] **Step 6.2: Run the architecture test**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: all six rules pass green, including the new one. If the new rule fails, read the violation list — every entry must point to a type or member still referencing `Anela.Heblo.Application.Features.ExpeditionList.*` from within `…ExpeditionListArchive`. Fix the offender (it's a missed using statement); do not add to the allowlist as a shortcut.

- [ ] **Step 6.3: Commit**

```bash
cd backend && git add test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): fence ExpeditionListArchive -> ExpeditionList boundary"
```

---

## Task 7: Repoint the frontend hook and regenerate the OpenAPI client

**Files:**
- Modify: `frontend/src/api/hooks/useExpeditionListArchive.ts`
- Modify (regenerated): `frontend/src/api/generated/api-client.ts`

The hand-written hook and the generated client both reference the old URL/method. The hook is a one-line change; the generated client must be produced from the new backend OpenAPI document, not hand-edited.

- [ ] **Step 7.1: Update the URL in the hand-written hook**

In `frontend/src/api/hooks/useExpeditionListArchive.ts`, find line 134:

```typescript
      const relativeUrl = `/api/expedition-list-archive/run-fix`;
```

Replace with:

```typescript
      const relativeUrl = `/api/expedition-list/run-fix`;
```

Nothing else in the file changes. Other URLs (`/api/expedition-list-archive/dates`, `/api/expedition-list-archive/${date}`, `/api/expedition-list-archive/reprint`, `/api/expedition-list-archive/download/...`) stay on the Archive controller because those endpoints stay on the Archive controller.

- [ ] **Step 7.2: Regenerate the TypeScript client from the updated backend**

The frontend regenerates the client on prebuild. Trigger the regeneration explicitly:

```bash
cd frontend && npm run generate-client
```

This runs `dotnet msbuild ../backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`, which executes `dotnet nswag run nswag.frontend.json`. The script reads the current API project, builds an OpenAPI document, and writes `frontend/src/api/generated/api-client.ts`.

After it completes, verify that the file now contains the new method:

```bash
grep -n "expeditionList_RunFix\|expedition-list/run-fix" frontend/src/api/generated/api-client.ts
```

Expected: matches for both `expeditionList_RunFix()` and `"/api/expedition-list/run-fix"`. Older references should be gone:

```bash
grep -n "expeditionListArchive_RunFix\|expedition-list-archive/run-fix" frontend/src/api/generated/api-client.ts
```

Expected: zero matches.

- [ ] **Step 7.3: Run the frontend build**

```bash
cd frontend && npm run build
```

Expected: build succeeds, no TypeScript errors. The hook references `useMutation` of an inline-typed fetch — no generated type is pulled in for `expeditionList_RunFix` (the hand-written hook hits the URL directly), so the rename in the generated client is invisible to the hook.

- [ ] **Step 7.4: Run the frontend lint**

```bash
cd frontend && npm run lint
```

Expected: passes with no errors related to the changed files.

- [ ] **Step 7.5: Run the page test**

```bash
cd frontend && npm test -- --testPathPattern=ExpeditionListArchivePage --watchAll=false
```

Expected: all tests in `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` pass. The test mocks the hook module wholesale, so the URL change is transparent to it.

- [ ] **Step 7.6: Verify no remaining references to the old URL or method anywhere in `frontend/src`**

```bash
grep -RIn "expedition-list-archive/run-fix\|expeditionListArchive_RunFix" frontend/src
```

Expected: zero matches.

- [ ] **Step 7.7: Commit**

```bash
git add \
  frontend/src/api/hooks/useExpeditionListArchive.ts \
  frontend/src/api/generated/api-client.ts

git commit -m "feat(frontend): repoint run-fix hook to /api/expedition-list/run-fix"
```

---

## Task 8: Final validation — backend + frontend full pipeline

**Files:** none modified in this task — verification only.

- [ ] **Step 8.1: Full backend build and format**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

Expected: both commands succeed. `dotnet format --verify-no-changes` returns non-zero if any formatting drift was introduced; if so, run `dotnet format` without the flag and commit the formatting fixups.

- [ ] **Step 8.2: Full backend test suite**

```bash
cd backend && dotnet test
```

Expected: all tests pass. Pay specific attention to:
- `ModuleBoundariesTests` — six rules green, including the new ExpeditionListArchive → ExpeditionList row.
- `ReprintExpeditionListHandlerTests`, `DownloadExpeditionListHandlerTests`, `GetExpeditionDatesHandlerTests`, `GetExpeditionListsByDateHandlerTests` — all four green using `ExpeditionListArchiveOptions`.
- `ExpeditionListServicePrintSinkTests`, `ExpeditionListServiceOrderStateTests`, `CombinedPrintQueueSinkTests` — still green (the print pipeline is unchanged at runtime).

- [ ] **Step 8.3: Manual smoke test of the new endpoint**

Start the API locally (Development environment):

```bash
cd backend/src/Anela.Heblo.API && dotnet run
```

In another shell, confirm the new endpoint is registered and the old one is not. Replace `<TOKEN>` with a real bearer token if needed (the endpoint is `[Authorize]`):

```bash
curl -i -X POST http://localhost:5001/api/expedition-list/run-fix -H "Authorization: Bearer <TOKEN>"
```

Expected: HTTP 200 + JSON `RunExpeditionListPrintFixResponse` body.

```bash
curl -i -X POST http://localhost:5001/api/expedition-list-archive/run-fix -H "Authorization: Bearer <TOKEN>"
```

Expected: HTTP 404.

Stop the API process.

- [ ] **Step 8.4: Frontend final build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: both pass.

- [ ] **Step 8.5: Final grep audit**

```bash
grep -RIn "using Anela.Heblo.Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive
```
Expected: zero matches.

```bash
grep -RIn "using Anela.Heblo.Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs
```
Expected: zero matches.

```bash
grep -RIn "expedition-list-archive/run-fix" frontend/src backend/src
```
Expected: zero matches.

If any of these greps surface a hit, stop and fix it before declaring done.

- [ ] **Step 8.6: (Optional) Deployment runbook note**

Before merging, update the deployment runbook (or the PR description if no runbook exists) with:

> **Deployment gate:** Production reads `ExpeditionListArchive:BlobContainerName` from configuration. The default in `ExpeditionListArchiveOptions` is `"expedition-lists"`, which matches the existing production value of `ExpeditionList:BlobContainerName`. **No Azure App Service settings change is strictly required** for production to keep working, but adding an explicit `ExpeditionListArchive:BlobContainerName="expedition-lists"` to the production app settings makes the new contract explicit and avoids surprise if the default ever changes. Staging/Test/Development override blocks ship with this PR.

No code change here — just communication.

---

## Self-Review

**Spec coverage:** I walked the spec section-by-section against the tasks above.

| Spec section | Implementing task(s) |
|---|---|
| FR-1 (relocate `run-fix` endpoint) | Task 5 |
| FR-2 (frontend hook + regenerated client + page test) | Task 7 |
| FR-3 (kill Archive → ExpeditionList imports) | Tasks 1–3 |
| FR-4 (no functional regression) | Task 3.13, Task 8.2 (test suites), Task 8.3 (smoke) |
| NFR-1 (perf) | Implicit — handlers unchanged |
| NFR-2 (security) | Auth attribute on new controller (Task 5.1); no new secrets — Task 4 adds non-secret container names only |
| NFR-3 (no backwards-compat alias) | Task 5.2 removes the old route; Task 8.3 confirms 404 |
| NFR-4 (testability — Archive tests don't need ExpeditionList types) | Task 3.A test updates strip the `using Anela.Heblo.Application.Features.ExpeditionList;` directive from all four test files |
| Data model — new `ExpeditionListArchiveOptions` | Task 2 |
| Data model — config delta in every appsettings | Task 4 |
| Open Question 1 (contract namespace) | Resolved to `Application.Shared.Printing` — Task 1 |
| Open Question 2 (config migration) | Resolved to new section — Task 4 |
| Open Question 3 (`ExpeditionListController` scope) | Resolved to single `RunFix` action — Task 5.1 |
| Arch-review amendment — `ModuleBoundariesTests` fence | Task 6 |
| Arch-review amendment — `AddExpeditionListArchiveModule` signature | Task 3.10 + Task 3.11 |
| Arch-review note — all four handlers, not just Reprint | Task 3.6–3.9 covers all four |

**Placeholder scan:** No "TBD", "TODO", "add appropriate error handling", "similar to Task N" placeholders. Every code-change step contains the actual code to write. Every command step contains the actual command to run.

**Type consistency:** `ExpeditionListArchiveOptions` is named the same in Task 2 (definition), Task 3 (consumption — module, handlers, tests), and Task 4 (config section). `IPrintQueueSink` keeps its signature (`Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)`) across Task 1's old and new locations. `ExpeditionListController` is the same class name in Task 5.1 (definition) and used implicitly via the new route in Task 7 and Task 8.3.

No issues found.
