# Specification: Move PhotobankGraphService to Adapters and Replace Exception-Based Error Propagation

## Summary

`PhotobankGraphService` is a concrete I/O-bound adapter that calls Microsoft Graph over HTTP and acquires OAuth tokens via MSAL, but it currently lives in the Application layer, coupling the Application project's build artifact to `Microsoft.Identity.Web` and `Microsoft.Graph`. This refactor moves the concrete implementation to `Anela.Heblo.Adapters.Microsoft365`, introduces a typed result discriminated union for `GetThumbnailAsync`, and removes the infrastructure-library exception imports from `GetThumbnailHandler` — bringing the Photobank module into compliance with the project's I/O placement rule and Clean Architecture layering.

## Background

`docs/architecture/filesystem.md` states the I/O placement rule:
> Concrete implementations and any I/O-bound service live in adapter projects under `backend/src/Adapters/`, not in `Features/{Feature}/Services/`

`PhotobankGraphService` (at `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs`) injects `ITokenAcquisition` (Microsoft.Identity.Web) and `IHttpClientFactory`, makes paginated HTTP calls to `https://graph.microsoft.com/v1.0`, and throws a custom `GraphThrottledException` (which wraps HTTP 429) back to its callers. Because the Application project hosts this class, `Anela.Heblo.Application.csproj` carries `<PackageReference>` entries for `Microsoft.Graph` and `Microsoft.Identity.Web` — infrastructure concerns that belong in the outermost ring.

The downstream consequence is that `GetThumbnailHandler` (`backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`) must `using Microsoft.Identity.Client;` and catch `MsalException` at lines 59-63 — an infrastructure-library exception type leaking directly into an Application-layer handler. The handler must also catch `HttpRequestException` (line 55) and `GraphThrottledException` (line 44), all to translate low-level transport failures into `ErrorCodes` values that could equally be produced by the adapter before surfacing to the handler.

`Anela.Heblo.Adapters.Microsoft365` already exists and already references `Microsoft.Graph` and `Microsoft.Identity.Web` in its `.csproj`. It is the natural home for the Graph adapter.

## Functional Requirements

### FR-1: Move `PhotobankGraphService` to `Anela.Heblo.Adapters.Microsoft365`

Create a `Photobank/` subfolder inside `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/` and move `PhotobankGraphService.cs` there. Update its namespace to `Anela.Heblo.Adapters.Microsoft365.Photobank`. No logic changes are made to the class during the move, except the namespace declaration and the `using` directives for internal DTOs.

**Acceptance criteria:**
- `PhotobankGraphService.cs` no longer exists under `backend/src/Anela.Heblo.Application/Features/Photobank/Services/`.
- The class compiles in its new location with namespace `Anela.Heblo.Adapters.Microsoft365.Photobank`.
- `Anela.Heblo.Application.csproj` no longer contains `<PackageReference Include="Microsoft.Graph" .../>` or `<PackageReference Include="Microsoft.Identity.Web" .../>` (those references already exist in the adapter project and are not needed in Application).
- All existing DI wiring (`IPhotobankGraphService -> PhotobankGraphService`) continues to resolve correctly at runtime.

### FR-2: Keep `IPhotobankGraphService` and related types in the Application layer

The interface `IPhotobankGraphService`, the enum `ThumbnailSize`, the class `GraphPhotoItem`, the class `GraphDeltaResult`, and the class `GraphThumbnail` must remain in `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs`. These are Application-owned contracts. The exception type `GraphThrottledException` is moved out of this file (see FR-3).

**Acceptance criteria:**
- `IPhotobankGraphService`, `ThumbnailSize`, `GraphPhotoItem`, `GraphDeltaResult`, and `GraphThumbnail` remain in the namespace `Anela.Heblo.Application.Features.Photobank.Services`.
- No handler, job, or other Application-layer type must reference any type from `Anela.Heblo.Adapters.Microsoft365` or any `Microsoft.Identity.Client`/`Microsoft.Graph` namespace.
- `Anela.Heblo.Adapters.Microsoft365.csproj` already has a `<ProjectReference>` to `Anela.Heblo.Application.csproj`; no new project reference is needed in the reverse direction.

### FR-3: Replace `GetThumbnailAsync` throw-on-error with a typed result

Change the signature of `GetThumbnailAsync` on `IPhotobankGraphService` from:

```csharp
Task<GraphThumbnail?> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
```

to:

```csharp
Task<GetThumbnailResult> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
```

`GetThumbnailResult` is a sealed discriminated union defined in the Application layer alongside the interface:

```csharp
public abstract class GetThumbnailResult
{
    public sealed class Success : GetThumbnailResult
    {
        public GraphThumbnail Thumbnail { get; }
        public Success(GraphThumbnail thumbnail) => Thumbnail = thumbnail;
    }

    public sealed class NotFound : GetThumbnailResult { }

    public sealed class Throttled : GetThumbnailResult
    {
        public TimeSpan? RetryAfter { get; }
        public Throttled(TimeSpan? retryAfter) => RetryAfter = retryAfter;
    }

    public sealed class AuthError : GetThumbnailResult
    {
        public string Detail { get; }
        public AuthError(string detail) => Detail = detail;
    }

    public sealed class UpstreamError : GetThumbnailResult
    {
        public string Detail { get; }
        public UpstreamError(string detail) => Detail = detail;
    }
}
```

The adapter (`PhotobankGraphService`) catches `MsalException`, `HttpRequestException`, and inspects HTTP status codes (`404`, `429`, `406`) internally, mapping each to the appropriate `GetThumbnailResult` case before returning. It no longer throws `GraphThrottledException`.

`GetThumbnailHandler` replaces its three `catch` blocks and the `null`-check with a `switch` expression (or `is`-type-check chain) over the result cases, mapping each to the existing `ErrorCodes` values. The handler no longer needs `using Microsoft.Identity.Client;` or `using System.Net.Http;`.

The `GraphThrottledException` class is deleted from `IPhotobankGraphService.cs`; it is no longer part of the public contract.

**Acceptance criteria:**
- `IPhotobankGraphService.GetThumbnailAsync` returns `Task<GetThumbnailResult>`.
- `GetThumbnailResult` and its five nested cases are declared in `Anela.Heblo.Application.Features.Photobank.Services`.
- `GetThumbnailHandler` contains no `catch` blocks and no `using` directives for `Microsoft.Identity.Client` or `System.Net.Http`.
- `GetThumbnailHandler` pattern-matches the result and maps `Success` → success response, `NotFound` → `PhotobankThumbnailNotFound`, `Throttled` → `PhotobankThumbnailThrottled` (with `RetryAfterSeconds`), `AuthError` → `PhotobankThumbnailAuthUnavailable`, `UpstreamError` → `PhotobankThumbnailUpstream`. The existing `ErrorCodes` enum values are not changed.
- `GraphThrottledException` no longer exists anywhere in the codebase.
- `MockPhotobankGraphService` is updated to return `GetThumbnailResult.Success` (wrapping the minimal PNG as before).

### FR-4: Move `MockPhotobankGraphService` or leave it in Application

`MockPhotobankGraphService` has no external I/O and no infrastructure dependencies. It may remain in `Anela.Heblo.Application/Features/Photobank/Services/` for simplicity, or move to a test-helpers project. The choice is to leave it in Application — it is a legitimate stub that is registered in the DI container under `UseMockAuth` / `BYPASS_JWT_VALIDATION` conditions, and the Application project does not incur any infrastructure dependency by hosting it.

**Acceptance criteria:**
- `MockPhotobankGraphService` remains in `Anela.Heblo.Application.Features.Photobank.Services` and continues to compile.
- It implements the updated `IPhotobankGraphService` (returning `GetThumbnailResult.Success` from `GetThumbnailAsync`).

### FR-5: Update DI registration in `PhotobankModule` and `Microsoft365AdapterServiceCollectionExtensions`

Currently, both the `HttpClient` named `"MicrosoftGraph"` and the `IPhotobankGraphService → PhotobankGraphService` binding are registered in `PhotobankModule.cs` (Application layer). Because `PhotobankGraphService` moves to the adapter project, its DI binding must also move to `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter(...)`.

- The `HttpClient` named `"MicrosoftGraph"` registration (`AddHttpClient("MicrosoftGraph", ...)`) moves to `AddMicrosoft365Adapter`.
- The `services.AddScoped<IPhotobankGraphService, PhotobankGraphService>()` binding moves to `AddMicrosoft365Adapter`.
- `PhotobankModule.cs` retains the `MockPhotobankGraphService` binding under the mock-auth branch, and removes the real-adapter branch (which now lives in the adapter extension).
- `Program.cs` (or wherever composition root wires adapters) must call `AddMicrosoft365Adapter` before `AddPhotobankModule` so the real implementation is already registered when the module runs (or order is irrelevant if both register under different conditions — the existing `useMockAuth` / `bypassJwt` guard logic must be consistent across both registration sites).

**Acceptance criteria:**
- `PhotobankModule.cs` no longer references `PhotobankGraphService` (the concrete class) when not in mock mode.
- `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter` registers `IPhotobankGraphService → PhotobankGraphService` and the `"MicrosoftGraph"` named `HttpClient` under the same `!useMockAuth && !bypassJwt` condition.
- `dotnet build` produces zero errors and zero warnings related to missing registrations.
- Application starts correctly in both mock-auth and real-auth modes.

### FR-6: Remove `Microsoft.Graph` and `Microsoft.Identity.Web` package references from Application project

Once the concrete implementation and all usages of these packages are removed from the Application layer, delete those `<PackageReference>` entries from `Anela.Heblo.Application.csproj`.

**Acceptance criteria:**
- `Anela.Heblo.Application.csproj` contains no `<PackageReference Include="Microsoft.Graph" .../>` line.
- `Anela.Heblo.Application.csproj` contains no `<PackageReference Include="Microsoft.Identity.Web" .../>` line.
- `dotnet build` of the Application project succeeds without those references.

## Non-Functional Requirements

### NFR-1: No behavior change

This is a pure structural refactor. No HTTP calls, retry logic, token acquisition behavior, or response mappings to `ErrorCodes` may change. The handler's observable outputs (status codes, `RetryAfterSeconds`, response bodies) are identical before and after.

### NFR-2: No new public API surface

`GetThumbnailResult` and its cases are internal to the Photobank feature's service contract. They are not exposed via any controller response DTO or OpenAPI schema.

### NFR-3: Build must pass

`dotnet build` must succeed for all projects (`Anela.Heblo.Application`, `Anela.Heblo.Adapters.Microsoft365`, `Anela.Heblo.API`, `Anela.Heblo.Tests`). `dotnet format` must produce no diffs. No new compiler warnings are introduced.

### NFR-4: Existing tests must pass

All unit and integration tests in `backend/test/Anela.Heblo.Tests/` that exercise `GetThumbnailHandler` or mock `IPhotobankGraphService` must pass without modification, except for the minimum updates required to satisfy the new `GetThumbnailAsync` signature (returning `GetThumbnailResult` instead of `GraphThumbnail?`/throwing).

## Data Model

No domain entities or database schema changes. This refactor is entirely in the service and adapter layers.

Key types and their post-refactor homes:

| Type | Project | Namespace |
|---|---|---|
| `IPhotobankGraphService` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `ThumbnailSize` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `GraphThumbnail` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `GraphPhotoItem` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `GraphDeltaResult` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `GetThumbnailResult` (new) | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `MockPhotobankGraphService` | `Anela.Heblo.Application` | `...Features.Photobank.Services` |
| `PhotobankGraphService` | `Anela.Heblo.Adapters.Microsoft365` | `...Photobank` |
| `GraphThrottledException` | deleted | — |

## API / Interface Design

### Updated `IPhotobankGraphService`

```csharp
// Stays in Anela.Heblo.Application.Features.Photobank.Services
public interface IPhotobankGraphService
{
    Task<GraphDeltaResult> GetDeltaAsync(string driveId, string rootItemId, string? deltaLink, CancellationToken cancellationToken = default);
    Task<string> ResolveItemIdAsync(string driveId, string folderPath, CancellationToken cancellationToken = default);
    Task<GetThumbnailResult> GetThumbnailAsync(string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
}
```

### `GetThumbnailHandler` after refactor (exception handling removed)

```csharp
var result = await _graphService.GetThumbnailAsync(
    locator.DriveId, locator.SharePointFileId, request.Size, cancellationToken);

return result switch
{
    GetThumbnailResult.Success s => new GetThumbnailResponse
    {
        Content = s.Thumbnail.Content,
        ContentType = s.Thumbnail.ContentType,
        ContentLength = s.Thumbnail.ContentLength,
    },
    GetThumbnailResult.NotFound => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound),
    GetThumbnailResult.Throttled t => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled)
    {
        RetryAfterSeconds = t.RetryAfter.HasValue ? (int)Math.Ceiling(t.RetryAfter.Value.TotalSeconds) : null,
    },
    GetThumbnailResult.AuthError a => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable),
    GetThumbnailResult.UpstreamError u => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream),
    _ => throw new InvalidOperationException($"Unhandled GetThumbnailResult case: {result?.GetType().Name}"),
};
```

The logging calls that currently live inside the `catch` blocks must be preserved: the `Throttled` path logs a warning, the `AuthError` path logs an error with the MSAL error detail, and the `UpstreamError` path logs a warning with the HTTP error detail. These log calls move into the adapter (before returning the result case) or remain in the handler (by adding an overload that carries a log message string on the result). Assumption: log calls move into the adapter, where the exception context is available; the handler only receives the typed result.

## Dependencies

- `Anela.Heblo.Adapters.Microsoft365.csproj` already references `Microsoft.Graph 5.92.0` and `Microsoft.Identity.Web 3.14.1` — no new package dependencies.
- `Anela.Heblo.Adapters.Microsoft365.csproj` already has a `<ProjectReference>` to `Anela.Heblo.Application.csproj` — no new project references.
- The Application project must drop its `Microsoft.Graph` and `Microsoft.Identity.Web` package references (FR-6). Verify no other file in the Application project imports from those packages before removing; a `grep` over the project for `using Microsoft.Graph` and `using Microsoft.Identity` should return zero hits after the move.

## Out of Scope

- `GetDeltaAsync` and `ResolveItemIdAsync` error handling. Those methods currently throw `HttpRequestException` on non-success status codes (via `response.EnsureSuccessStatusCode()`). Wrapping them in a typed result is a follow-on concern; this spec does not change those signatures.
- Retry / resilience policy changes. The adapter's existing behavior (no Polly retry) is unchanged.
- Moving `PhotobankTagsCache` or any other service — only `PhotobankGraphService` is in scope.
- Any changes to the controller, OpenAPI spec, or frontend.
- Moving `MockPhotobankGraphService` to a test-helpers project.
- Database or migration changes.

## Open Questions

None.

## Status: COMPLETE
