## Module
UserManagement

## Finding
`IGraphService` documents that `GetGroupMembersAsync` throws `GraphServiceException` "when Microsoft Graph returns an OData error response" (`IGraphService.cs:7–10`). The contract is designed to let callers distinguish "empty result" from "external service error."

However, `GraphService.GetGroupMembersAsync` at `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:141–150` silently swallows non-success HTTP responses:

```csharp
if (!response.IsSuccessStatusCode)
{
    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
    _logger.LogError("Microsoft Graph API call failed ...", ...);
    return new List<T>();   // ← swallows the failure
}
```

When Graph returns 403, 404, 429, or 500, this path returns an empty list instead of throwing `GraphServiceException`. Consequently:

- **`GetGroupMembersHandler`** receives `{ Members: [] }` and returns `{ Success: true, Members: [] }` to the client — a 200 OK that silently misrepresents an external failure as an empty group.
- **`GraphArticleUserResolver`** (`GraphArticleUserResolver.cs:22`) calls `GetGroupMembersAsync` and maps `GraphServiceException` to `ArticleUserResolverServiceException`. That catch block is unreachable for HTTP-level failures, so the article backfill handler gets an empty user list and cannot tell it apart from a genuinely empty group.

The same silent-swallow pattern appears in `GetGroupMembersAsync` — note that `ODataError` (`GraphService.cs:174`) IS re-thrown correctly via `GraphServiceException`; it's specifically the `!response.IsSuccessStatusCode` branch at line 149 that is not.

## Why it matters
This breaks the `IGraphService` contract and violates the Liskov Substitution Principle: callers written against the documented exception contract (`GetGroupMembersHandler`, `GraphArticleUserResolver`) behave incorrectly under HTTP-level Graph failures — a real scenario (throttling, permission changes, temporary outages). The failure surfaces as empty data rather than an error, making it silent and hard to diagnose.

## Suggested fix
Replace the silent return in the non-success branch with a `GraphServiceException` throw, consistent with what the OData catch block already does:

```csharp
if (!response.IsSuccessStatusCode)
{
    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
    _logger.LogError("Microsoft Graph API call failed for groupId: {GroupId}. Status: {StatusCode} ...", ...);
    throw new GraphServiceException(
        $"Microsoft Graph returned {(int)response.StatusCode} for group {groupId}.",
        new HttpRequestException(errorContent));
}
```

The existing `catch (GraphServiceException)` in `GetGroupMembersHandler` and `GraphArticleUserResolver` will then handle this path correctly without any changes to those callers.

---
_Filed by daily arch-review routine on 2026-07-21._
