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
