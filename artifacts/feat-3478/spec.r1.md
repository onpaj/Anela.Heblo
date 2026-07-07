# Specification: `GenerateLeafletHandler` error-signaling consistency fix

## Summary
`GenerateLeafletHandler` (Leaflet module) currently throws a domain exception (`EmptyRetrievalException`) when the knowledge base and leaflet style corpus both return zero relevant hits, forcing `LeafletController.Generate` to catch that exception and a generic `Exception` to decide HTTP status codes — a business-logic decision that belongs in the handler, and a pattern inconsistent with every other handler in the module. This fix converts the empty-retrieval case into a normal error-code response (`ErrorCodes.LeafletEmptyRetrieval`) returned by the handler, lets the existing `HandleResponse` helper produce the HTTP result (as every sibling Leaflet action already does), removes the controller's try/catch and its dependency on an Application-layer exception type, and deletes the now-dead `EmptyRetrievalException` type and its remaining consumer-side workarounds.

## Background
An architecture-review finding (filed 2026-07-04) identified that `GenerateLeafletHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`, lines 63–67) throws `EmptyRetrievalException` when `kbHits.Count == 0 && leafletHits.Count == 0`, and that `LeafletController.Generate` (`backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`, lines 39–66) wraps the `_mediator.Send` call in a try/catch that maps `EmptyRetrievalException` → HTTP 422 and any other exception → HTTP 502, with a friendly message.

This is inconsistent with every other handler in the same module:
- `SubmitLeafletFeedbackHandler` returns `new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden, ...)` / `ErrorCodes.LeafletFeedbackNotFound` / `ErrorCodes.LeafletFeedbackAlreadySubmitted`.
- `GetLeafletGenerationHandler` returns `new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)`.
- `GetLeafletChunkDetailHandler` returns `new GetLeafletChunkDetailResponse(ErrorCodes.LeafletChunkNotFound)`.

All three response types derive from `BaseResponse` (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`), which carries `Success`, `ErrorCode` (an `ErrorCodes` enum value), and an optional `Params` dictionary. `BaseApiController.HandleResponse<T>` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`) inspects `response.Success`/`response.ErrorCode`, looks up the `[HttpStatusCode(...)]` attribute on the matching `ErrorCodes` enum member via reflection, and returns the corresponding `ActionResult`. Every other action in `LeafletController` already calls `return HandleResponse(result);` with no try/catch. `docs/architecture/development_guidelines.md` lists "Business logic in Controller class" as a forbidden practice — the controller currently owns the exception→status-code mapping for this one case, which is exactly that.

Per `docs/architecture/development_guidelines.md`, this fix does not change module boundaries or persistence — it is a pure consistency/refactor fix confined to the Leaflet module (Application + API layers) plus its two callers.

## Functional Requirements

### FR-1: Add a dedicated `ErrorCodes` member for the empty-retrieval condition
Add a new member to the Leaflet section (25XX range) of `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, immediately after the existing `LeafletFeedbackAlreadySubmitted = 2503`:

```csharp
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
LeafletEmptyRetrieval = 2504,
```

Tagging it `UnprocessableEntity` preserves the current external HTTP contract (422) for this specific condition — no client-visible status-code regression.

**Acceptance criteria:**
- `ErrorCodes.LeafletEmptyRetrieval = 2504` exists in the `// Leaflet module errors (25XX)` block.
- It is decorated with `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]`.
- `frontend/src/api/generated/api-client.ts`'s generated `ErrorCodes` TS enum includes `LeafletEmptyRetrieval` after the next OpenAPI client regeneration (automatic on build per project conventions — no manual edit).

### FR-2: Give `GenerateLeafletResponse` an error-constructing constructor
`GenerateLeafletResponse` (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`) currently has only the implicit parameterless constructor. Add the same two-constructor shape used by `SubmitLeafletFeedbackResponse`:

```csharp
public GenerateLeafletResponse() { }

public GenerateLeafletResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
    : base(errorCode, details) { }
```

**Acceptance criteria:**
- `new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...)` compiles and sets `Success = false`, `ErrorCode = ErrorCodes.LeafletEmptyRetrieval`, `Params` as passed, per `BaseResponse`'s existing constructor behavior.
- The success-path usage (`new GenerateLeafletResponse { Content = ..., KbSourceCount = ..., LeafletSourceCount = ... }`, lines 134–139 of the handler) is unaffected — `Success` defaults to `true` via `BaseResponse`'s parameterless constructor.

### FR-3: `GenerateLeafletHandler` returns an error response instead of throwing
Replace lines 63–67 of `GenerateLeafletHandler.Handle`:

```csharp
if (kbHits.Count == 0 && leafletHits.Count == 0)
{
    throw new EmptyRetrievalException(
        "Knowledge Base does not yet cover this topic; try a broader phrasing");
}
```

with an early return:

```csharp
if (kbHits.Count == 0 && leafletHits.Count == 0)
{
    return new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval,
        new() { { "detail", "Knowledge Base does not yet cover this topic; try a broader phrasing" } });
}
```

The user-facing detail text is preserved verbatim in `Params["detail"]` (same key convention as the `"generationId"` param used by `SubmitLeafletFeedbackHandler`/others), rather than being lost. No other line in the handler changes — the KB/leaflet retrieval calls above this check, and the two-stage LLM generation below it, are untouched.

**Acceptance criteria:**
- When both `kbHits` and `leafletHits` are empty, `Handle` returns (does not throw) a `GenerateLeafletResponse` with `Success == false`, `ErrorCode == ErrorCodes.LeafletEmptyRetrieval`, and `Params["detail"]` containing the original message text.
- When either collection is non-empty, behavior is unchanged (existing cold-start logging and two-stage generation still run).
- `GenerateLeafletHandler.cs` no longer references `EmptyRetrievalException`.

### FR-4: `LeafletController.Generate` delegates to `HandleResponse`
Replace the entire method body (lines 37–67 of `LeafletController.cs`), including the try/catch for `EmptyRetrievalException`, `OperationCanceledException`, and the catch-all `Exception` → 502 mapping, with the same shape used by every other action in the file:

```csharp
[HttpPost("generate")]
[FeatureAuthorize(Feature.Marketing_Leaflet, AccessLevel.Write)]
[ProducesResponseType(typeof(GenerateLeafletResponse), 200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(GenerateLeafletResponse), 422)]
public async Task<ActionResult<GenerateLeafletResponse>> Generate([FromBody] GenerateLeafletRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```

Notes on the specific attribute/signature changes:
- Return type changes from `Task<IActionResult>` to `Task<ActionResult<GenerateLeafletResponse>>`, matching every other action in this controller (`GetChunkDetail`, `SubmitFeedback`, `GetGeneration`, etc.) and `HandleResponse<T>`'s signature.
- The `[ProducesResponseType(typeof(ProblemDetails), 422)]` attribute is changed to `typeof(GenerateLeafletResponse)`, because `HandleResponse` now returns the actual `GenerateLeafletResponse` DTO (with `success: false`, `errorCode`, `params`) on the 422 path, not a `ProblemDetails` object. This is a **breaking change to the documented/generated 422 response shape** — see FR-6.
- The `[ProducesResponseType(typeof(ProblemDetails), 400)]` attribute is retained: it reflects ASP.NET's automatic `[ApiController]` model-validation response (triggered by the `[Required]`/`[MinLength]`/`[MaxLength]` attributes on `GenerateLeafletRequest`), which is unrelated to this fix and unaffected by it.
- The `[ProducesResponseType(typeof(ProblemDetails), 502)]` attribute is removed — there is no code path left in this action that produces a 502. Any genuinely unexpected exception (e.g., an embedding/chat-client failure) now propagates out of the action to ASP.NET's global exception-handling pipeline (`services.AddExceptionHandler<...>()` + `services.AddProblemDetails()` in `ServiceCollectionExtensions.AddCrossCuttingServices`), which returns a generic `ProblemDetails` response. **This changes the status code for unexpected failures from 502 to whatever the global pipeline's default is (500, since no registered `IExceptionHandler` specifically matches generic exceptions) and drops the custom "Leaflet generation failed. Please try again." message.** This is a deliberate consequence of removing the catch-all per the brief's rationale ("silently catches all other exceptions as 502, which swallows potential bugs") — see Open Questions for confirmation before implementation.

**Acceptance criteria:**
- `LeafletController.Generate` contains no try/catch block.
- `LeafletController.cs` no longer references `EmptyRetrievalException`.
- A mocked `IMediator.Send` returning a `GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...)` causes `Generate` to return an `ObjectResult`/`UnprocessableEntityObjectResult` with `StatusCode == 422` and `Value` equal to that same `GenerateLeafletResponse` instance (via `HandleResponse`'s existing `StatusCode` switch, default arm).
- A mocked `IMediator.Send` returning a successful `GenerateLeafletResponse` causes `Generate` to return `OkObjectResult` with that response, unchanged from today.
- A mocked `IMediator.Send` that throws (e.g., `InvalidOperationException`) is **not** caught by `Generate` — it propagates out of the action (verified by an integration-level or middleware test, not a controller-unit-test catch expectation).

### FR-5: Update the MCP tool consumer (`LeafletTools.GenerateLeaflet`)
`backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs` (lines 52–55) also catches `EmptyRetrievalException` and rethrows it as `McpException(ex.Message)`. Once `GenerateLeafletHandler` stops throwing that exception (FR-3), this catch block becomes dead code and the MCP tool would silently return a "successful" JSON payload with `success: false` instead of surfacing an MCP-level error. Update `GenerateLeaflet` to check the response after the `_mediator.Send` call, mirroring how a non-throwing handler result should be surfaced over MCP:

```csharp
var response = await _mediator.Send(new GenerateLeafletRequest
{
    Topic = topic,
    Audience = audienceEnum,
    Length = lengthEnum
}, ct);

if (!response.Success)
{
    var message = response.ErrorCode == ErrorCodes.LeafletEmptyRetrieval
        ? "Knowledge Base does not yet cover this topic; try a broader phrasing"
        : "Leaflet generation failed. Please try again.";
    throw new McpException(message);
}

return JsonSerializer.Serialize(response);
```

Remove the now-unreachable `catch (EmptyRetrievalException ex) { throw new McpException(ex.Message); }` block (lines 52–55). Keep the existing `catch (McpException)` rethrow and the generic `catch (Exception ex)` → `McpException("Leaflet generation failed. Please try again.")` block — those handle genuinely unexpected failures (e.g., embedding/chat client exceptions) and are a legitimate MCP-protocol boundary translation, not the "business logic in caller" problem the brief targets.

**Acceptance criteria:**
- `LeafletTools.cs` no longer references `EmptyRetrievalException`.
- A mocked `IMediator.Send` returning `GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...)` causes `GenerateLeaflet` to throw `McpException` with the empty-retrieval message (same externally-observable behavior as before, achieved via response inspection instead of exception catching).
- A mocked `IMediator.Send` returning a successful response still returns the serialized JSON payload, unchanged.

### FR-6: Delete `EmptyRetrievalException`
After FR-4 and FR-5 land, `EmptyRetrievalException` (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/EmptyRetrievalException.cs`) has zero remaining consumers (confirmed: only `LeafletController.cs` and `LeafletTools.cs` referenced it; both are updated by this fix). Delete the file.

**Acceptance criteria:**
- `EmptyRetrievalException.cs` is removed from the repository.
- A repo-wide search for `EmptyRetrievalException` returns no matches outside of removed/updated test files (see FR-8).

### FR-7: Frontend: fix the 422 error-shape regression in `LeafletGenerateTab.tsx`
Changing the `[ProducesResponseType]` attribute for the 422 case (FR-4) from `ProblemDetails` to `GenerateLeafletResponse` changes what the auto-generated TypeScript client (`frontend/src/api/generated/api-client.ts`, regenerated automatically on build) parses and throws for a 422 response. Today, `processLeaflet_Generate` parses 422 bodies as `ProblemDetails` and `throwException` throws that parsed object directly, so the caught `err` has `.status` (422) and `.detail` (the message) — which is exactly what `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`'s `isApiError`/`ApiError` duck-typing (lines 7–19) checks for.

After this fix, the thrown `err` on a 422 will instead be a `GenerateLeafletResponse` instance (fields: `success`, `errorCode`, `params`, `content`, `id`, `kbSourceCount`, `leafletSourceCount` — inherited from `BaseResponse`/`GenerateLeafletResponse`'s generated TS classes). It has **no `status` field**, so `isApiError(err)` will return `false` for this case, and the component's `catch` block (lines 39–52) will fall through to the generic `'transient'` red banner ("Generování selhalo...") instead of the intended `'insufficient'` amber banner — a silent UX regression if left unfixed.

Update `LeafletGenerateTab.tsx` to detect this case via the response's `errorCode` field instead of HTTP status:

```ts
import { ErrorCodes, GenerateLeafletResponse } from '../../api/generated/api-client';
// ...
} catch (err: unknown) {
  if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
    setErrorBanner({
      kind: 'insufficient',
      message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
    });
  } else {
    setErrorBanner({
      kind: 'transient',
      message: 'Generování selhalo. Zkuste to prosím znovu.',
    });
  }
}
```

The existing hardcoded Czech fallback message can be used directly (no server-supplied detail text is needed client-side any more, since the server-side `Params["detail"]` string from FR-3 is in English and was never surfaced to end users through this path anyway — the component already had its own Czech copy as the primary/fallback text).

The now-unused `ApiError`/`isApiError` helper (lines 7–19) should be removed if nothing else in the file uses it; keep it only if another code path in the same file still depends on `status`-based detection.

**Acceptance criteria:**
- After regenerating the OpenAPI client, submitting a topic with zero KB/leaflet hits shows the amber "insufficient knowledge" banner (not the red "transient" banner).
- Any other failure (network error, unexpected 500, etc.) still shows the red "transient" banner.
- `npm run build` and `npm run lint` pass.

### FR-8: Update existing tests to match the new contract
The following existing tests assert the old throw/catch behavior and must be updated (not just left failing):

- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`, test `Handle_dual_empty_retrieval_throws_EmptyRetrievalException` (around line 85): change from `await act.Should().ThrowAsync<EmptyRetrievalException>();` to asserting the handler *returns* a `GenerateLeafletResponse` with `Success == false` and `ErrorCode == ErrorCodes.LeafletEmptyRetrieval`. Rename the test to reflect the new behavior (e.g. `Handle_dual_empty_retrieval_returns_LeafletEmptyRetrieval_error`).
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs`:
  - `Generate_returns_422_on_EmptyRetrievalException` (lines 74–102): change the mediator mock from `.ThrowsAsync(new EmptyRetrievalException(...))` to `.ReturnsAsync(new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...))`; assert the result is a `422` `ObjectResult`/`UnprocessableEntityObjectResult` whose `Value` is the `GenerateLeafletResponse` (not `ProblemDetails`), with `ErrorCode == ErrorCodes.LeafletEmptyRetrieval`.
  - `Generate_returns_502_on_unexpected_exception` (lines 104–133): this test's premise no longer holds (see FR-4's open question). Pending the answer, either delete it or replace it with a test confirming the exception now propagates unhandled out of `Generate` (e.g. `await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Generate(request, CancellationToken.None));`), consistent with `Generate_propagates_OperationCanceledException` immediately below it.
  - `Generate_propagates_OperationCanceledException` (lines 135–155): behavior is unchanged (still propagates); no changes required, though the test may be simplified now that there's no explicit `catch (OperationCanceledException) { throw; }` to exercise.
- `backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs`, test `GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException` (around lines 63–90): change the mediator mock from `.ThrowsAsync(new EmptyRetrievalException(...))` to `.ReturnsAsync(new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...))`; keep asserting that `GenerateLeaflet` throws `McpException` with the expected message. Rename the test accordingly.

**Acceptance criteria:**
- `dotnet build` and the full `Anela.Heblo.Tests` suite pass with no reference to `EmptyRetrievalException` remaining anywhere in the test project.
- No test is skipped or deleted without an equivalent replacement asserting the new behavior (except the 502 test, whose disposition depends on the Open Question below).

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. Returning a DTO from an early `if` branch is at least as cheap as throwing and catching a `.NET` exception (exception throw/catch has non-trivial overhead versus a normal return), so this is a minor net improvement on the empty-retrieval path, not a regression. No new allocations of consequence (one small `Dictionary<string,string>`, same order of magnitude as today's `Params` usage in sibling handlers).

### NFR-2: Security / information disclosure
- The `Params["detail"]` message set in FR-3 must remain the curated, user-safe string already in use today ("Knowledge Base does not yet cover this topic; try a broader phrasing") — do **not** use `BaseResponse`'s `Exception`-based constructor (which serializes `ex.ToString()`, including stack traces, into `Params`) for this expected business condition; that constructor is reserved for genuinely unexpected failures.
- Removing the catch-all in `LeafletController.Generate` (FR-4) must not cause internal exception details (message, stack trace) to leak to the client. This is delegated to the existing global `AddProblemDetails()` pipeline, which by default does not include exception details unless the environment is configured for it (verify `IncludeExceptionDetails` / development-only behavior is already correctly scoped — this is existing, unchanged infrastructure, not new to this fix).

### NFR-3: Consistency / maintainability
After this fix, all five Leaflet-module handlers (`GenerateLeafletHandler`, `SubmitLeafletFeedbackHandler`, `GetLeafletGenerationHandler`, `GetLeafletChunkDetailHandler`, and the others already following the pattern) use exactly one error-signaling idiom: return a `BaseResponse`-derived DTO with `ErrorCode` set, mapped to HTTP status by `HandleResponse` via `[HttpStatusCode]` attributes on `ErrorCodes`. No handler in the module throws a domain exception for an expected/anticipated business condition; no controller action in `LeafletController` contains a try/catch for business-logic error mapping.

### NFR-4: Backward compatibility (HTTP contract)
For the specific empty-retrieval case, the HTTP status code returned to REST API clients must remain `422 Unprocessable Entity` (achieved via the `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]` attribute on the new `ErrorCodes.LeafletEmptyRetrieval` member) — only the response *body shape* changes (from `ProblemDetails { status, title, detail }` to `GenerateLeafletResponse { success: false, errorCode: "LeafletEmptyRetrieval", params: { detail: "..." }, content: "", ... }`). Any consumer that only branches on HTTP status code is unaffected; any consumer that reads `.detail` from the body must be updated (this repo has exactly one such consumer — `LeafletGenerateTab.tsx`, addressed in FR-7).

## Data Model
No persistence/schema changes. The only "data model" change is the addition of one member to the in-memory `ErrorCodes` enum (`Anela.Heblo.Application.Shared.ErrorCodes.LeafletEmptyRetrieval = 2504`), which flows into the generated OpenAPI schema and TypeScript client as an additional enum value on rebuild.

## API / Interface Design

`POST /api/leaflet/generate`

**Before (error path):**
```
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json

{
  "status": 422,
  "title": "Insufficient knowledge",
  "detail": "Knowledge Base does not yet cover this topic; try a broader phrasing"
}
```

**After (error path) — same status code, new body shape:**
```
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json

{
  "success": false,
  "errorCode": "LeafletEmptyRetrieval",
  "params": { "detail": "Knowledge Base does not yet cover this topic; try a broader phrasing" },
  "content": "",
  "id": null,
  "kbSourceCount": 0,
  "leafletSourceCount": 0
}
```

Success path (`200 OK`) is unchanged in shape (`GenerateLeafletResponse` with `success: true`).

MCP tool `GenerateLeaflet` (exposed via `LeafletTools.cs`): externally-observable behavior unchanged — still throws `McpException` with a message when the KB has no coverage for the topic; internal mechanism changes from exception-catch to response-inspection (FR-5).

## Dependencies
None new. This fix touches only existing types/services already in the Leaflet module: `ErrorCodes`, `BaseResponse`, `BaseApiController.HandleResponse`, `GenerateLeafletHandler`, `GenerateLeafletResponse`, `LeafletController`, `LeafletTools`, and their respective test files, plus the auto-generated OpenAPI TypeScript client and the one frontend component that special-cases this error.

## Out of Scope
- Any change to the retrieval/generation logic itself (embedding search, RAG query expansion, two-stage chat generation) — only the empty-result signaling path changes.
- A general-purpose reusable "external-service transient failure → 502" exception handler for the Leaflet module or others. If the Open Question below is answered "keep a safety net," a minimal, scoped solution (e.g., a dedicated `IExceptionHandler` or a narrower catch inside the handler itself) is a follow-up decision, not part of this fix.
- Any change to the other four Leaflet handlers, which already follow the target pattern correctly and need no modification.
- Changes to `ArticleGeneration` or other modules' analogous "not generated yet" error codes (e.g. `ErrorCodes.ArticleNotGenerated`), even though they follow the same pattern this fix reinforces — out of scope for this specific arch-review finding.

## Open Questions

1. **Should `LeafletController.Generate` retain a safety-net catch-all that maps genuinely unexpected exceptions (e.g., embedding/chat-client transient failures) to a distinct status code (today: 502 with a friendly "please try again" message), or should those now fall through entirely to the global ASP.NET exception-handling pipeline (`AddProblemDetails()`), which returns a generic `ProblemDetails` with status 500 and no leaflet-specific messaging?**
   The brief's suggested fix ("remove the try/catch ... rely on HandleResponse") and its stated rationale ("silently catches all other exceptions as 502, which swallows potential bugs") both point toward full removal, which is what this spec assumes (FR-4, FR-8). However, this is a genuine externally-visible behavior change (502 → 500, loss of the custom message) that may affect monitoring/alerting dashboards or any external caller keyed on 502 for "AI generation transient failure" versus 500 for "server bug." Please confirm before implementation whether:
   - (a) full removal is acceptable (this spec's assumption), or
   - (b) a scoped safety net should be preserved — e.g., a dedicated `IExceptionHandler` registered alongside `UnauthorizedAccessExceptionHandler`/`ValidationExceptionHandler`/`ArgumentExceptionHandler` in `ServiceCollectionExtensions.AddCrossCuttingServices` that maps specific external-service exception types (e.g., failures from the embedding/chat clients) to 502, without reintroducing a per-action try/catch in the controller.

## Status: HAS_QUESTIONS
