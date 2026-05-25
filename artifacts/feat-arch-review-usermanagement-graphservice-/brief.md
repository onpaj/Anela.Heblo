## Module
UserManagement

## Finding
`GraphService.GetGroupMembersAsync` catches every possible exception — `MsalException`, `ODataError`, `UnauthorizedAccessException`, and a final catch-all `Exception` — and returns an empty `List<UserDto>` in every failure path, without re-throwing (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`, lines 72–185).

Because `GetGroupMembersAsync` never throws, the try/catch in `GetGroupMembersHandler.Handle` (lines 19–43) can never reach its `catch` branch. The handler therefore always constructs `GetGroupMembersResponse { Success = true }` — even when the Graph API is down, the token cannot be acquired, or the group does not exist.

The result is that:
- The `Success` field in `GetGroupMembersResponse` is effectively dead code — it will never be `false` at runtime.
- Callers (the controller and the frontend) cannot distinguish "the group has no members" from "the Graph call failed."
- The frontend's `success: true` check provides no actual signal, and users see an empty dropdown with no indication of a backend failure.

## Why it matters
This is a violation of the "fail fast" principle and breaks the contract expressed by the `Success` field. Silent failures are harder to diagnose in production and mask real errors (expired credentials, misconfigured group IDs, Graph API outages). The existing `Success`/`ErrorCode` response shape was designed to surface these failures — it just never gets populated.

## Suggested fix
Remove the catch-all swallowing in `GraphService` and let exceptions propagate to the handler, which already has the right structure to set `Success = false`. Keep only the catches that represent expected, non-exceptional conditions (e.g. an empty group), and re-throw everything else:

```csharp
// GraphService: only handle the "group not found" gracefully — let auth failures throw
if (!response.IsSuccessStatusCode)
{
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    _logger.LogError("Graph API failed: {Status} {Content}", response.StatusCode, content);
    throw new ExternalServiceException($"Microsoft Graph returned {response.StatusCode}");
}
```

The handler's existing `catch (Exception ex)` block will then correctly set `Success = false` and a meaningful `ErrorCode`, which the frontend can surface to the user.

---
_Filed by daily arch-review routine on 2026-05-24._