# Merge conflict resolution — PR #3735

## Conflict

Merging `origin/main` into this PR's branch produced one conflict:

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`

## Cause

- **This branch (HEAD)** — PR #3735 fixes `GraphService.GetAppRoleMembersAsync` error handling and adds an xmldoc `<exception>` block for `GraphServiceAuthException` above that method's declaration in `IGraphService`.
- **origin/main (MERGE_HEAD)** — PR #3728 (arch-review cleanup) removed `SearchUsersAsync` from `IGraphService` entirely, having found it had no consumers (dead interface method).

Both changes touched the same region of the interface (the lines between `GetGroupMembersAsync` and `GetAppRoleMembersAsync`), producing a textual conflict even though the changes are logically independent.

## Resolution

Kept `origin/main`'s removal of `SearchUsersAsync` (confirmed dead: no implementations or call sites exist anywhere in `backend/src` or `backend/test` other than the interface declaration itself) and kept this branch's new `GetAppRoleMembersAsync` xmldoc. Result:

```csharp
public interface IGraphService
{
    /// <exception cref="GraphServiceAuthException">
    /// Thrown when token acquisition fails (MSAL auth error).
    /// </exception>
    /// <exception cref="GraphServiceException">
    /// Thrown when Microsoft Graph returns an OData error response.
    /// </exception>
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);

    /// <exception cref="GraphServiceAuthException">
    /// Thrown when token acquisition fails (MSAL auth error).
    /// </exception>
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
```

## Verification

- Grepped the full tree for conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`) — none remain.
- Grepped for `SearchUsersAsync` across `backend/src` and `backend/test` — zero implementations or callers, confirming the interface method's removal (main's side) doesn't break any other code that was expecting it. Only remaining references are historical doc/artifact files recording that removal (e.g. `docs/superpowers/plans/...`, `.artifacts/tsk_73a0d5f588a54964/*`).
- `GraphService.cs` and `MockGraphService.cs` (both already merged cleanly, no conflicts) implement only `GetGroupMembersAsync` and `GetAppRoleMembersAsync` — consistent with the resolved interface.
- Staged the resolved file (`git add`). No other files had unmerged paths (`git status` shows a clean merge otherwise).
- Could not run `dotnet build`/`dotnet format` — no .NET SDK available in this sandbox (consistent with prior steps' notes). CI must run the standard build/test/format gate.
