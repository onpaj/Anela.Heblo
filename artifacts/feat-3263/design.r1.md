# Design: Remove Infrastructure Exception Leakage from BackfillArticleRequestedByHandler

## Component Design

### New: Domain Exception Types
**Location:** `Application/Features/Article/Contracts/ArticleUserResolverAuthException.cs`, `Application/Features/Article/Contracts/ArticleUserResolverServiceException.cs`

**Responsibility:** Represent failure modes of `IArticleUserResolver` in domain terms, decoupled from any SDK. Both are plain `Exception` subclasses with a single constructor `(string message, Exception innerException)`.

**Interface:**
```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public class ArticleUserResolverAuthException : Exception
{
    public ArticleUserResolverAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}

public class ArticleUserResolverServiceException : Exception
{
    public ArticleUserResolverServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

---

### Modified: `IArticleUserResolver`
**Location:** `Application/Features/Article/Contracts/IArticleUserResolver.cs`

**Change:** Add XML doc to `ResolveByGroupAsync` declaring the two new exceptions as part of the contract.

```csharp
/// <summary>
/// Resolves article users belonging to the specified group.
/// </summary>
/// <exception cref="ArticleUserResolverAuthException">
///   Thrown when token acquisition for the underlying service fails.
/// </exception>
/// <exception cref="ArticleUserResolverServiceException">
///   Thrown when the underlying service returns an error response.
/// </exception>
Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct);
```

---

### Modified: `GraphArticleUserResolver` (Infrastructure adapter)
**Location:** `Infrastructure/…/GraphArticleUserResolver.cs`

**Responsibility:** Sole site of SDK-to-domain exception translation. Catches `MsalException` and `ODataError` from their respective SDK namespaces and re-throws as the domain exceptions defined above. `UnauthorizedAccessException` is not caught here and propagates unchanged.

**Translated try/catch block (wraps the Graph call only):**
```csharp
try
{
    var members = await _graph.GetGroupMembersAsync(groupId, ct);
    return members.Select(m => new ArticleUserMatch(m.Id, m.DisplayName)).ToList();
}
catch (MsalException ex)
{
    throw new ArticleUserResolverAuthException("Graph token acquisition failed.", ex);
}
catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
{
    throw new ArticleUserResolverServiceException("Graph OData error.", ex);
}
```

No other catch blocks are added. No other class performs this translation.

---

### Modified: `BackfillArticleRequestedByHandler`
**Location:** `Application/Features/Article/Commands/BackfillArticleRequestedBy/BackfillArticleRequestedByHandler.cs`

**Changes:**
- Replace `catch (MsalException ex)` with `catch (ArticleUserResolverAuthException ex)`
- Replace `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` with `catch (ArticleUserResolverServiceException ex)`
- Remove `using Microsoft.Identity.Client;`

The handler logic inside each catch block remains unchanged (same log level, same error propagation/return). The handler has no remaining direct dependency on infrastructure SDK namespaces after this change.

---

### Modified: `BackfillArticleRequestedByHandlerTests`
**Location:** `Application.Tests/Features/Article/Commands/BackfillArticleRequestedBy/BackfillArticleRequestedByHandlerTests.cs`

**Changes:**
- Mock setup that previously threw `MsalUiRequiredException` now throws `new ArticleUserResolverAuthException("...", new Exception())`
- Mock setup that previously threw `new Microsoft.Graph.Models.ODataErrors.ODataError()` now throws `new ArticleUserResolverServiceException("...", new Exception())`

Test assertions and expected handler outcomes are unchanged.

---

### Attempted (expected no-op): `Application.csproj` dependency removal
**Location:** `src/Application/Application.csproj`

Attempt removal of `Microsoft.Graph` and `Microsoft.Identity.Web` package references. Expected to fail because other services in the Application layer depend on them. No action taken if references cannot be removed cleanly.

---

## Component Boundaries Summary

| Layer | Component | SDK dependency after change |
|---|---|---|
| Application | `ArticleUserResolverAuthException` | None |
| Application | `ArticleUserResolverServiceException` | None |
| Application | `IArticleUserResolver` | None |
| Application | `BackfillArticleRequestedByHandler` | None (SDK usings removed) |
| Infrastructure | `GraphArticleUserResolver` | `MsalException`, `ODataError` (translation point) |

---

## Data Schemas

No database schema changes. No API request/response shape changes. No new DTOs. No DI registration changes.
