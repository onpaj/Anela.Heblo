## Module
Photobank

## Finding
`PhotobankController` injects two services beyond `IMediator`:

```csharp
// backend/src/Anela.Heblo.API/Controllers/PhotobankController.cs, lines 39–50
private readonly IPhotobankRepository _photobankRepository;
private readonly IPhotobankGraphService _photobankGraphService;
```

The `GetThumbnail` action (lines 369–422) uses these directly to implement multi-step orchestration that belongs in the Application layer:

1. Queries the repository for the locator: `_photobankRepository.GetLocatorAsync`
2. Calls the external Graph service: `_photobankGraphService.GetThumbnailAsync`
3. Catches and handles infrastructure-specific exceptions: `GraphThrottledException`, `MsalException`, `HttpRequestException`
4. Writes response headers (`Retry-After`, `Cache-Control`, `Content-Length`) and streams binary content

This is ~50 lines of application-layer orchestration living inside the HTTP layer.

## Why it matters
- Violates the project rule: *"Business logic must be in MediatR handlers, NOT in controllers"* (`docs/architecture/development_guidelines.md`).
- The controller now depends on three abstractions (`IMediator`, `IPhotobankRepository`, `IPhotobankGraphService`) instead of one (`IMediator`), blurring the single entry-point contract.
- Exception handling for `GraphThrottledException` and `MsalException` is infrastructure knowledge leaking into the HTTP composition layer.
- The logic cannot be tested without an HTTP context.

## Suggested fix
Introduce a `GetThumbnail` use case:

```
Application/Features/Photobank/UseCases/GetThumbnail/
  GetThumbnailRequest.cs   (photoId, size)
  GetThumbnailResponse.cs  (Stream content, string contentType, long? contentLength, int? retryAfterSeconds, bool notFound, bool unavailable)
  GetThumbnailHandler.cs   (injects IPhotobankRepository + IPhotobankGraphService, moves current logic here)
```

The controller action becomes a thin dispatch:
```csharp
var result = await _mediator.Send(new GetThumbnailRequest { Id = id, Size = size }, ct);
if (result.NotFound) return NotFound();
if (result.Unavailable) { /* set Retry-After header, return 503 */ }
// stream result.Content
```

Remove `IPhotobankRepository` and `IPhotobankGraphService` from the controller constructor.

---
_Filed by daily arch-review routine on 2026-05-21._