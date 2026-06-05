## Module
UserManagement

## Finding
`GraphService.GetGroupMembersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs:50-193`) catches every exception class — `MsalException`, `ODataError`, `UnauthorizedAccessException`, and the outer `Exception` — and returns `new List<UserDto>()` in each branch. The method never propagates an exception to its caller.

`GetGroupMembersHandler` (`backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs:18-43`) wraps the service call in its own `try/catch` and sets `Success = false` on error:

```csharp
catch (Exception ex)
{
    // This branch is unreachable for any Graph API error
    return new GetGroupMembersResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.InternalServerError,
        Members = new List<UserDto>()
    };
}
```

Because `GraphService` already absorbs every failure, the handler's catch fires only for entirely unexpected exceptions (e.g. `NullReferenceException` in response assembly). The `Success = false` path that signals a failure to API consumers is therefore effectively dead for all real-world Graph API errors.

A secondary consequence: a caller receiving `Success = true, Members = []` cannot distinguish "the group is genuinely empty" from "the Graph API call failed". The `GetGroupMembersHandlerTests.Handle_WhenGraphServiceThrowsException_ReturnsFailureResponse` test covers the handler's catch block, but that test is not representative of production failures.

## Why it matters
- **Observable contract is incorrect**: `Success` always returns `true` on Graph failures; consumers (frontend, MCP tool) cannot detect degraded responses.
- **Dead test coverage**: the handler test for the error path gives false confidence — it tests a code path that production never exercises.
- **SOLID / single-level-of-abstraction**: each layer re-implements the same catch-all behaviour independently instead of establishing a clear responsibility boundary.

## Suggested fix
Choose one of these two consistent approaches (pick whichever matches the broader project convention):

**Option A — Service propagates, handler catches once:**
Remove the individual `try/catch` blocks inside `GraphService.GetGroupMembersAsync` (or convert them to `throw` after logging) so exceptions surface to `GetGroupMembersHandler`, which remains the single catch-and-convert boundary.

**Option B — Service returns a Result/discriminated union:**
Change `IGraphService.GetGroupMembersAsync` to return `Result<List<UserDto>>` (or equivalent) so the empty-list-on-error implicit contract becomes explicit in the type signature, and the handler branches on success/failure without relying on exceptions.

Either option makes "empty group" and "fetch error" distinguishable, and removes the dead catch in the handler.

---
_Filed by daily arch-review routine on 2026-06-05._