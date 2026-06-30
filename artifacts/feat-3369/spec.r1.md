# Specification: UserManagement Application Layer — SDK Exception Decoupling

## Summary

`GetGroupMembersHandler` directly catches `MsalException` and `Microsoft.Graph.Models.ODataErrors.ODataError` from external SDK packages, violating the Clean Architecture dependency rule that prohibits the Application layer from referencing infrastructure types. This specification defines the work to introduce application-level exception wrappers for the UserManagement service boundary, bring `GraphService` in line with the existing wrapping pattern, and remove the SDK dependencies from the Application project.

## Background

The codebase already establishes the correct pattern for this problem: `ArticleUserResolverAuthException` and `ArticleUserResolverServiceException` in `Application/Features/Article/Contracts/` wrap `MsalException` and `ODataError` respectively, so that `IArticleUserResolver` consumers in the Application layer never see SDK types. `GraphArticleUserResolver` (in `UserManagement/Infrastructure/`) correctly catches raw exceptions and rethrows as those typed wrappers before they surface.

`GetGroupMembersHandler` bypasses this pattern entirely. It imports `Microsoft.Identity.Client` directly (line 6) and catches `MsalException` (line 35) and `ODataError` (line 46). Consequently, `Anela.Heblo.Application.csproj` carries `Microsoft.Graph` and `Microsoft.Identity.Web` as direct `PackageReference` entries (lines 25–26), embedding a hard infrastructure dependency into the Application project.

The practical consequences are:
- A change to MSAL's exception hierarchy or the Graph SDK's OData error model forces a change in `GetGroupMembersHandler` — the wrong layer owns the concern.
- Unit-testing the handler requires either live MSAL infrastructure or careful ceremony around SDK exception construction.
- The `Application_types_should_not_reference_AspNetCore_namespaces` and `ModuleBoundariesTests` architecture tests do not currently catch SDK-namespace violations; this leak is invisible to the automated boundary guard.

## Functional Requirements

### FR-1: Define application-level exception types for the UserManagement service boundary

Create two sealed exception classes in `backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/`:

- `GraphServiceAuthException` — thrown when token acquisition (MSAL) fails.
- `GraphServiceException` — thrown when the remote Graph service returns an error response (OData error).

Both classes must follow the same shape as `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException`: a single constructor taking `(string message, Exception innerException)`, with XML-doc comments explaining what infrastructure condition each wraps.

**Acceptance criteria:**
- Both files exist under `UserManagement/Contracts/` with the `Anela.Heblo.Application.Features.UserManagement.Contracts` namespace.
- Neither class references any `Microsoft.Identity.*` or `Microsoft.Graph.*` type.
- XML-doc comments name the infrastructure exception being wrapped and reference `IGraphService`.

### FR-2: Update `IGraphService` XML documentation to declare the exception contract

Add `<exception>` XML-doc tags to `IGraphService.GetGroupMembersAsync` (and optionally `SearchUsersAsync` / `GetAppRoleMembersAsync` if they surface auth errors upward in the future) declaring that implementations may throw `GraphServiceAuthException` and `GraphServiceException`.

**Acceptance criteria:**
- `IGraphService.GetGroupMembersAsync` carries `/// <exception cref="GraphServiceAuthException">` and `/// <exception cref="GraphServiceException">` doc tags.
- No SDK types are referenced in the interface file.

### FR-3: Update `GraphService.GetGroupMembersAsync` to wrap and rethrow as application-level exceptions

In `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs`, replace the `throw;` in the `catch (MsalException)` and `catch (ODataError)` blocks inside `GetGroupMembersAsync` with:

```csharp
catch (MsalException msalEx)
{
    _logger.LogError(...);
    throw new GraphServiceAuthException(
        $"Token acquisition failed for group {groupId}.", msalEx);
}
catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
{
    _logger.LogError(...);
    throw new GraphServiceException(
        $"Graph OData error fetching members for group {groupId}.", odataEx);
}
```

The existing log statements must be preserved verbatim; only the `throw;` lines change.

**Acceptance criteria:**
- `GraphService.GetGroupMembersAsync` no longer re-throws raw `MsalException` or `ODataError` as unhandled.
- Both catch blocks rethrow the corresponding application-level wrapper type.
- `UnauthorizedAccessException` and the general `Exception` catch blocks are left unchanged (they still re-throw as-is; the handler already has a catch for `UnauthorizedAccessException`).

### FR-4: Update `GetGroupMembersHandler` to catch only application-level exception types

In `GetGroupMembersHandler.cs`:

- Remove `using Microsoft.Identity.Client;` (line 6).
- Replace the `catch (MsalException ex)` block with `catch (GraphServiceAuthException ex)`.
- Replace the `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` block with `catch (GraphServiceException ex)`.
- The handler body for each catch block (log + return `GetGroupMembersResponse` with the matching `ErrorCode`) must remain functionally identical.

**Acceptance criteria:**
- `GetGroupMembersHandler.cs` contains no `using` directives for `Microsoft.Identity.Client` or `Microsoft.Graph`.
- The handler catches `GraphServiceAuthException` and maps it to `ErrorCodes.ConfigurationError`, matching the previous `MsalException` handling.
- The handler catches `GraphServiceException` and maps it to `ErrorCodes.ExternalServiceError`, matching the previous `ODataError` handling.
- All other catch blocks (`UnauthorizedAccessException`, `Exception`) remain unchanged.

### FR-5: Remove SDK package references from the Application project

In `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`, remove:

```xml
<PackageReference Include="Microsoft.Graph" Version="5.92.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

Confirm that no other file under the Application project imports types from those packages before removing them.

**Acceptance criteria:**
- `Anela.Heblo.Application.csproj` no longer references `Microsoft.Graph` or `Microsoft.Identity.Web`.
- `dotnet build` succeeds for the full solution after the removals.

### FR-6: Add an architecture test enforcing the Application layer has no SDK exception references

Extend `ModuleBoundariesTests.cs` (or add a dedicated `[Fact]` in that file) with a test that asserts no type under `Anela.Heblo.Application` references `Microsoft.Identity.*` or `Microsoft.Graph.*` namespaces, following the same pattern as `Application_types_should_not_reference_AspNetCore_namespaces`.

**Acceptance criteria:**
- A new `[Fact]` named `Application_types_should_not_reference_Microsoft_SDK_namespaces` (or similar) exists in `ModuleBoundariesTests.cs`.
- The test uses `EnumerateReferencedTypes` with forbidden prefixes `["Microsoft.Identity", "Microsoft.Graph"]`.
- The test passes after FR-1 through FR-5 are applied and fails if the handler is reverted.

## Non-Functional Requirements

### NFR-1: No behavioural change at runtime

The error-handling semantics visible to callers of `GetGroupMembersHandler` must remain byte-for-byte identical. The same `ErrorCode` values are returned for auth failures and Graph service errors. Log messages at the `LogError` level must be preserved.

### NFR-2: Testability

After this change, unit tests for `GetGroupMembersHandler` can throw `GraphServiceAuthException` or `GraphServiceException` from a mock `IGraphService` without any MSAL or Graph SDK dependency in the test project.

### NFR-3: Build integrity

`dotnet build` and `dotnet format` must pass for the entire solution after the changes. The Application project must not carry `Microsoft.Graph` or `Microsoft.Identity.Web` in its transitive closure as a direct `PackageReference`.

## Data Model

No data model changes. All types involved are exception classes and existing DTOs.

Key types after the change:

| Type | Location | Responsibility |
|---|---|---|
| `GraphServiceAuthException` | `Application/Features/UserManagement/Contracts/` | Application-level wrapper for MSAL auth failure |
| `GraphServiceException` | `Application/Features/UserManagement/Contracts/` | Application-level wrapper for Graph OData errors |
| `IGraphService` | `Application/Features/UserManagement/Services/` | Service contract; declares exception surface in XML docs |
| `GraphService` | `Adapters/Microsoft365/UserManagement/` | Adapter; catches SDK exceptions, rethrows as application types |
| `GetGroupMembersHandler` | `Application/Features/UserManagement/UseCases/GetGroupMembers/` | Handler; catches only application-level exceptions |

## API / Interface Design

No changes to HTTP endpoints, request/response contracts, or OpenAPI schema. This is a purely internal refactor within the backend.

The only public surface change is the documented exception contract on `IGraphService.GetGroupMembersAsync` (XML docs).

## Dependencies

- Existing `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` in `Application/Features/Article/Contracts/` — reference implementation to mirror.
- `ModuleBoundariesTests.cs` architecture test infrastructure — `EnumerateReferencedTypes` helper is reused for FR-6.
- `GraphService` adapter in `Adapters/Anela.Heblo.Adapters.Microsoft365/` — must continue to have `Microsoft.Graph` and `Microsoft.Identity.Web` as its own package references (unchanged; the violation is the Application project's direct reference, not the adapter's).

## Out of Scope

- `SearchUsersAsync` and `GetAppRoleMembersAsync` in `GraphService`: both currently suppress exceptions internally (return empty list on catch) and never surface SDK types to the Application layer. Wrapping their exception handling is not required by this spec.
- `GraphArticleUserResolver`: already correct; no changes needed.
- Any changes to frontend code, database, or deployment configuration.
- Adding handler-level unit tests (the spec enables testability; authoring the tests is a separate task).
- Audit of other modules for similar SDK leaks (out of scope; file as separate arch-review issues).

## Open Questions

None.

## Status: COMPLETE
