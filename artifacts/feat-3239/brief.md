## Module
Photobank

## Finding
`PhotobankGraphService` and `MockPhotobankGraphService` are concrete I/O-bound service implementations that live in the Application layer:

- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs`

`PhotobankGraphService` makes HTTP calls to the Microsoft Graph API via `IHttpClientFactory` and acquires tokens via `ITokenAcquisition` (Microsoft.Identity.Web). It is an infrastructure adapter, not application logic.

A related symptom: `GetThumbnailHandler` (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`, lines 7, 53–63) directly `using Microsoft.Identity.Client;` and catches `MsalException` and `HttpRequestException` — infrastructure-library exceptions leaking into an Application-layer handler, coupling business logic to a specific auth library.

## Why it matters
`docs/architecture/filesystem.md` states the I/O placement rule:
> Concrete implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`.

There is already an `Anela.Heblo.Adapters.Microsoft365` project that is the natural home for this. Having the concrete implementation in Application ties the build artifact of the Application project to `Microsoft.Identity.Web`, `Microsoft.Identity.Client`, and `HttpClient` — infrastructure concerns that Clean Architecture places in the outermost ring.

The handler catching `MsalException` and `HttpRequestException` is a direct consequence: because the adapter "leaks" its exception types into the Application layer, the handler must know about them to translate them to domain error codes.

## Suggested fix
1. Move `PhotobankGraphService` to `Anela.Heblo.Adapters.Microsoft365` (or a new `Photobank/` subfolder within it). The `MockPhotobankGraphService` can stay in Application (it has no external I/O) or move to a test helpers project.
2. Keep `IPhotobankGraphService` in `Application/Features/Photobank/Services/` — the interface is Application-owned.
3. Change `GetThumbnailAsync` to return a result type (e.g., a `sealed record` with success/throttled/auth-error/not-found cases) instead of throwing Graph-specific exceptions. The adapter catches `MsalException` / `GraphThrottledException` and maps them to result cases; the handler pattern-matches on the result without any infrastructure imports.

---
_Filed by daily arch-review routine on 2026-06-19._
