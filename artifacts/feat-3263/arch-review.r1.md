# Architecture Review: Remove Infrastructure Exception Leakage from BackfillArticleRequestedByHandler

## Skip Design: false

## Architectural Fit Assessment

The violation is real and clear-cut. `BackfillArticleRequestedByHandler` (Application layer) currently imports `Microsoft.Identity.Client` directly — confirmed at line 6 of the handler — and catches `MsalException` and `Microsoft.Graph.Models.ODataErrors.ODataError` by their SDK types. `GraphArticleUserResolver` (UserManagement module, Application layer) is the adapter that should absorb these infrastructure details, but at present it is a thin pass-through that lets both exception types escape to the caller unchanged.

The fix is well-contained. `GraphArticleUserResolver` is the single call site for `IGraphService.GetGroupMembersAsync` in the Article flow. `GraphService.GetGroupMembersAsync` already re-throws `MsalException`, `ODataError`, `UnauthorizedAccessException`, and generic `Exception` after logging, so no logging is lost by wrapping at the adapter level.

The spec is accurate, achievable, and well-scoped. There are no ambiguities that would block implementation.

One nuance not fully addressed in the spec: `Microsoft.Graph` and `Microsoft.Identity.Web` are used by other Application-layer services (`GraphService`, `GraphPlannerService`, `GraphCatalogDocumentsStorage`). FR-5 (remove those PackageReferences from Application.csproj) is correct only if the new domain exceptions do not reference any Graph/MSAL types — which is true by design — but the packages will remain referenced because of those other files. The build gate in FR-5 ("if build still passes") will correctly prevent removal. This is not a spec error, just an expected outcome worth documenting explicitly.

## Proposed Architecture

### Component Overview

```
Application/Features/Article/Contracts/
  IArticleUserResolver.cs                 ← add XML <exception> docs + no type change
  ArticleUserResolverAuthException.cs     ← NEW domain exception
  ArticleUserResolverServiceException.cs  ← NEW domain exception

Application/Features/UserManagement/Infrastructure/
  GraphArticleUserResolver.cs             ← add try/catch, translate MsalException → Auth, ODataError → Service

Application/Features/Article/Admin/
  BackfillArticleRequestedByHandler.cs    ← swap catch types, remove MSAL using

Tests/Article/Admin/
  BackfillArticleRequestedByHandlerTests.cs ← swap two mock throw types
```

No new files outside the Contracts folder. No interface signature changes. No DI registration changes.

### Key Design Decisions

#### Decision 1: Exception placement — Application/Contracts vs Application/Shared
**Options considered:**
- `Application/Shared/` — alongside `ErrorCodes` and other cross-cutting application types.
- `Application/Features/Article/Contracts/` — co-located with `IArticleUserResolver`, the interface that declares the exception contract.

**Chosen approach:** `Application/Features/Article/Contracts/`

**Rationale:** These exceptions are part of the contract that `IArticleUserResolver` exposes to its consumers. They are Article-owned, not shared across the application. Placing them beside the interface makes the contract self-contained: a developer reading `IArticleUserResolver.cs` finds the XML doc referencing the exception types, and those types are in the same directory. The existing precedent in this codebase is that domain exceptions live near their owning feature contracts (`ProductMarginsException` under `Catalog/Infrastructure/Exceptions/`, `OutlookCalendarSyncException` under `Marketing/Services/`). The Contracts subfolder is the correct placement for the Article module.

#### Decision 2: Exception class shape — plain class vs record
**Options considered:**
- C# records (concise, value-equality).
- Plain classes extending `Exception`.

**Chosen approach:** Plain classes extending `Exception`.

**Rationale:** Project rule: DTOs are classes, not records (OpenAPI generator issue). More specifically, exception types must extend `Exception` and `Exception` is not a record-compatible base. The existing codebase exception pattern (`ProductMarginsException`, `OutlookCalendarSyncException`) uses plain classes with `(string message, Exception innerException)` constructors. Follow that pattern exactly.

#### Decision 3: Translation layer — GraphArticleUserResolver vs GraphService
**Options considered:**
- Translate inside `GraphService.GetGroupMembersAsync` itself — wrap the re-throws into domain exceptions.
- Translate inside `GraphArticleUserResolver.ResolveByGroupAsync` — wrap the `_graph.GetGroupMembersAsync(...)` call.

**Chosen approach:** Translate inside `GraphArticleUserResolver`.

**Rationale:** `GraphService` is owned by the UserManagement module and is used by multiple consumers (`GetGroupMembersHandler`, `GraphCatalogDocumentsStorage`, `GraphPlannerService`). Translating at `GraphService` would change the exception contract for all callers, not just the Article flow, and those callers have their own catch/handling today. `GraphArticleUserResolver` is a single-purpose adapter that bridges the UserManagement infrastructure into the Article contract — translating there keeps the change isolated to the Article dependency boundary and leaves `GraphService` unchanged.

#### Decision 4: Naming — Auth vs Msal, Service vs OData
**Options considered:**
- `MsalArticleUserResolverException` / `ODataArticleUserResolverException` — names that leak the SDK.
- `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` — names that express the semantic meaning.

**Chosen approach:** `ArticleUserResolverAuthException` / `ArticleUserResolverServiceException` (as spec'd).

**Rationale:** The entire point of the refactor is to remove SDK type references from the Application layer. Exception names that mention MSAL or OData would re-introduce the same conceptual leak even without a compile-time dependency. The semantic names (`Auth` = authentication/token failure, `Service` = upstream service error) map cleanly to the `ErrorCodes.ConfigurationError` and `ErrorCodes.ExternalServiceError` response values already used in the handler.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Article/Contracts/
  ArticleUserResolverAuthException.cs     ← new file
  ArticleUserResolverServiceException.cs  ← new file
  IArticleUserResolver.cs                 ← modified (XML docs only)

backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/
  GraphArticleUserResolver.cs             ← modified (try/catch added)

backend/src/Anela.Heblo.Application/Features/Article/Admin/
  BackfillArticleRequestedByHandler.cs    ← modified (catch types + using)

backend/test/Anela.Heblo.Tests/Article/Admin/
  BackfillArticleRequestedByHandlerTests.cs ← modified (two test methods)
```

### Interfaces and Contracts

`IArticleUserResolver` signature does not change. Add XML doc to the method:

```csharp
/// <exception cref="ArticleUserResolverAuthException">
///   Thrown when token acquisition for the directory service fails.
/// </exception>
/// <exception cref="ArticleUserResolverServiceException">
///   Thrown when the directory service returns an error response.
/// </exception>
/// <exception cref="UnauthorizedAccessException">
///   Thrown when the caller lacks permission to read the group.
/// </exception>
Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
    string groupId,
    CancellationToken cancellationToken);
```

New exception classes follow the established project pattern:

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public class ArticleUserResolverAuthException : Exception
{
    public ArticleUserResolverAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public class ArticleUserResolverServiceException : Exception
{
    public ArticleUserResolverServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

### Data Flow

**Before (broken):**
```
BackfillArticleRequestedByHandler
  → IArticleUserResolver.ResolveByGroupAsync
    → GraphArticleUserResolver (pass-through)
      → GraphService.GetGroupMembersAsync
        throws MsalException / ODataError
      ← MsalException / ODataError (unmodified)
    ← MsalException / ODataError (unmodified)
  ← handler catches SDK types directly (violation)
```

**After (fixed):**
```
BackfillArticleRequestedByHandler
  → IArticleUserResolver.ResolveByGroupAsync
    → GraphArticleUserResolver (translating adapter)
      → GraphService.GetGroupMembersAsync
        throws MsalException / ODataError
      GraphArticleUserResolver catches MsalException → throws ArticleUserResolverAuthException
      GraphArticleUserResolver catches ODataError   → throws ArticleUserResolverServiceException
    ← ArticleUserResolverAuthException / ArticleUserResolverServiceException
  ← handler catches domain types only (clean)
```

`UnauthorizedAccessException` propagates unchanged through both layers — the handler already catches it at line 53 and returns `ErrorCodes.Forbidden`. No change needed there.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `Microsoft.Graph` and `Microsoft.Identity.Web` remain in Application.csproj after FR-5 attempt | Low | Expected. Other Graph-using services (`GraphService`, `GraphPlannerService`, `GraphCatalogDocumentsStorage`) require them. The build gate in FR-5 catches this automatically. Note this in the PR description so it is not revisited. |
| `GraphService.GetGroupMembersAsync` also logs before re-throwing — double-logging if adapter wraps | Low | Acceptable duplication. The log at `GraphService` captures SDK-level detail (MSAL error code, OData code); the log at the handler captures business context (GroupId, operation). They serve different diagnostic purposes. No change needed to logging. |
| Test project references `Microsoft.Graph` transitively via `Anela.Heblo.Application` — tests for the old ODataError throw still compile after FR-6 | Low | The test project references Application which still carries `Microsoft.Graph`. The two test cases that throw `MsalUiRequiredException` and `ODataError` must be updated to throw the new domain exceptions; they would fail (wrong error code) if left unchanged even though they still compile. The test build gate enforces correctness. |
| Handler's `catch (Exception)` block at line 59 remains a safety net for truly unexpected errors | None | No change required. The four catch blocks in the handler — `Auth`, `Service`, `Unauthorized`, `Exception` — remain structurally identical; only two type names change. |

## Specification Amendments

**Amendment 1 — FR-5 expected outcome:** The spec says "Remove the two PackageReferences if build still passes." Clarify in the implementation PR that the build will NOT pass without those references because `GraphService`, `GraphPlannerService`, and `GraphCatalogDocumentsStorage` all use `Microsoft.Graph` and/or `Microsoft.Identity.Web` types. FR-5 is effectively a verification step, not a removal step. The meaningful metric is that `BackfillArticleRequestedByHandler.cs` no longer imports `Microsoft.Identity.Client` — that `using` is the actual artifact of the dependency violation.

**Amendment 2 — Test file has no `using Microsoft.Graph.Models.ODataErrors;` at top-level but constructs `ODataError` inline.** When updating `BackfillArticleRequestedByHandlerTests.cs`, remove the `using Microsoft.Identity.Client;` at line 7 and the `ODataError` construction once those are replaced. Confirm the test file compiles cleanly without those namespaces.

## Prerequisites

- No migration changes.
- No DI registration changes.
- No other PR or branch changes in flight that touch `GraphArticleUserResolver`, `IArticleUserResolver`, or `BackfillArticleRequestedByHandler`.
- Validate: `dotnet build` + `dotnet test --filter "FullyQualifiedName~BackfillArticleRequestedByHandlerTests"` must pass before closing.
