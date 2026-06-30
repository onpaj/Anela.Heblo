# Architecture Review: Move PhotobankGraphService to Adapters and Replace Exception-Based Error Propagation

## Skip Design: true

## Architectural Fit Assessment

This refactor is well-aligned with the project's Clean Architecture layering and the I/O placement rule documented in `docs/architecture/filesystem.md`. The pattern it follows — interface in Application, concrete implementation in an Adapter project — is already established by `IOutlookCalendarSync` / `OutlookCalendarSyncService`. The `Anela.Heblo.Adapters.Microsoft365` project is the correct target.

The three integration points that matter:

1. **DI wiring split** — `PhotobankModule.cs` (Application) currently owns both the real and mock registrations. The real binding moves to `Microsoft365AdapterServiceCollectionExtensions`, leaving only the mock branch in `PhotobankModule`. This matches the established `IOutlookCalendarSync` / `NoOpOutlookCalendarSync` split: `MarketingModule` registers the no-op, `AddMicrosoft365Adapter` overrides it with the real implementation (last-registration-wins). `PhotobankModule` should follow the same override pattern rather than splitting on the same `useMockAuth` flag in two places.

2. **Test impact** — `GetThumbnailHandlerTests.cs` currently uses `ThrowsAsync` / `ReturnsAsync(null)` on the `IPhotobankGraphService` mock. All seven test setups and assertions must be rewritten to use `GetThumbnailResult` cases. `PhotobankGraphServiceThumbnailTests.cs` tests the concrete implementation; it must be moved or its namespace references updated since `PhotobankGraphService` moves to the adapter project. These changes are mechanical but non-trivial in size.

3. **FR-6 is blocked** — `Anela.Heblo.Application.csproj` currently carries `<PackageReference Include="Microsoft.Graph" .../>` and `<PackageReference Include="Microsoft.Identity.Web" .../>`. After moving `PhotobankGraphService`, those references are still legitimately consumed by five other Application-layer features: `GraphPlannerService` (MeetingTasks), `GraphOneDriveService` (KnowledgeBase), `GraphCatalogDocumentsStorage` (CatalogDocuments), `BackfillArticleRequestedByHandler` (Article), and `GraphService`/`GetGroupMembersHandler` (UserManagement). Deleting the package references breaks the build. FR-6 must be descoped from this feature.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application
  Features/Photobank/Services/
    IPhotobankGraphService.cs          (interface + shared types + GetThumbnailResult DU)
    MockPhotobankGraphService.cs       (no-op stub, stays here)
  Features/Photobank/UseCases/GetThumbnail/
    GetThumbnailHandler.cs             (no catch blocks, no infra usings)

Anela.Heblo.Adapters.Microsoft365
  Photobank/
    PhotobankGraphService.cs           (moved here; catches all infra exceptions internally)

Program.cs (composition root)
  AddPhotobankModule()                 → registers MockPhotobankGraphService (no-op fallback)
  AddMicrosoft365Adapter()             → overrides with PhotobankGraphService (real, last-wins)
```

Data flow for `GetThumbnailAsync`:

```
GetThumbnailHandler
  → calls IPhotobankGraphService.GetThumbnailAsync(...)
      ← PhotobankGraphService (adapter):
          catches MsalException        → returns GetThumbnailResult.AuthError
          catches HttpRequestException → returns GetThumbnailResult.UpstreamError
          HTTP 404 / 406               → returns GetThumbnailResult.NotFound
          HTTP 429                     → returns GetThumbnailResult.Throttled(retryAfter)
          HTTP 2xx                     → returns GetThumbnailResult.Success(thumbnail)
  → handler switch-matches result → GetThumbnailResponse
```

### Key Design Decisions

#### Decision 1: DI registration via no-op override, not conditional split

**Options considered:**
- A: `PhotobankModule` registers the no-op; `AddMicrosoft365Adapter` unconditionally overrides with the real implementation (same pattern as `IOutlookCalendarSync`).
- B: Both `PhotobankModule` and `AddMicrosoft365Adapter` check `useMockAuth`/`bypassJwt` independently and register different implementations conditionally.

**Chosen approach:** Option A — unconditional override.

**Rationale:** Option B duplicates the `useMockAuth` guard in two projects, creating a consistency risk: if someone adds a new auth-bypass configuration key, they must remember to update both files. Option A follows the established project convention (`MarketingModule` + `AddMicrosoft365Adapter`) and keeps the mock-vs-real decision in one place (the adapter extension). The no-op registered by `PhotobankModule` is overridden by `AddMicrosoft365Adapter` in production; in mock-auth environments `AddMicrosoft365Adapter` still runs but registers under the same condition it already checks for `IOutlookCalendarSync`, so the pattern is consistent. If `AddMicrosoft365Adapter` is called unconditionally (as `Program.cs` shows), the guard inside the extension method ensures the override only happens for real auth.

#### Decision 2: Logging stays in the adapter

**Options considered:**
- A: The adapter logs before returning each error result; the handler receives a clean typed value.
- B: Error result cases carry a log-message string; the handler logs at the point of mapping.
- C: Handler logs from the result; adapter logs too (double-logging).

**Chosen approach:** Option A — adapter logs, handler does not.

**Rationale:** The adapter has the exception object in scope (MSAL error code, HTTP status, URL). The handler has none of that context. Passing log strings through the result type (Option B) leaks presentation-layer concerns into the domain contract. Option A is the simplest, keeps the result type data-only, and is what the spec already proposes.

#### Decision 3: GetThumbnailResult as abstract class with sealed nested cases

**Options considered:**
- A: Abstract class with sealed nested cases (as specced).
- B: C# discriminated union via `OneOf<>` library.
- C: Interface-based union.

**Chosen approach:** Option A.

**Rationale:** The project does not use `OneOf` elsewhere. The abstract-class pattern with sealed nested types is idiomatic C# and exhaustive enough with the `_ => throw new InvalidOperationException(...)` arm in the switch. No new library dependency is introduced. This is the correct choice.

#### Decision 4: FR-6 is descoped

**Options considered:**
- A: Remove `Microsoft.Graph` and `Microsoft.Identity.Web` from `Application.csproj` as specified.
- B: Descope FR-6; those package references remain because other features still need them.

**Chosen approach:** Option B — descope FR-6.

**Rationale:** Five other Application-layer files import from these packages (`GraphPlannerService`, `GraphOneDriveService`, `GraphCatalogDocumentsStorage`, `BackfillArticleRequestedByHandler`, `GraphService`/`GetGroupMembersHandler`). Removing the package references causes compilation failures for all five. Cleaning up those services is a separate and larger refactor. The primary goal of this feature — moving `PhotobankGraphService` and eliminating infrastructure exception leakage from `GetThumbnailHandler` — is fully achievable without FR-6.

## Implementation Guidance

### Directory / Module Structure

Files to create:
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Photobank/PhotobankGraphService.cs`
  (moved from `Application/Features/Photobank/Services/PhotobankGraphService.cs`)

Files to modify:
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/IPhotobankGraphService.cs`
  — add `GetThumbnailResult` discriminated union; update `GetThumbnailAsync` return type; delete `GraphThrottledException`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/MockPhotobankGraphService.cs`
  — update `GetThumbnailAsync` to return `GetThumbnailResult.Success(...)`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetThumbnail/GetThumbnailHandler.cs`
  — replace catch blocks with result switch; remove `using Microsoft.Identity.Client;` and `using System.Net.Http;`
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
  — remove the `!useMockAuth && !bypassJwt` branch (and `AddHttpClient("MicrosoftGraph")` inside it); leave only `AddScoped<IPhotobankGraphService, MockPhotobankGraphService>()` as the no-op fallback (unconditionally, or under `useMockAuth || bypassJwt` — see DI decision above)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
  — add `AddHttpClient("MicrosoftGraph", ...)` and `AddScoped<IPhotobankGraphService, PhotobankGraphService>()` under the `!useMockAuth && !bypassJwt` guard (mirrors the existing `IOutlookCalendarSync` registration)
- `backend/test/Anela.Heblo.Tests/Features/Photobank/GetThumbnailHandlerTests.cs`
  — replace all `ThrowsAsync` / `ReturnsAsync(null)` setups with `ReturnsAsync(new GetThumbnailResult.X(...))` returns; remove `using Microsoft.Identity.Client;` and `using System.Net.Http;`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankGraphServiceThumbnailTests.cs`
  — update `using` for new namespace; update assertions from throw-based to result-based (`result.Should().BeOfType<GetThumbnailResult.Throttled>()`, etc.)

Files to delete:
- `backend/src/Anela.Heblo.Application/Features/Photobank/Services/PhotobankGraphService.cs`

Files NOT touched (FR-6 descoped):
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — `Microsoft.Graph` and `Microsoft.Identity.Web` references remain

### Interfaces and Contracts

`GetThumbnailResult` — declare in `Anela.Heblo.Application.Features.Photobank.Services`, in `IPhotobankGraphService.cs` alongside the interface:

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

Updated interface signature (only `GetThumbnailAsync` changes):

```csharp
Task<GetThumbnailResult> GetThumbnailAsync(
    string driveId, string fileId, ThumbnailSize size, CancellationToken cancellationToken = default);
```

`PhotobankGraphService` namespace after move: `Anela.Heblo.Adapters.Microsoft365.Photobank`

`PhotobankGraphService.GetThumbnailAsync` internal structure after refactor:
- Catch `MsalException` → log error (preserving the `ex.ErrorCode` log field) → `return new GetThumbnailResult.AuthError(ex.ErrorCode)`
- Catch `HttpRequestException` → log warning → `return new GetThumbnailResult.UpstreamError(ex.Message)`
- HTTP 404 → `return new GetThumbnailResult.NotFound()`
- HTTP 429 → log warning (preserving the `retryAfter` log field) → `return new GetThumbnailResult.Throttled(retryAfter)`
- HTTP 406 → `return new GetThumbnailResult.NotFound()`
- 2xx → `return new GetThumbnailResult.Success(thumbnail)`
- Do NOT call `response.EnsureSuccessStatusCode()` after the explicit status-code checks; replace it with an explicit fallback for other non-success codes returning `UpstreamError`.

### Data Flow

**GetThumbnailHandler (after refactor):**

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
        RetryAfterSeconds = t.RetryAfter.HasValue
            ? (int)Math.Ceiling(t.RetryAfter.Value.TotalSeconds)
            : null,
    },
    GetThumbnailResult.AuthError => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable),
    GetThumbnailResult.UpstreamError => new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream),
    _ => throw new InvalidOperationException($"Unhandled GetThumbnailResult case: {result.GetType().Name}"),
};
```

The `null`-check path (`if (rawThumbnail is null)`) is subsumed by `GetThumbnailResult.NotFound`. The handler acquires no stream ownership responsibility change — `GraphThumbnail.Content` is still a `MemoryStream` whose lifetime the caller manages.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-6 breaks build: 5 other Application-layer features still reference `Microsoft.Graph` / `Microsoft.Identity.Web` | High | Descope FR-6 entirely from this feature. The package references remain in `Application.csproj`. Their removal is a separate, broader refactor. |
| DI registration order: `PhotobankModule` no-op overridden by `AddMicrosoft365Adapter` only if adapter is called | Medium | `Program.cs` already calls `AddMicrosoft365Adapter` unconditionally (line 120). The `!useMockAuth && !bypassJwt` guard inside the extension ensures the override fires correctly. No change to `Program.cs` is needed. Verify by checking the guard condition matches exactly what `PhotobankModule` previously used. |
| `GetThumbnailHandlerTests` uses throw-based mock setups for `IPhotobankGraphService` | Medium | All 7 test methods must be rewritten. Tests using `.ThrowsAsync<GraphThrottledException>()` and `.ThrowsAsync<MsalServiceException>()` become `.ReturnsAsync(new GetThumbnailResult.Throttled(...))` and `.ReturnsAsync(new GetThumbnailResult.AuthError(...))`. The test for `null` return becomes `.ReturnsAsync(new GetThumbnailResult.NotFound())`. This is mechanical but the developer must not skip it — NFR-4 requires all tests pass. |
| `PhotobankGraphServiceThumbnailTests` creates `PhotobankGraphService` directly | Low | The test class will need its namespace import updated to `Anela.Heblo.Adapters.Microsoft365.Photobank`. The test project's `.csproj` must reference `Anela.Heblo.Adapters.Microsoft365`. Verify the test project already has this reference, or add it. |
| Other callers of `IPhotobankGraphService` (`PhotobankIndexJob`) do not call `GetThumbnailAsync` | None | `PhotobankIndexJob` only calls `GetDeltaAsync` and `ResolveItemIdAsync`, which are not changed by this spec. No risk. |
| `response.EnsureSuccessStatusCode()` currently at end of adapter method — must be replaced | Medium | The current implementation calls `EnsureSuccessStatusCode()` after 404/429/406 handling. After the refactor, that call must be replaced with an explicit catch on any remaining non-2xx status (e.g. log warning + return `UpstreamError`). Do not leave a bare `EnsureSuccessStatusCode()` or `HttpRequestException` will escape the adapter. |

## Specification Amendments

**FR-6 must be removed from this feature.** The spec states:

> `Anela.Heblo.Application.csproj` no longer contains `<PackageReference Include="Microsoft.Graph" .../>` or `<PackageReference Include="Microsoft.Identity.Web" .../>`.

This is incorrect as stated. Five other Application-layer services (`GraphPlannerService`, `GraphOneDriveService`, `GraphCatalogDocumentsStorage`, `BackfillArticleRequestedByHandler`, `GraphService`) continue to import from `Microsoft.Identity.Web` and `Microsoft.Identity.Client`. Removing those package references breaks the build for unrelated features. FR-6 acceptance criteria should be changed to: "The Application project's dependency on `Microsoft.Graph` and `Microsoft.Identity.Web` via `PhotobankGraphService` is eliminated, but the package references themselves remain until all other Application-layer Graph usages are migrated in a follow-on task."

**Logging in AuthError result:** The spec says logging may move to the adapter or stay in the handler via a log-message string on the result. The architecture decision above chooses adapter-side logging only. Implement accordingly: the `AuthError` result does NOT carry a log string; the adapter logs `LogError` with `ex.ErrorCode` before returning the result. The handler removes its `_logger` injection for this case (though the logger remains for the overall handler if still needed — check whether other log calls remain in the handler after the refactor; if the handler logs nothing, the `ILogger<GetThumbnailHandler>` injection becomes unnecessary and should be removed to keep the constructor minimal).

## Prerequisites

No infrastructure prerequisites. No migrations. No new packages. The `Anela.Heblo.Adapters.Microsoft365.csproj` already has the required package and project references. Implementation can start immediately.

Recommended implementation order:
1. Add `GetThumbnailResult` to `IPhotobankGraphService.cs` and update the interface signature.
2. Update `MockPhotobankGraphService` to implement the new signature.
3. Move and update `PhotobankGraphService` to the adapter project (namespace change + catch-to-result refactor).
4. Update `GetThumbnailHandler` (switch expression, remove catch blocks and infra usings).
5. Update `PhotobankModule` and `Microsoft365AdapterServiceCollectionExtensions` for DI.
6. Update both test files.
7. Run `dotnet build` and `dotnet format` across all four projects.
