## Module
UserManagement

## Finding
`GraphService.GetAppRoleMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`, lines 386–404) resolves display name and email for directly-assigned users with a separate HTTP call per user:

```csharp
foreach (var userId in directUserIds)
{
    var userUrl = $"https://graph.microsoft.com/v1.0/users/{userId}?$select=id,displayName,mail,userPrincipalName";
    using var userRequest = new HttpRequestMessage(HttpMethod.Get, userUrl);
    userRequest.Headers.Authorization = ...;
    var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
    // parse one user
}
```

With N directly-assigned users this produces N sequential HTTP round-trips to Microsoft Graph, all blocking the request.

## Why it matters
Microsoft Graph supports batching multiple requests in a single HTTP call (`POST /v1.0/$batch`, up to 20 sub-requests per batch). For a role with, say, 10 directly-assigned users, the current code makes 10 sequential Graph calls where 1 batch call would suffice. Since these calls are also not parallelised (`await` in a `foreach`), they stack latency linearly.

## Suggested fix
Replace the per-user resolution loop with Graph `$batch` requests (groups of up to 20 per batch call). Each batch sub-request hits `GET /users/{id}?$select=id,displayName,mail,userPrincipalName`. This reduces N sequential calls to ⌈N/20⌉ parallel batch calls.

Alternatively, if the app role always has a small, bounded membership (< 5 users), parallelising the existing calls with `Task.WhenAll` is a simpler incremental improvement while the batch approach is implemented.

---
_Filed by daily arch-review routine on 2026-06-22._
