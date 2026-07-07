# Architecture Review: `GenerateLeafletHandler` error-signaling consistency fix

## Skip Design: true

This is a pure backend/contract consistency fix confined to the Leaflet module (Application + API layers), one MCP tool, and one existing frontend component's error-branching logic. No new screens, layouts, or visual components are introduced — the only frontend change (FR-7) swaps which condition drives an *already-existing* amber/red banner. No design review is needed.

## Architectural Fit Assessment

The spec's proposal is a straight application of a pattern that is already the dominant idiom in this codebase, not a new one being introduced. I verified directly:

- `BaseResponse` (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`) already supports the exact three-constructor shape the spec assumes: parameterless (success), `(ErrorCodes, Dictionary<string,string>?)` (expected business error), and `(Exception)` (unexpected error, reserved — correctly flagged by the spec's NFR-2 as *not* to be used here).
- `BaseApiController.HandleResponse<T>` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`) already does exactly what the spec says: reflects on the `[HttpStatusCode]` attribute of the response's `ErrorCode` and dispatches to the matching `ActionResult`. Every other action in `LeafletController.cs` (`GetChunkDetail`, `SubmitFeedback`, `GetGeneration`, `GetDocuments`, `UploadDocument`, `DeleteDocument`, `GetFeedbackList`) already calls `return HandleResponse(result);` with zero try/catch. `Generate` is the sole outlier.
- `ErrorCodes.cs` already has a `// Leaflet module errors (25XX)` block ending at `LeafletFeedbackAlreadySubmitted = 2503`, and already has precedent for `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]` on other "not enough data" conditions in other modules (`ArticleNotGenerated = 2406`, `ManufacturedInventoryInsufficientStock = 1216`, `TransportBoxStateChangeError = 1402`). Adding `LeafletEmptyRetrieval = 2504` with the same attribute is squarely in-pattern, not a new convention.
- `SubmitLeafletFeedbackResponse` (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackRequest.cs`) is the two-constructor template the spec asks `GenerateLeafletResponse` to copy — confirmed identical in shape to what FR-2 proposes.
- The global exception pipeline exists and is real, not hypothetical: `ServiceCollectionExtensions.AddCrossCuttingServices` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`) registers `UnauthorizedAccessExceptionHandler`, `ValidationExceptionHandler`, `ArgumentExceptionHandler` (all in `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/`) plus `AddProblemDetails()`. None of these three handlers match a generic embedding/chat-client failure, so an unexpected exception from `GenerateLeafletHandler` genuinely will fall through to ASP.NET's default `ProblemDetails` 500 response once the controller's catch-all is removed. This is the correct, already-existing "sink" — no new infrastructure is needed to answer the spec's now-settled Open Question.
- I confirmed the frontend regression the spec identifies in FR-7 is real, not speculative, by reading the generated client's `throwException` (`frontend/src/api/generated/api-client.ts:41260`): `if (result !== null && result !== undefined) throw result;` — it throws the parsed body object directly. `GenerateLeafletResponse.fromJS(...)` (line 24070) produces a real class instance, so `err instanceof GenerateLeafletResponse` in the spec's proposed fix works as written. Today's `processLeaflet_Generate` parses the 422 branch as `ProblemDetails.fromJS(...)`; after regeneration it will parse as `GenerateLeafletResponse.fromJS(...)`, which has no `status` field — confirming `isApiError`'s current duck-typing check would silently break.

**Conclusion: the spec is architecturally sound and requires no material redesign.** My job here is to pin down the few implementation-order and safety details the spec leaves implicit, and to formally close the one item it left open.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────┐        ┌──────────────────────────────┐
│ LeafletGenerateTab.tsx  │        │  api-client.ts (generated)    │
│  - reads err.errorCode  │◄───────┤  processLeaflet_Generate:      │
│    instead of err.status│        │   200 -> GenerateLeafletResponse│
└─────────────────────────┘        │   422 -> throw GenerateLeafletResponse (was ProblemDetails)
              ▲                    │   400 -> throw ProblemDetails (unchanged, model validation)
              │ HTTP 422/200       │   (502 branch removed; unmapped -> generic throwException)
              │                    └──────────────────────────────┘
┌─────────────────────────────────────────────────────────────────┐
│ LeafletController.Generate  (no try/catch — matches every other  │
│  action in this controller)                                      │
│   var result = await _mediator.Send(request, ct);                │
│   return HandleResponse(result);   // BaseApiController           │
└─────────────────────────────────────────────────────────────────┘
              │ MediatR
              ▼
┌─────────────────────────────────────────────────────────────────┐
│ GenerateLeafletHandler.Handle                                     │
│   kbHits/leafletHits both empty                                  │
│     -> return new GenerateLeafletResponse(                       │
│           ErrorCodes.LeafletEmptyRetrieval, { detail: "..." })    │
│   otherwise -> unchanged two-stage generation, return success DTO │
│   unexpected exception (embedding/chat client failure)            │
│     -> propagates unhandled -> ASP.NET AddExceptionHandler chain  │
│        (Unauthorized/Validation/Argument handlers, none match)    │
│     -> falls through to AddProblemDetails() default -> 500        │
└─────────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────────┐
│ LeafletTools.GenerateLeaflet (MCP)                                │
│   var response = await _mediator.Send(...);                      │
│   if (!response.Success) throw new McpException(...);            │
│   catch (Exception) remains, for genuinely unexpected failures    │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the empty-retrieval condition is signaled
**Options considered:**
(a) Keep throwing `EmptyRetrievalException`, move the catch to middleware/`IExceptionHandler`.
(b) Return a `BaseResponse`-derived error DTO from the handler (the spec's choice), mapped by the existing `HandleResponse` + `[HttpStatusCode]` reflection mechanism.

**Chosen approach:** (b), exactly as specified.

**Rationale:** Option (a) would introduce a *second* error-signaling idiom into a codebase that has exactly one (`BaseResponse` + `HandleResponse`) everywhere else, including the other four Leaflet handlers. `EmptyRetrievalException` is not a genuinely exceptional/unexpected condition — "the KB doesn't cover this topic yet" is an anticipated business outcome of a search operation, structurally identical to `LeafletFeedbackNotFound` or `ArticleNotGenerated`, both of which are already response error codes, not exceptions. Domain exceptions should be reserved for truly unexpected failures (the embedding/chat client throwing), which is exactly what's left to fall through to the global pipeline. This keeps a single, consistent rule: **expected business outcomes are response values; only truly unexpected failures are exceptions.**

#### Decision 2: Disposition of the "safety net" catch-all (the spec's now-resolved Open Question)
**Options considered:**
(a) Remove the controller's catch-all entirely; unexpected exceptions fall through to ASP.NET's global `ProblemDetails` pipeline, resulting in a generic 500.
(b) Add a new `IExceptionHandler` (e.g. `ExternalServiceExceptionHandler`) alongside `UnauthorizedAccessExceptionHandler`/`ValidationExceptionHandler`/`ArgumentExceptionHandler`, mapping embedding/chat-client failures to 502 with a friendly message, preserving the current external contract for infra failures.

**Chosen approach:** (a) — confirmed by the pipeline owner, matches the brief's explicit suggested fix ("remove the try/catch... rely on HandleResponse"). Treat as settled; do not re-litigate.

**Rationale:** This aligns with the brief's own stated rationale ("silently catches all other exceptions as 502, which swallows potential bugs") and with the pattern already established for `UnauthorizedAccessException`/`ValidationException`/`ArgumentException` — infra-layer exception mapping lives in `Infrastructure/ExceptionHandling/*`, not in individual controllers. Scope is important: this decision applies *only* to `LeafletController.Generate`. It does not retroactively justify adding a generic external-service-failure `IExceptionHandler` as part of this fix — that remains explicitly out of scope per the spec, and should only be built later if a genuine cross-module need materializes (YAGNI — don't build the 502 middleware speculatively for one call site).

#### Decision 3: Preserve HTTP 422, change body shape (accept the breaking body-shape change)
**Options considered:**
(a) Preserve both the status code and the exact `ProblemDetails { status, title, detail }` body shape (e.g., by having `HandleResponse` special-case this one error code to emit `ProblemDetails` instead of the DTO).
(b) Preserve the status code (422) via `[HttpStatusCode]`, but let the body become the standard `GenerateLeafletResponse { success: false, errorCode, params }` shape, consistent with every other error in the module.

**Chosen approach:** (b), as specified.

**Rationale:** Option (a) would require a special case inside `HandleResponse` (used by every controller in the codebase, not just Leaflet) purely to preserve one endpoint's legacy body shape — that is exactly the kind of bespoke, non-uniform behavior this fix is meant to eliminate. There is exactly one internal consumer of the old shape (`LeafletGenerateTab.tsx`), and it is being updated in the same change (FR-7). No external/third-party consumers are known (this is an internal SPA + MCP tool, not a published public API with external versioning guarantees). Given a single known internal consumer already being fixed, breaking the body shape while holding the status code fixed is the right trade — it buys the same one-idiom consistency for the whole module.

## Implementation Guidance

### Directory / Module Structure

No new files or directories beyond one deletion. All work is in existing files:

```
backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs                                      (add member)
backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/
    GenerateLeafletResponse.cs                                                                (add 2 ctors)
    GenerateLeafletHandler.cs                                                                  (throw -> return)
    EmptyRetrievalException.cs                                                                 (delete)
backend/src/Anela.Heblo.API/Controllers/LeafletController.cs                                   (Generate: drop try/catch)
backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs                                           (GenerateLeaflet: inspect response)
frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx                                  (errorCode check, drop isApiError if unused elsewhere)
frontend/src/api/generated/api-client.ts                                                        (auto-regenerated on build — do not hand-edit)
```

Test files to update in place (no new test files needed — this is a behavior-preserving-at-the-HTTP-status-level refactor):
```
backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs
backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs
```

### Interfaces and Contracts

- `ErrorCodes.LeafletEmptyRetrieval = 2504`, `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]`, placed immediately after `LeafletFeedbackAlreadySubmitted = 2503` in the existing `// Leaflet module errors (25XX)` block — do not renumber or reorder existing members (their numeric values are the wire contract for the TS enum).
- `GenerateLeafletResponse` gains the two-constructor shape already used by `SubmitLeafletFeedbackResponse`. No other property changes.
- `LeafletController.Generate` signature changes from `Task<IActionResult>` to `Task<ActionResult<GenerateLeafletResponse>>` — required for `HandleResponse<T>`'s generic constraint (`where T : BaseResponse`) and consistent with every sibling action.
- `[ProducesResponseType]` attributes on `Generate`: keep `200 -> GenerateLeafletResponse`, keep `400 -> ProblemDetails` (model validation, untouched), change `422` from `ProblemDetails` to `GenerateLeafletResponse`, remove `502` entirely (no code path produces it anymore).
- `LeafletTools.GenerateLeaflet` keeps its outer `catch (McpException)` rethrow and generic `catch (Exception)` block (legitimate MCP boundary translation for real infra failures) but replaces the `catch (EmptyRetrievalException)` block with a post-`Send` `if (!response.Success)` check.

### Data Flow

**Empty-retrieval path (REST):**
1. Client `POST /api/leaflet/generate` with a topic that has zero KB/leaflet hits.
2. `GenerateLeafletHandler.Handle` returns early with `GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, { detail: "..." })`, `Success = false`.
3. `LeafletController.Generate` calls `HandleResponse(result)` → reflects `[HttpStatusCode]` on `LeafletEmptyRetrieval` → `422` → `StatusCode(422, response)`.
4. Generated TS client (`processLeaflet_Generate`) parses body as `GenerateLeafletResponse`, throws it via `throwException`.
5. `LeafletGenerateTab.tsx` catches, checks `err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval`, shows amber banner.

**Empty-retrieval path (MCP):**
1. `LeafletTools.GenerateLeaflet` sends the same request, gets the same `Success = false` response.
2. Inspects `response.Success`, throws `McpException` with the user-facing message — externally unchanged from today.

**Unexpected failure path (e.g., embedding client throws) (REST):**
1. Exception propagates out of `GenerateLeafletHandler.Handle`, uncaught by `LeafletController.Generate` (no try/catch left).
2. ASP.NET's registered `IExceptionHandler`s run in order (`UnauthorizedAccessExceptionHandler`, `ValidationExceptionHandler`, `ArgumentExceptionHandler`) — none match a generic exception.
3. Falls through to `AddProblemDetails()` default → generic `ProblemDetails` with status `500`, no Leaflet-specific message, no stack trace exposed (existing, already-correct behavior — not new to this fix).

**Unexpected failure path (MCP):** unchanged — `LeafletTools`'s generic `catch (Exception)` still converts to `McpException("Leaflet generation failed. Please try again.")`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Monitoring/alerting dashboards keyed on HTTP 502 for "Leaflet generation transient failure" silently stop firing (502→500 change is real and was flagged in the spec) | Medium | Since the Open Question is now settled as full removal, explicitly check (grep) for any Azure/App Insights alert rule, dashboard, or synthetic monitor referencing `leaflet` + `502` before merging; if found, update it to watch for 500s on `POST /api/leaflet/generate` instead. This is an ops-config check, not a code change, and should not block the PR but should be a checklist item. |
| Frontend 422 body-shape change breaks silently if `LeafletGenerateTab.tsx` (FR-7) is forgotten during implementation | High if missed | FR-7 is not optional — it must land in the **same PR** as FR-4 (controller change), not as a follow-up. Add an explicit test asserting `isApiError`-based branching is gone and `errorCode`-based branching is present (e.g., a component test mocking a thrown `GenerateLeafletResponse`). |
| Deleting `EmptyRetrievalException.cs` before all three consumers (`GenerateLeafletHandler`, `LeafletController`, `LeafletTools`) are updated causes a compile break | Low (compiler catches it) | Order the change as: FR-1 (ErrorCodes) → FR-2 (response ctor) → FR-3 (handler) → FR-4 (controller) → FR-5 (MCP tool) → FR-6 (delete exception file) → FR-7 (frontend) → FR-8 (tests). Do FR-6 last, after `dotnet build` has zero references. |
| `GenerateLeafletResponse`'s `Params["detail"]` string is in English while the frontend's own fallback copy is Czech; a future consumer might mistakenly display the English `Params["detail"]` to end users | Low | Not this fix's concern per FR-7 (the frontend already ignores the server string and uses its own Czech copy), but worth a one-line code comment on `Params["detail"]` in the handler noting it is for API-consumer/log diagnostics, not for direct end-user display, to prevent future misuse. |
| `HandleResponse`'s `Forbid()` branch for `ErrorCodes.Forbidden` (used by `SubmitLeafletFeedbackHandler`) returns no body — irrelevant here since `LeafletEmptyRetrieval` maps to `UnprocessableEntity`, which falls into the `_ => StatusCode(...)` default arm and does carry the response body | None (verified, no action needed) | Already confirmed by reading `BaseApiController.HandleResponse`: `UnprocessableEntity` isn't one of the explicitly special-cased switch arms (`BadRequest`, `NotFound`, `Unauthorized`, `Forbidden`, `ServiceUnavailable`, `InternalServerError`), so it falls to `_ => StatusCode((int)statusCode, response)`, which does include the body. No gap. |

## Specification Amendments

The spec is implementation-ready as written. Two small clarifications to make explicit during implementation (not scope changes):

1. **Open Question is closed.** Per the pipeline owner: full removal of the try/catch (spec's option (a) throughout FR-4/FR-8) is confirmed. Do not implement a scoped safety-net `IExceptionHandler` as part of this fix; that remains explicitly out of scope (as the spec's own "Out of Scope" section already states as a *possible* follow-up). `Generate_returns_502_on_unexpected_exception` in `LeafletControllerTests.cs` should be **replaced** (not deleted with no equivalent) with a test asserting the exception now propagates unhandled, mirroring the adjacent `Generate_propagates_OperationCanceledException` test, per FR-8's own guidance.
2. **Implementation order matters for a clean compile.** The spec lists FR-1 through FR-8 in dependency order already; follow that order literally (ErrorCodes → response ctor → handler → controller → MCP tool → delete exception → frontend → tests) rather than parallelizing, since FR-3/FR-4/FR-5 all depend on FR-1/FR-2 existing, and FR-6 depends on FR-3/FR-4/FR-5 all being done first.

No functional, contract, or scope changes to the spec are needed.

## Prerequisites

- None. No migrations, no new config, no new infrastructure. All target types (`BaseResponse`, `HandleResponse`, `ErrorCodes`, `IExceptionHandler` registrations) already exist and are exercised by other Leaflet handlers today.
- A local `dotnet build` after the backend changes will auto-regenerate `frontend/src/api/generated/api-client.ts` per this project's existing OpenAPI-client-generation convention (`docs/development/api-client-generation.md`) — the frontend change (FR-7) must be written against the *regenerated* client's shape, not guessed at, since generated TS class field names/nullability can shift slightly. Regenerate first, then write FR-7.
