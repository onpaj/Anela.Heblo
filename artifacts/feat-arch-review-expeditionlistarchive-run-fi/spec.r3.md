# Specification: Remove ExpeditionListArchive → ExpeditionList cross-module dependency

## Summary
The `ExpeditionListArchive` module currently reaches directly into the sibling `ExpeditionList` module — both at the controller layer (`POST /api/expedition-list-archive/run-fix` dispatches a use case owned by `ExpeditionList`) and at the application layer (the Archive module wires its `ReprintExpeditionList` handler with `IPrintQueueSink` and `PrintPickingListOptions` types declared inside `ExpeditionList`, not in any `Contracts/` folder). This violates the project's module-boundary guidelines. The fix relocates the misrouted endpoint to its rightful owner and promotes the shared print/blob abstractions into contract types that both modules can consume without coupling.

## Background
A daily architecture review on 2026-05-26 surfaced two cross-module pulls in `ExpeditionListArchive`:

1. **Controller-level pull.** `ExpeditionListArchiveController.cs:1,57–62` imports `RunExpeditionListPrintFixRequest`/`RunExpeditionListPrintFixResponse` from `Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix` and exposes them under `/api/expedition-list-archive/run-fix`. The URL implies an Archive-owned operation, but the action triggers a live print-fix workflow defined entirely inside `ExpeditionList`. Compile-time, the API project is forced to know about another module's internal use case.

2. **Module wiring pull.** `ExpeditionListArchiveModule.cs:1–2` imports `Anela.Heblo.Application.Features.ExpeditionList` (for `PrintPickingListOptions`) and `Anela.Heblo.Application.Features.ExpeditionList.Services` (for `IPrintQueueSink`). Neither type lives in a `Contracts/` namespace, so the Archive module is binding to internals of another feature module. `ReprintExpeditionListHandler.cs:1–5` has the same imports.

Per `docs/architecture/development_guidelines.md`, cross-module references must go through contract interfaces owned by the consumer (the documented exemplar is `ILeafletKnowledgeSource` consumed by the producer-side `KnowledgeBaseLeafletSourceAdapter`). The Archive module currently violates that rule, which means it cannot be understood, tested, or evolved without first reading `ExpeditionList` internals. Tests, refactors, and dependency-graph hygiene all suffer.

This spec covers the corrective refactor.

## Functional Requirements

### FR-1: Relocate the `run-fix` endpoint to an `ExpeditionList` controller
The `[HttpPost("run-fix")]` action currently in `ExpeditionListArchiveController` must be moved to a new `ExpeditionListController` (no such controller exists yet) under the route `/api/expedition-list/run-fix`. The action signature, request/response types, MediatR dispatch, and `[Authorize]` requirement are preserved verbatim — the only changes are file location, controller class, and URL prefix.

After the move:
- `ExpeditionListArchiveController.cs` no longer references `Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix` (its only direct reference into `ExpeditionList`).
- The new `ExpeditionListController` lives at `backend/src/Anela.Heblo.API/Controllers/ExpeditionListController.cs`, derives from `BaseApiController`, is decorated with `[Authorize]`, `[ApiController]`, `[Route("api/expedition-list")]`, and is constructor-injected with `IMediator`.
- The OpenAPI document advertises the endpoint at `POST /api/expedition-list/run-fix`. The TypeScript client regenerates with method `expeditionList_RunFix` instead of `expeditionListArchive_RunFix`.

**Acceptance criteria:**
- `dotnet build` succeeds across the solution.
- `ExpeditionListArchiveController.cs` contains no `using Anela.Heblo.Application.Features.ExpeditionList.UseCases.*` directive.
- A new file `Anela.Heblo.API/Controllers/ExpeditionListController.cs` exists with a single `RunFix` action exposed at `POST /api/expedition-list/run-fix`.
- `POST /api/expedition-list-archive/run-fix` returns 404; `POST /api/expedition-list/run-fix` returns 200 and the same `RunExpeditionListPrintFixResponse` payload it previously did.
- The regenerated `frontend/src/api/generated/api-client.ts` exposes `expeditionList_RunFix` and no longer exposes `expeditionListArchive_RunFix`.

### FR-2: Update the frontend hook and call sites to the new URL
The hand-written hook in `frontend/src/api/hooks/useExpeditionListArchive.ts:134` issues a `POST` against the absolute URL `${apiClient.baseUrl}/api/expedition-list-archive/run-fix`. After the backend move it must call `${apiClient.baseUrl}/api/expedition-list/run-fix`. UI placement of the trigger button on `ExpeditionListArchivePage` stays unchanged — only the URL the hook hits and any references to the regenerated method name change.

**Acceptance criteria:**
- `useExpeditionListArchive.ts` references the new URL `/api/expedition-list/run-fix`.
- `ExpeditionListArchivePage.tsx` continues to render and wire the existing "Run fix" button via the same hook; user-visible behavior is identical.
- `npm run build` and `npm run lint` succeed.
- `ExpeditionListArchivePage.test.tsx` passes (any mocked URL assertions updated to the new path).
- No remaining occurrence of the string `expedition-list-archive/run-fix` anywhere in `frontend/src/`.

### FR-3: Eliminate the Archive module's compile-time dependency on `ExpeditionList` internals
`ExpeditionListArchiveModule.cs:1–2` and `ReprintExpeditionListHandler.cs:1–5` import `Anela.Heblo.Application.Features.ExpeditionList` (for `PrintPickingListOptions`) and `Anela.Heblo.Application.Features.ExpeditionList.Services` (for `IPrintQueueSink`). These imports must be removed.

Approach (working assumption — see Open Questions for the alternatives considered):

1. **Promote `IPrintQueueSink` to a shared contract.** Move the interface from `Anela.Heblo.Application.Features.ExpeditionList.Services.IPrintQueueSink` to a contracts namespace that is intended for cross-module consumption, e.g. `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` (or an equivalent `Contracts/` folder under either module). All existing implementations (`FileSystemPrintQueueSink`, `CombinedPrintQueueSink`, the Cups/Azure adapter sinks) are updated to implement the relocated interface. DI registrations follow the namespace change.

2. **Introduce an Archive-owned options class for the blob container name.** The Archive's `ReprintExpeditionListHandler` reads only one field from `PrintPickingListOptions` — `BlobContainerName`. Create a new `ExpeditionListArchiveOptions` (in the Archive module) with `BlobContainerName`, bound from configuration. The handler is rewired to inject `IOptions<ExpeditionListArchiveOptions>` instead of `IOptions<PrintPickingListOptions>`. The Archive module's `ExpeditionListArchiveModule` registers and binds the new options class; configuration is sourced from the same config section the Archive needs (initial assumption: a new `ExpeditionListArchive` section in `appsettings*.json`, populated to the same value `PrintPickingListOptions.BlobContainerName` carries today). `PrintPickingListOptions` is **not** removed — `ExpeditionList` still uses the other fields.

After this work:
- `ExpeditionListArchiveModule.cs` contains no `using Anela.Heblo.Application.Features.ExpeditionList...` directives.
- `ReprintExpeditionListHandler.cs` contains no `using Anela.Heblo.Application.Features.ExpeditionList...` directives.
- The Archive module compiles without referencing `ExpeditionList` types in any code path.
- `ExpeditionList` continues to compile and behave identically.

**Acceptance criteria:**
- `grep -R "Application.Features.ExpeditionList\b" backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive` returns no results.
- The relocated `IPrintQueueSink` interface signature is unchanged (`Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)`).
- All keyed DI registrations of `IPrintQueueSink` (e.g. the `"cups"` keyed registration referenced in `ExpeditionListArchiveModule.cs:22`) still resolve correctly.
- `Reprint` end-to-end behavior is unchanged: a valid blob path downloads from the configured container, is sent to the keyed `cups` sink when available, falls back to the non-keyed sink otherwise, and cleans up the temp file in all paths.
- Unit/integration tests for `ReprintExpeditionListHandler` pass against the new options type.
- `dotnet build` + `dotnet format` + `dotnet test` succeed.

### FR-4: Preserve runtime behavior (no functional regressions)
This is a pure refactor. No business logic changes. No new endpoints. No removed capability. No changes to authentication, authorization, payloads, status codes, or side effects.

**Acceptance criteria:**
- The print-fix workflow produces the same observable side effects (state transitions, prints, emails) as before the refactor.
- The reprint workflow downloads the same blob and prints to the same sink as before.
- Configuration values consumed at runtime (blob container name, keyed sink resolution, etc.) match pre-refactor values.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change in latency or throughput is expected. Both endpoints continue to dispatch the same MediatR handler on the same dependency graph.

### NFR-2: Security
- `[Authorize]` attribute is preserved on the new `ExpeditionListController` and on the existing actions in `ExpeditionListArchiveController`.
- No new secrets, connection strings, or credentials are introduced. The `BlobConnectionString`/`BlobContainerName` values continue to be sourced from configuration, never hardcoded.
- The renamed/relocated `IPrintQueueSink` interface preserves the same authorization-irrelevant signature; no new attack surface.

### NFR-3: Backwards compatibility
The legacy URL `POST /api/expedition-list-archive/run-fix` is **removed**, not aliased. The only consumer is the in-repo frontend (verified by grep), which is updated as part of FR-2. No external consumers exist.

### NFR-4: Testability
After the refactor it must be possible to compile and run `ExpeditionListArchive` tests without referencing the `ExpeditionList` module. Any existing tests that import `ExpeditionList` types solely to set up Archive fixtures should be updated to use the new contract/options types.

## Data Model
No database schema changes. No new entities.

Configuration delta:
- **New options class** `ExpeditionListArchiveOptions` (Archive module), bound from a new `ExpeditionListArchive` configuration section. Initial fields: `string BlobContainerName` (default `"expedition-lists"` — matches current `PrintPickingListOptions.BlobContainerName` default).
- **Unchanged**: `PrintPickingListOptions` keeps all its existing fields (`EmailSender`, `PrintQueueFolder`, `DefaultEmailRecipients`, `SourceStateId`, `FixSourceStateId`, `DesiredStateId`, `SendToPrinterByDefault`, `ChangeOrderStateByDefault`, `PrintSink`, `BlobConnectionString`, `BlobContainerName`). The `BlobContainerName` field remains because `ExpeditionList` itself still uses it (verify with grep during implementation; if it doesn't, that's an unrelated cleanup not in scope).
- Deployment must add the new `ExpeditionListArchive:BlobContainerName` setting to every environment's configuration source before the refactor ships, or the handler must fall back to the same default (`"expedition-lists"`).

## API / Interface Design

### Backend HTTP API
| Before | After |
|---|---|
| `POST /api/expedition-list-archive/run-fix` | `POST /api/expedition-list/run-fix` |

Request body: none. Response body: unchanged (`RunExpeditionListPrintFixResponse`). Auth: unchanged (`[Authorize]`). All other endpoints on `ExpeditionListArchiveController` remain in place at their existing routes.

### Internal contract surface
| Before | After |
|---|---|
| `Anela.Heblo.Application.Features.ExpeditionList.Services.IPrintQueueSink` | `Anela.Heblo.Application.Shared.Printing.IPrintQueueSink` (or equivalent contract namespace — to be finalized during implementation; signature unchanged) |
| `Anela.Heblo.Application.Features.ExpeditionList.PrintPickingListOptions` (Archive reads `BlobContainerName`) | `Anela.Heblo.Application.Features.ExpeditionListArchive.ExpeditionListArchiveOptions` (Archive-owned, contains `BlobContainerName`) |

### Frontend
- `frontend/src/api/hooks/useExpeditionListArchive.ts`: change one hardcoded URL string.
- `frontend/src/api/generated/api-client.ts`: regenerated from OpenAPI; method renames from `expeditionListArchive_RunFix` to `expeditionList_RunFix`. Any direct callers of the generated method (none found in current code — only the hand-written hook calls the URL) update accordingly.
- `frontend/src/pages/ExpeditionListArchivePage.tsx`: no functional changes; button placement and `handleRunFix` wiring preserved.

## Dependencies
- **Internal modules**: `ExpeditionList` (still owns the use case being dispatched), `ExpeditionListArchive` (loses its illegal cross-module imports), `Anela.Heblo.API` (gains a new controller).
- **Configuration**: `appsettings.Development.json`, `appsettings.Production.json`, staging config, and Azure App Service application settings must include `ExpeditionListArchive:BlobContainerName` after the refactor — or rely on the default.
- **Code generation**: The OpenAPI → TypeScript client regenerates on backend build; the regenerated file is committed.
- **No new third-party libraries or NuGet packages required.**

## Out of Scope
- Restructuring `PrintPickingListOptions` further (e.g. splitting it into multiple smaller options). Only the Archive side gets its own options object.
- Moving the "Run fix" UI off the Archive page. The button stays on `ExpeditionListArchivePage` even though it now calls `/api/expedition-list/run-fix` — UI relocation is a product decision and not part of this architectural fix.
- Adding deprecation aliasing for the old URL. No external consumers exist; the old route is removed cleanly.
- Refactoring any other module that may have cross-module pulls. Scope is limited to `ExpeditionListArchive` ↔ `ExpeditionList`.
- Renaming or restructuring `ExpeditionList` itself, its `Services/` folder, or the `CombinedPrintQueueSink` adapter wiring.
- Changes to the `Reprint`, `Download`, `GetDates`, or `GetByDate` actions on `ExpeditionListArchiveController` (their routes and behavior are untouched).

## Open Questions

1. **Contract namespace location.** The two candidate homes for the relocated `IPrintQueueSink` interface are (a) a new `Anela.Heblo.Application.Shared.Printing` namespace, (b) a `Contracts/` subfolder under whichever module owns it conceptually (`ExpeditionList` if it is the producer, `ExpeditionListArchive` if treated as a consumer-defined contract per the `ILeafletKnowledgeSource` pattern). The `ILeafletKnowledgeSource` exemplar suggests the **consumer** defines the contract, which would mean `ExpeditionListArchive` owns `IPrintQueueSink`. But there are multiple consumers (both `ExpeditionList` and `ExpeditionListArchive` print), so a shared location may be more honest. Architect to decide before implementation begins.

2. **Configuration migration.** Should `ExpeditionListArchiveOptions.BlobContainerName` be sourced from a new config section, or should the Archive module bind from the existing `ExpeditionList:BlobContainerName` value to avoid env-config churn? Recommended default: new section (`ExpeditionListArchive:BlobContainerName`) for cleanliness, with deployment instructions to copy the value over. The default `"expedition-lists"` covers environments where the section is missing.

3. **`ExpeditionListController` scope.** This spec creates the controller solely to host `RunFix`. Should other `ExpeditionList` operations (currently in `Anela.Heblo.API/Features/ExpeditionList/`) also be migrated into the new controller as part of this change, or is that scoped out? Default assumption: scoped out — only `RunFix` moves. Architect can override if there's an obvious adjacent cleanup.

## Status: HAS_QUESTIONS
