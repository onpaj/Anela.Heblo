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
