## Module
Leaflet

## Finding
`GenerateLeafletHandler` throws a domain exception when the knowledge base returns no results, forcing the controller to catch it and decide the HTTP status code:

**Handler** (`backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`, lines 63–67):
```csharp
if (kbHits.Count == 0 && leafletHits.Count == 0)
{
    throw new EmptyRetrievalException(
        "Knowledge Base does not yet cover this topic; try a broader phrasing");
}
```

**Controller** (`backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`, lines 39–66):
```csharp
try
{
    var response = await _mediator.Send(request, ct);
    return Ok(response);
}
catch (EmptyRetrievalException ex)
{
    return UnprocessableEntity(new ProblemDetails { Status = 422, ... });
}
catch (Exception ex)
{
    // maps all other exceptions to 502
    return StatusCode(502, ...);
}
```

Every other handler in the Leaflet module returns an error response object with an error code instead of throwing:
- `SubmitLeafletFeedbackHandler` → `new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden, ...)`
- `GetLeafletGenerationHandler` → `new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound)`
- `GetLeafletChunkDetailHandler` → `new GetLeafletChunkDetailResponse(ErrorCodes.LeafletChunkNotFound)`

## Why it matters
The forbidden practices in `docs/architecture/development_guidelines.md` explicitly state: **"Business logic in Controller class — Business logic should be in MediatR handlers."** The controller knowing that `EmptyRetrievalException` maps to HTTP 422 is a business-logic decision that belongs in the handler or in a shared exception-to-problem-details middleware. It also makes the controller import a domain exception type from the Application layer and silently catches *all other* exceptions as 502, which swallows potential bugs.

The inconsistency also means two different error-signalling patterns co-exist in the same module, increasing cognitive load.

## Suggested fix
Minimal fix: replace the throw with a response error code.

1. Add `ErrorCodes.EmptyRetrieval` (or reuse an existing generic code like `ErrorCodes.NoContent`).
2. Have `GenerateLeafletHandler.Handle` return `new GenerateLeafletResponse(ErrorCodes.EmptyRetrieval) { ... }` with the user-facing detail in a response property instead of throwing.
3. Remove the try/catch from `LeafletController.Generate` and rely on `HandleResponse` (already used by every other action in the controller) to produce the correct HTTP result.
4. Delete `EmptyRetrievalException.cs` if it has no other consumers.

---
_Filed by daily arch-review routine on 2026-07-04._
