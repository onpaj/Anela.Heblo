# Specification: Remove Infrastructure Exception Leakage from BackfillArticleRequestedByHandler

## Summary

`BackfillArticleRequestedByHandler` (Application layer) directly catches `MsalException` and `ODataError` — types from `Microsoft.Identity.Client` and the Graph SDK — which violates the Clean Architecture dependency rule and forces the Application project to carry `Microsoft.Graph` and `Microsoft.Identity.Web` package references. This feature replaces those two infrastructure catches with catches of two new domain exceptions defined inside the Application layer, and moves the translation of MSAL/OData exceptions to the adapter (`GraphArticleUserResolver`) where infrastructure types are already permitted.

## Background

The Application project (`Anela.Heblo.Application`) currently references `Microsoft.Graph` 5.92.0 and `Microsoft.Identity.Web` 3.14.1 solely to support the two `catch` blocks in `BackfillArticleRequestedByHandler`. The abstraction `IArticleUserResolver` is meant to hide the identity-provider implementation, but because its exception contract is undocumented the MSAL and OData exceptions propagate freely through `GraphArticleUserResolver.ResolveByGroupAsync` all the way up to the handler.

The existing exception-class pattern in the codebase (`OutlookCalendarSyncException` in Application, `GridLayoutPersistenceException` in Domain) confirms that simple classes extending `Exception` are the accepted mechanism for communicating infrastructure failures upward without leaking SDK types.

Tests in `BackfillArticleRequestedByHandlerTests.cs` also import `Microsoft.Identity.Client` and `Microsoft.Graph.Models.ODataErrors` to construct test doubles; these usages disappear once the handler no longer catches those types.

## Functional Requirements

### FR-1: Define two domain exception types in the Application layer

Create two exception classes inside `Anela.Heblo.Application`:

- `ArticleUserResolverAuthException` — raised when token acquisition fails (i.e., MSAL configuration is wrong or a token cannot be obtained). Maps to `ErrorCodes.ConfigurationError` in the handler.
- `ArticleUserResolverServiceException` — raised when the upstream directory service returns an error response (i.e., OData/Graph API error). Maps to `ErrorCodes.ExternalServiceError` in the handler.

Both classes extend `Exception`. Both accept a `string message` and an `Exception innerException` constructor parameter so the original SDK exception is preserved for logging.

**Suggested location:** `backend/src/Anela.Heblo.Application/Features/Article/Contracts/` alongside `IArticleUserResolver.cs`, since they form the contract of that interface.

**Acceptance criteria:**
- `ArticleUserResolverAuthException` compiles with no reference to `Microsoft.Identity.Client` or `Microsoft.Graph`.
- `ArticleUserResolverServiceException` compiles with no reference to `Microsoft.Identity.Client` or `Microsoft.Graph`.
- Both classes carry the inner exception from the infrastructure layer.

### FR-2: Update IArticleUserResolver XML doc to declare the exception contract

Add an XML `<exception>` doc comment to `ResolveByGroupAsync` in `IArticleUserResolver.cs` stating that `ArticleUserResolverAuthException` is thrown on token failure and `ArticleUserResolverServiceException` is thrown on upstream service error. `UnauthorizedAccessException` (BCL) remains part of the contract and should also be documented.

**Acceptance criteria:**
- The interface file lists all three throwable exception types in XML doc comments.
- No SDK-specific types are referenced in the interface file.

### FR-3: Translate infrastructure exceptions in GraphArticleUserResolver

In `GraphArticleUserResolver.ResolveByGroupAsync`, wrap the call to `_graph.GetGroupMembersAsync(...)` in a try/catch that:

- Catches `MsalException` → wraps and re-throws as `ArticleUserResolverAuthException`.
- Catches `Microsoft.Graph.Models.ODataErrors.ODataError` → wraps and re-throws as `ArticleUserResolverServiceException`.
- Does **not** catch `UnauthorizedAccessException` (let it propagate as-is; the handler already handles it as a BCL type).
- Does **not** catch the generic `Exception` (let unexpected failures propagate to the handler's existing generic catch).

`GraphService.GetGroupMembersAsync` already re-throws all three exception types (MSAL, OData, Unauthorized) so they will reliably arrive at `GraphArticleUserResolver`.

**Acceptance criteria:**
- `GraphArticleUserResolver.ResolveByGroupAsync` has explicit catch blocks for `MsalException` and `ODataError`.
- Each catch wraps the original exception as `innerException` in the new domain exception.
- After the change, `MsalException` and `ODataError` cannot escape `GraphArticleUserResolver`.

### FR-4: Update BackfillArticleRequestedByHandler to catch only domain exceptions

Replace the two infrastructure catches in `BackfillArticleRequestedByHandler.Handle(...)`:

- Remove `catch (MsalException ex)` → replace with `catch (ArticleUserResolverAuthException ex)`.
- Remove `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` → replace with `catch (ArticleUserResolverServiceException ex)`.
- Remove the `using Microsoft.Identity.Client;` directive.
- The `UnauthorizedAccessException` catch and the generic `Exception` catch remain unchanged.

Log messages in both new catches should be identical in content to the original ones so that existing log-monitoring queries continue to work.

**Acceptance criteria:**
- `BackfillArticleRequestedByHandler.cs` has no `using` directive for `Microsoft.Identity.Client` or `Microsoft.Graph`.
- The handler returns `ErrorCodes.ConfigurationError` on `ArticleUserResolverAuthException`.
- The handler returns `ErrorCodes.ExternalServiceError` on `ArticleUserResolverServiceException`.
- Handler behaviour for all other exception types is unchanged.

### FR-5: Remove Microsoft.Graph and Microsoft.Identity.Web from Application project

After FR-1–FR-4 are complete, remove from `Anela.Heblo.Application.csproj`:

```xml
<PackageReference Include="Microsoft.Graph" Version="5.92.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

Verify that the project still builds after removal (these packages may be transitive dependencies via other paths; if so, the build error will make that visible and the reference should be retained with a comment explaining why).

**Acceptance criteria:**
- `dotnet build` succeeds after the package references are removed (or a comment explains why a reference must remain).
- No remaining `using Microsoft.Identity.Client` or `using Microsoft.Graph` directives exist in the Application project except inside `Features/UserManagement/` (which is the legitimate infrastructure-adjacent slice).

### FR-6: Update BackfillArticleRequestedByHandlerTests

In `BackfillArticleRequestedByHandlerTests.cs`:

- Remove `using Microsoft.Identity.Client;` and any `using Microsoft.Graph...` directives.
- Replace the test `Handle_WhenResolverThrowsMsalException_ReturnsConfigurationError` so that the mock throws `ArticleUserResolverAuthException` instead of `MsalUiRequiredException`.
- Replace the test `Handle_WhenResolverThrowsODataError_ReturnsExternalServiceError` so that the mock throws `ArticleUserResolverServiceException` instead of `Microsoft.Graph.Models.ODataErrors.ODataError`.
- All other tests remain unchanged.

**Acceptance criteria:**
- Test project compiles with no reference to `Microsoft.Identity.Client` or `Microsoft.Graph` types (unless those are already used by other test classes, which would be a separate concern).
- Both updated tests pass.
- Total test count for the file is unchanged.

## Non-Functional Requirements

### NFR-1: Behaviour Equivalence

The observable runtime behaviour of `BackfillArticleRequestedByHandler` must be identical before and after the change. The same `ErrorCodes` values must be returned for the same failure conditions. Log messages must be equivalent so that existing alerting is unaffected.

### NFR-2: Inner Exception Preservation

The original `MsalException` / `ODataError` instance must be stored as `InnerException` on the wrapping domain exception so that full SDK stack traces are available in logs and APM tooling. Do not swallow or discard the original exception.

### NFR-3: Layering Compliance

After this change, no file under `Anela.Heblo.Application/Features/Article/` may contain a `using` directive for `Microsoft.Identity.Client` or `Microsoft.Graph.*`. This can be confirmed with a simple `grep`.

### NFR-4: Build and Test

`dotnet build` and `dotnet format` (backend) must pass clean. All existing unit tests must pass.

## Data Model

No database schema changes. No new domain entities. The only new types are two exception classes.

| Type | Namespace | Base |
|---|---|---|
| `ArticleUserResolverAuthException` | `Anela.Heblo.Application.Features.Article.Contracts` | `Exception` |
| `ArticleUserResolverServiceException` | `Anela.Heblo.Application.Features.Article.Contracts` | `Exception` |

## API / Interface Design

No API surface changes. No new endpoints. No changes to request/response DTOs or `ErrorCodes` values. The existing `ErrorCodes.ConfigurationError` and `ErrorCodes.ExternalServiceError` enum members are reused as-is.

The updated `IArticleUserResolver` interface (documentation-only change):

```csharp
/// <exception cref="ArticleUserResolverAuthException">
/// Thrown when token acquisition fails (misconfigured credentials or expired app secret).
/// </exception>
/// <exception cref="ArticleUserResolverServiceException">
/// Thrown when the upstream directory service returns an error response.
/// </exception>
/// <exception cref="UnauthorizedAccessException">
/// Thrown when the application lacks the required directory permissions.
/// </exception>
Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
    string groupId,
    CancellationToken cancellationToken);
```

## Dependencies

- `GraphArticleUserResolver` lives at `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`. It already references `Microsoft.Identity.Client` (transitively via `GraphService`) — this is acceptable because it is in the `UserManagement.Infrastructure` sub-namespace.
- `GraphService.GetGroupMembersAsync` already re-throws `MsalException`, `ODataError`, and `UnauthorizedAccessException` — no changes needed there.
- `Microsoft.Graph` and `Microsoft.Identity.Web` remain as package references in the Application project until FR-5 confirms they can be removed. If they are pulled in transitively by another feature's legitimate dependency (e.g. `Adapters.Microsoft365`), the Application project reference may be unnecessary but harmless; the important change is removing the Application-layer *catch* blocks, not necessarily the package references.

## Out of Scope

- Refactoring `GraphService` exception handling strategy (logs and re-throws — this is acceptable and unrelated to the violation).
- Adding retry logic or circuit-breaker policy to `IArticleUserResolver`.
- Changing the `ErrorCodes` enum or adding Article-module-specific error codes (2408+) for these failures. The existing general codes are sufficient.
- Moving `GraphArticleUserResolver` out of the Application project into a separate Adapters project — this is a larger structural change and is not required to fix the violation.
- Any changes to other callers of `IGraphService` that are not part of the Article backfill flow.

## Open Questions

None.

## Status: COMPLETE
