# Task Plan: Remove Infrastructure Exception Leakage from BackfillArticleRequestedByHandler

**Feature:** feat-3263
**Type:** Pure backend refactoring — no UI changes.

**Goal:** Replace the `MsalException` and `Microsoft.Graph.Models.ODataErrors.ODataError` catches in `BackfillArticleRequestedByHandler` (an Application-layer class) with Article-owned domain exceptions. The infrastructure-specific exception types must be caught and re-wrapped inside `GraphArticleUserResolver` (the adapter that lives in the UserManagement infrastructure layer), which is the only place where infrastructure packages are legitimately visible.

---

### task: create-domain-exceptions

**What and why:** Create two new Article-module exception classes in the `Contracts` folder alongside `IArticleUserResolver`. These are the clean Application-layer exception types that `BackfillArticleRequestedByHandler` will catch instead of the SDK-specific ones.

**Files to create:**

`backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverAuthException.cs`

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Thrown by <see cref="IArticleUserResolver"/> implementations when token acquisition
/// or authentication for the underlying identity provider fails.
/// Wraps infrastructure-specific auth exceptions (e.g. MsalException) so that
/// Application-layer consumers remain decoupled from SDK packages.
/// </summary>
public sealed class ArticleUserResolverAuthException : Exception
{
    public ArticleUserResolverAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

`backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverServiceException.cs`

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Thrown by <see cref="IArticleUserResolver"/> implementations when the remote
/// directory service returns an error response (e.g. an OData error from Microsoft Graph).
/// Wraps infrastructure-specific service exceptions so that Application-layer consumers
/// remain decoupled from SDK packages.
/// </summary>
public sealed class ArticleUserResolverServiceException : Exception
{
    public ArticleUserResolverServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

**Verify build:**

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet build
```

Build must succeed with zero errors before committing.

**Commit:** `refactor(article): add ArticleUserResolverAuthException and ArticleUserResolverServiceException`

---

### task: update-interface-and-adapter

**What and why:** Add XML `<exception>` doc tags to `IArticleUserResolver.ResolveByGroupAsync` so callers know which exceptions to expect. Then add a `try/catch` block inside `GraphArticleUserResolver.ResolveByGroupAsync` to catch the two infrastructure-specific exception types and re-throw them as the new Article-domain exceptions.

**File to modify (XML doc):**

`backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs`

Replace the current interface method signature block with:

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction for resolving the set of users associated
/// with a directory group (used by the RequestedBy backfill admin command).
/// Implemented by the UserManagement module via an adapter.
/// </summary>
public interface IArticleUserResolver
{
    /// <summary>
    /// Resolves the members of the given directory group.
    /// </summary>
    /// <param name="groupId">The group identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matched users.</returns>
    /// <exception cref="ArticleUserResolverAuthException">
    /// Thrown when token acquisition or authentication fails.
    /// </exception>
    /// <exception cref="ArticleUserResolverServiceException">
    /// Thrown when the remote directory service returns an error response.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the caller lacks permission to read the group.
    /// </exception>
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken);
}

public sealed record ArticleUserMatch(string Id, string DisplayName);
```

**File to modify (try/catch):**

`backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`

Replace the entire file content with:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Identity.Client;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class GraphArticleUserResolver : IArticleUserResolver
{
    private readonly IGraphService _graph;

    public GraphArticleUserResolver(IGraphService graph)
    {
        _graph = graph;
    }

    public async Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var members = await _graph.GetGroupMembersAsync(groupId, cancellationToken);
            return members
                .Select(m => new ArticleUserMatch(m.Id, m.DisplayName))
                .ToList();
        }
        catch (MsalException ex)
        {
            throw new ArticleUserResolverAuthException(
                $"Token acquisition failed for group {groupId}.", ex);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            throw new ArticleUserResolverServiceException(
                $"Graph OData error for group {groupId}.", ex);
        }
        // UnauthorizedAccessException and Exception propagate as-is;
        // BackfillArticleRequestedByHandler already catches both.
    }
}
```

**Verify build:**

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet build
```

Build must succeed with zero errors before committing.

**Commit:** `refactor(article): wrap infrastructure exceptions in GraphArticleUserResolver`

---

### task: fix-handler-and-tests

**What and why:** Remove the `Microsoft.Identity.Client` using from `BackfillArticleRequestedByHandler` and swap its two infrastructure-specific catch blocks to use the new Article-domain exceptions. Then update the two corresponding test methods to throw the new domain exceptions instead of the SDK types, and remove the `using Microsoft.Identity.Client` from the test file.

**Step 1 — Update the handler (run tests first to confirm they fail, then fix).**

Before editing, run the test suite to establish the failing baseline:

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet test --filter "FullyQualifiedName~BackfillArticleRequestedBy"
```

Expected: `Handle_WhenResolverThrowsMsalException_ReturnsConfigurationError` and `Handle_WhenResolverThrowsODataError_ReturnsExternalServiceError` fail (or the entire suite fails to compile once the test usings are removed). This confirms the tests drive the implementation.

**File to modify:**

`backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`

- Remove the line `using Microsoft.Identity.Client;`
- Replace the `catch (MsalException ex)` block with `catch (ArticleUserResolverAuthException ex)`
- Replace the `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` block with `catch (ArticleUserResolverServiceException ex)`
- Keep the log messages and return values unchanged.

Complete updated using block and catch section:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;
```

Complete updated catch section (replace lines 43–62 in the original):

```csharp
        catch (ArticleUserResolverAuthException ex)
        {
            _logger.LogError(ex, "Graph token acquisition failed for backfill of group {GroupId}", request.GroupId);
            return new BackfillArticleRequestedByResponse(ErrorCodes.ConfigurationError);
        }
        catch (ArticleUserResolverServiceException ex)
        {
            _logger.LogError(ex, "Graph OData error during backfill for group {GroupId}", request.GroupId);
            return new BackfillArticleRequestedByResponse(ErrorCodes.ExternalServiceError);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Graph access denied during backfill for group {GroupId}", request.GroupId);
            return new BackfillArticleRequestedByResponse(ErrorCodes.Forbidden);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during backfill for group {GroupId}", request.GroupId);
            return new BackfillArticleRequestedByResponse(ErrorCodes.InternalServerError);
        }
```

**Step 2 — Update the tests.**

`backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs`

- Remove the line `using Microsoft.Identity.Client;`
- Replace the body of `Handle_WhenResolverThrowsMsalException_ReturnsConfigurationError` mock setup with `ArticleUserResolverAuthException`:

```csharp
    [Fact]
    public async Task Handle_WhenResolverThrowsMsalException_ReturnsConfigurationError()
    {
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArticleUserResolverAuthException(
                "token failed",
                new Exception("inner")));

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
    }
```

- Replace the body of `Handle_WhenResolverThrowsODataError_ReturnsExternalServiceError` mock setup with `ArticleUserResolverServiceException`:

```csharp
    [Fact]
    public async Task Handle_WhenResolverThrowsODataError_ReturnsExternalServiceError()
    {
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArticleUserResolverServiceException(
                "odata error",
                new Exception("inner")));

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ExternalServiceError);
    }
```

**Step 3 — Attempt to remove packages from Application.csproj.**

Open `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` and attempt to remove both:

```xml
<PackageReference Include="Microsoft.Graph" Version="5.92.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

Then run:

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet build
```

**Expected outcome:** Build will fail. These packages are referenced by `GraphService.cs`, `GraphPlannerService.cs`, `GraphOneDriveService.cs`, `GraphCatalogDocumentsStorage.cs`, and `UserManagementModule.cs` — all of which remain in the Application project. Restore both `PackageReference` lines and add a comment:

```xml
<!-- Microsoft.Graph and Microsoft.Identity.Web are still required by
     GraphService, GraphPlannerService, GraphOneDriveService, and
     GraphCatalogDocumentsStorage. Remove only after those services are
     moved to infrastructure adapters (out of scope for feat-3263). -->
<PackageReference Include="Microsoft.Graph" Version="5.92.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="3.14.1" />
```

**Step 4 — Run full targeted test suite.**

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet test --filter "FullyQualifiedName~BackfillArticleRequestedBy"
```

All tests must pass (11 tests total: the two updated ones plus the 9 unchanged ones).

**Step 5 — Final build check.**

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet build
```

Zero errors and zero warnings added by this change.

**Commit:** `refactor(article): remove infrastructure exception leakage from BackfillArticleRequestedByHandler`
