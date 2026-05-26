Now I have enough context. Let me write the architecture review.

```markdown
# Architecture Review: Remove ExpeditionListArchive → ExpeditionList cross-module dependency

## Skip Design: true

(Pure refactor. No new UI components or visual design decisions — the existing "Run fix" button stays on `ExpeditionListArchivePage` and only the URL it calls changes.)

## Architectural Fit Assessment

The spec correctly identifies a real boundary violation and proposes a fix consistent with `docs/architecture/development_guidelines.md`. However, **active exploration uncovered three facts the spec under-reports**, all of which materially shape the architecture:

1. **All four Archive handlers — not just `ReprintExpeditionListHandler` — depend on `PrintPickingListOptions.BlobContainerName`.** `GetExpeditionDatesHandler.cs:16`, `GetExpeditionListsByDateHandler.cs:17`, `DownloadExpeditionListHandler.cs:16`, and `ReprintExpeditionListHandler.cs:19` each `using Anela.Heblo.Application.Features.ExpeditionList;` solely to read `BlobContainerName`. Fixing only the Reprint handler leaves the boundary violation in place for the other three. **This is a spec amendment** (see below).

2. **`IPrintQueueSink` has two true consumers and five implementations.** `ExpeditionListService` (in `ExpeditionList`) and `ReprintExpeditionListHandler` (in `ExpeditionListArchive`) both inject it. Implementations live in `ExpeditionList.Services` (`FileSystem`), `Anela.Heblo.API.Features.ExpeditionList` (`Combined`), `Anela.Heblo.Adapters.Cups`, and `Anela.Heblo.Adapters.Azure`. Because there is no single "provider" and there are multiple consumers, the **`ILeafletKnowledgeSource` consumer-defined pattern does not fit**. The closest existing precedent is `Anela.Heblo.Application.Shared.WebSearch.IWebSearchClient` — a multi-consumer, multi-implementation abstraction sitting under `Application.Shared`. That is the right shape here.

3. **`AzureBlobPrintQueueSink` (in `Anela.Heblo.Adapters.Azure`) also reads `PrintPickingListOptions.BlobConnectionString` + `BlobContainerName`** to build the `BlobContainerClient`. This is fine — it's the producer side, owned by ExpeditionList — but it means `PrintPickingListOptions` stays put; only the Archive's *consumption* of it is removed.

4. **`Anela.Heblo.Application.Shared` namespace is an established pattern** (`BaseResponse`, `IWebSearchClient`, `IRagQueryExpander`, `IWordWindowChunker`, etc.). The "no Common/Shared projects" rule in `development_guidelines.md` applies to *assemblies*, not namespaces under `Application/`. Multi-consumer abstractions consistently live there.

5. **`ModuleBoundariesTests.cs` enforces five module-boundary rules via reflection — `ExpeditionListArchive` is not yet covered.** Adding it as part of this refactor prevents regression and is consistent with how the codebase has fenced off every other completed decoupling.

## Proposed Architecture

### Component Overview

```
┌──────────────────── Anela.Heblo.API ────────────────────┐
│  Controllers/                                           │
│   ├── ExpeditionListController       [Route("api/      │
│   │     └─ POST run-fix              expedition-list")]│
│   │        → mediator.Send(RunExpeditionListPrintFix)  │
│   └── ExpeditionListArchiveController                  │
│         └─ dates / {date} / download / reprint         │
│            (no more run-fix; no ExpeditionList using)  │
│  Features/ExpeditionList/                              │
│   └── CombinedPrintQueueSink  (impl of IPrintQueueSink)│
└─────────────────────────────────────────────────────────┘
                          │
                          │ MediatR
                          ▼
┌──────────────── Anela.Heblo.Application ────────────────┐
│  Shared/Printing/                                       │
│   └── IPrintQueueSink            ◄── NEW HOME           │
│                                                         │
│  Features/ExpeditionList/                               │
│   ├── PrintPickingListOptions   (unchanged; producer)   │
│   ├── ExpeditionListModule                              │
│   ├── Services/IExpeditionListService, ...              │
│   ├── Services/FileSystemPrintQueueSink                 │
│   └── UseCases/RunExpeditionListPrintFix/               │
│                                                         │
│  Features/ExpeditionListArchive/                        │
│   ├── ExpeditionListArchiveOptions  ◄── NEW             │
│   ├── ExpeditionListArchiveModule                       │
│   └── UseCases/                                         │
│       ├── Reprint…/Handler         ─┐                   │
│       ├── Download…/Handler          │ all 4 inject     │
│       ├── GetExpeditionDates/Handler │ IOptions<        │
│       └── GetExpeditionListsByDate…  ┘ ExpeditionList   │
│                                        ArchiveOptions>  │
└─────────────────────────────────────────────────────────┘
                          ▲
                          │ implements IPrintQueueSink
        ┌─────────────────┼─────────────────┐
        │                 │                 │
┌───────▼──────┐  ┌──────▼─────────┐  ┌────▼─────────────┐
│ Adapters.Cups│  │ Adapters.Azure │  │ API.Features.    │
│ CupsPrint…   │  │ AzureBlobPrint │  │ ExpeditionList.  │
│              │  │ (reads Print   │  │ CombinedPrint…   │
│              │  │  PickingList   │  │                  │
│              │  │  Options —     │  │                  │
│              │  │  producer side)│  │                  │
└──────────────┘  └────────────────┘  └──────────────────┘
```

### Key Design Decisions

#### Decision 1: Home for `IPrintQueueSink`

**Options considered:**
- (a) Consumer-defined contract in `ExpeditionListArchive/Contracts/` (ILeafletKnowledgeSource pattern)
- (b) Producer-owned contract in `ExpeditionList/Contracts/`
- (c) Shared contract in `Anela.Heblo.Application.Shared.Printing/`
- (d) Domain abstraction in `Anela.Heblo.Domain.Features.Printing/` (parallel to `IBlobStorageService`)

**Chosen approach:** (c) `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink`.

**Rationale:** The Leaflet/KnowledgeBase pattern assumes one consumer and one provider. Here there are **two consumers** (`ExpeditionList`, `ExpeditionListArchive`) and **four implementations** in distinct assemblies (`FileSystem`, `Combined`, `Cups`, `AzureBlob`). The exact analog is `Anela.Heblo.Application.Shared.WebSearch.IWebSearchClient`, which is consumed by multiple modules and implemented by infrastructure adapters. Domain (option d) is reserved for true infrastructure abstractions like `IBlobStorageService`; `IPrintQueueSink` is application-flavored (knows about "print jobs", "sinks").

#### Decision 2: Archive-owned options class

**Options considered:**
- (a) Archive binds from the existing `ExpeditionList:BlobContainerName` section (no env-config churn, but Archive still implicitly couples to ExpeditionList's config layout)
- (b) New `ExpeditionListArchive:BlobContainerName` section in appsettings (clean isolation; risk of config drift)
- (c) Define a Provider-owned contract `IExpeditionListBlobLocator` exposing `string ContainerName { get; }`, implemented by ExpeditionList

**Chosen approach:** (b) New `ExpeditionListArchive` config section, bound to a new `ExpeditionListArchiveOptions` class.

**Rationale:** (a) re-introduces compile-time coupling through configuration paths and weakens the boundary. (c) is heavyweight for a single string value. (b) gives clean type-level decoupling at the cost of two appsettings entries staying in sync — which is the same cost the codebase already pays for every other multi-section setting. The drift risk is mitigated by (i) the same default value (`"expedition-lists"`), (ii) a deployment checklist (see Prerequisites), and (iii) a startup-time integration test that asserts both options resolve to the same container in non-development environments.

#### Decision 3: Scope of `ExpeditionListController`

**Options considered:**
- (a) Single action (`RunFix`) only; rest of ExpeditionList HTTP surface stays wherever it currently lives
- (b) Pull all ExpeditionList-owned actions into the new controller as a cleanup

**Chosen approach:** (a) — only `RunFix` moves.

**Rationale:** The spec is correct here. The brief explicitly limits scope to fixing the boundary violation. There are no other ExpeditionList controllers today (`grep -RIn "Route(\"api/expedition-list" backend/src/Anela.Heblo.API/Controllers/`) returns nothing; the only ExpeditionList API project files are `Features/ExpeditionList/CombinedPrintQueueSink.cs`, which isn't a controller. So (b) has no actual work to absorb.

#### Decision 4: Regression fence

**Chosen approach:** Add a new rule to `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:

```
ModuleBoundaryRule(
  Name: "ExpeditionListArchive -> ExpeditionList",
  InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive",
  ForbiddenNamespacePrefixes: [
    "Anela.Heblo.Application.Features.ExpeditionList",
    "Anela.Heblo.Domain.Features.ExpeditionList",     // if it exists
    "Anela.Heblo.Persistence.ExpeditionList",         // if it exists
  ],
  Allowlist: <empty>)
```

**Rationale:** Every prior decoupling work (`Leaflet`, `Article`, `Logistics`, `Purchase`, `PackingMaterials`) is fenced by a rule in this test. Skipping the fence guarantees regression within a few months.

## Implementation Guidance

### Directory / Module Structure

**New files:**
```
backend/src/Anela.Heblo.API/Controllers/
  └── ExpeditionListController.cs                    [NEW]

backend/src/Anela.Heblo.Application/Shared/Printing/
  └── IPrintQueueSink.cs                             [MOVED from ExpeditionList/Services]

backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
  └── ExpeditionListArchiveOptions.cs                [NEW]
```

**Modified files:**
```
backend/src/Anela.Heblo.API/Controllers/
  └── ExpeditionListArchiveController.cs             remove run-fix + using
backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/
  ├── ExpeditionListArchiveModule.cs                 register options; remove ExpeditionList usings; keep keyed-sink factory
  └── UseCases/{Reprint,Download,GetExpeditionDates,
                GetExpeditionListsByDate}/*Handler.cs   inject IOptions<ExpeditionListArchiveOptions>
backend/src/Anela.Heblo.Application/Features/ExpeditionList/
  ├── Services/FileSystemPrintQueueSink.cs           update namespace usage
  ├── Services/ExpeditionListService.cs              update using
  └── Services/IPrintQueueSink.cs                    DELETED (moved)
backend/src/Anela.Heblo.API/Features/ExpeditionList/CombinedPrintQueueSink.cs   update using
backend/src/Adapters/Anela.Heblo.Adapters.Cups/Features/ExpeditionList/CupsPrintQueueSink.cs  update using
backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsAdapterServiceCollectionExtensions.cs       update using
backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/AzureBlobPrintQueueSink.cs  update using
backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs           update using (KEEP PrintPickingListOptions usage — producer side)
backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs           update using for IPrintQueueSink

backend/src/Anela.Heblo.API/appsettings*.json                                   add "ExpeditionListArchive:BlobContainerName"
backend/test/Anela.Heblo.Tests/ExpeditionListArchive/*Tests.cs                  update fixtures to ExpeditionListArchiveOptions
backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs            add new rule

frontend/src/api/hooks/useExpeditionListArchive.ts                              URL → /api/expedition-list/run-fix
frontend/src/api/generated/api-client.ts                                        regenerated
```

**Reference removed:** `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;` from controller, and `using Anela.Heblo.Application.Features.ExpeditionList[.Services];` from every Archive file.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application/Shared/Printing/IPrintQueueSink.cs
namespace Anela.Heblo.Application.Shared.Printing;

public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
```

```csharp
// Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveOptions.cs
namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public class ExpeditionListArchiveOptions
{
    public const string ConfigurationKey = "ExpeditionListArchive";

    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

```csharp
// Anela.Heblo.API/Controllers/ExpeditionListController.cs
[Authorize]
[ApiController]
[Route("api/expedition-list")]
public class ExpeditionListController : BaseApiController
{
    private readonly IMediator _mediator;
    public ExpeditionListController(IMediator mediator) => _mediator = mediator;

    [HttpPost("run-fix")]
    public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new RunExpeditionListPrintFixRequest(), cancellationToken);
        return Ok(response);
    }
}
```

`ExpeditionListArchiveModule.cs` keeps its explicit handler factory (the keyed-`"cups"` fallback logic is still needed) but with new namespaces and new options type:

```csharp
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;
// no more Anela.Heblo.Application.Features.ExpeditionList[.Services] usings

services.Configure<ExpeditionListArchiveOptions>(
    configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));
// (Module signature changes: must accept IConfiguration now)

services.AddTransient<IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>>(provider =>
{
    var blobStorage = provider.GetRequiredService<IBlobStorageService>();
    var cupsSink = provider.GetKeyedService<IPrintQueueSink>("cups")
        ?? provider.GetRequiredService<IPrintQueueSink>();
    var options = provider.GetRequiredService<IOptions<ExpeditionListArchiveOptions>>();
    return new ReprintExpeditionListHandler(blobStorage, cupsSink, options);
});
```

**Note:** `AddExpeditionListArchiveModule` currently takes no `IConfiguration` parameter. Its signature must change. Update the call in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` accordingly (verify which extension method wires it).

### Data Flow

**Print-fix request (post-refactor):**
```
Browser
  → POST /api/expedition-list/run-fix
  → ExpeditionListController.RunFix
  → IMediator.Send(RunExpeditionListPrintFixRequest)
  → RunExpeditionListPrintFixHandler              (ExpeditionList module)
  → existing print-fix side effects (unchanged)
```

**Reprint request (post-refactor):**
```
Browser
  → POST /api/expedition-list-archive/reprint
  → ExpeditionListArchiveController.Reprint
  → ReprintExpeditionListHandler
     ├── reads ExpeditionListArchiveOptions.BlobContainerName   (no ExpeditionList ref)
     ├── IBlobStorageService.DownloadAsync                       (Domain abstraction)
     └── IPrintQueueSink.SendAsync                               (Application.Shared.Printing)
                                ▲
                                │ resolved as keyed "cups" or non-keyed fallback
                                │ implementation is Cups / Azure / FileSystem / Combined
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Producer (`ExpeditionList:BlobContainerName`) and consumer (`ExpeditionListArchive:BlobContainerName`) drift in env config → Archive reads empty container, UI silently empty | **High** | Add a startup-time sanity check or integration test that asserts both options resolve to the same container in non-Development environments. Update deployment runbook to mirror values. Document in `docs/architecture/environments.md`. |
| `AddExpeditionListArchiveModule` signature change breaks call site | Low | Compile-time error catches this immediately. Single call site in `ServiceCollectionExtensions`. |
| Namespace change on `IPrintQueueSink` touches Cups + Azure adapter assemblies → unrelated PR scope creep | Low | Mechanical find/replace; covered by `dotnet build`. List all touched files up front (above). |
| Spec underspecifies — only `Reprint` handler called out, but `Download`/`GetExpeditionDates`/`GetExpeditionListsByDate` also bind `IOptions<PrintPickingListOptions>` | **High** (spec defect) | Spec amendment below. |
| Tests in `ExpeditionListArchive/*Tests.cs` build options via `IOptions<PrintPickingListOptions>` fixture; switching the options type forces test updates | Medium | All four test files use `private const string ContainerName = "expedition-lists";`. Update fixtures to construct `IOptions<ExpeditionListArchiveOptions>`. Mechanical. |
| `ModuleBoundariesTests` rule may flip RED if any unrelated archive code still references ExpeditionList types | Low | Run the test locally before pushing. Add to allowlist only with explicit comment if a residual coupling is intentionally out of scope (per existing precedent). |
| Frontend regenerated client breaks any direct caller of `expeditionListArchive_RunFix` | Low | Grep confirms only the hand-written hook references the URL; no generated-method call sites exist. |
| Stale staging/QA environments still hit `POST /api/expedition-list-archive/run-fix` and 404 | Low | Confirm no external consumers via `gh api`/server logs. Old URL is removed cleanly per NFR-3. Frontend and backend ship in the same Docker image, so they're versioned together. |

## Specification Amendments

1. **FR-3 must cover all four Archive handlers, not just `ReprintExpeditionListHandler`.** The current spec wording (paragraph 2 of FR-3) implies only the Reprint handler reads `PrintPickingListOptions`. In fact, `GetExpeditionDatesHandler.cs:16`, `GetExpeditionListsByDateHandler.cs:17`, `DownloadExpeditionListHandler.cs:16`, and `ReprintExpeditionListHandler.cs:19` all do. Update the FR-3 acceptance grep to:

   > `grep -RIn "using Anela.Heblo.Application.Features.ExpeditionList" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive` returns no results.

   …and explicitly enumerate the four handlers as touched files. The existing acceptance criterion (`grep -R "Application.Features.ExpeditionList\b" …`) already catches all four — but the spec narrative is misleading about scope.

2. **Open Question 1 (contract namespace) — resolved:** Place `IPrintQueueSink` in `Anela.Heblo.Application.Shared.Printing`. Rationale in Decision 1.

3. **Open Question 2 (config migration) — resolved:** New `ExpeditionListArchive` config section. Rationale and risk in Decision 2 and the risk table.

4. **Open Question 3 (`ExpeditionListController` scope) — resolved:** Single `RunFix` action only. Rationale in Decision 3.

5. **Add to scope:** New `ModuleBoundariesTests` rule for `ExpeditionListArchive → ExpeditionList`. Without this fence, the refactor decays.

6. **Add to scope:** `AddExpeditionListArchiveModule` signature change to accept `IConfiguration` (currently parameterless). Single call site update.

7. **Out of Scope reaffirmed:** Splitting `PrintPickingListOptions` further (the producer side keeps it intact for `EmailSender`, `PrintQueueFolder`, `PrintSink`, `BlobConnectionString`, the state-id fields, and `BlobContainerName`).

## Prerequisites

1. **Configuration delta (must ship in the same PR or precede it):** Add `"ExpeditionListArchive": { "BlobContainerName": "<same value as ExpeditionList:BlobContainerName>" }` to:
   - `backend/src/Anela.Heblo.API/appsettings.json` (`"expedition-lists"`)
   - `backend/src/Anela.Heblo.API/appsettings.Development.json` (`"expedition-lists-stg"`)
   - `backend/src/Anela.Heblo.API/appsettings.Test.json` (`"expedition-lists-stg"`)
   - `backend/src/Anela.Heblo.API/appsettings.Staging.json` (`"expedition-lists-stg"`)
   - Azure App Service application settings for the production slot (manual)

   The default in `ExpeditionListArchiveOptions` (`"expedition-lists"`) covers production if the operator forgets — but staging/dev would silently bind to the wrong container without explicit config. Treat this as a deployment gate.

2. **No new NuGet packages, no DB migrations, no infrastructure changes.**

3. **Verify before starting:**
   - `grep -RIn "expeditionListArchive_RunFix\|/api/expedition-list-archive/run-fix" frontend/src` shows only the two known references (generated client + hook).
   - No external API consumers exist for `POST /api/expedition-list-archive/run-fix` (Azure access logs / server logs).
   - Run `dotnet test backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` once before the refactor begins; expect green. After implementation, expect green again with the new rule.
```