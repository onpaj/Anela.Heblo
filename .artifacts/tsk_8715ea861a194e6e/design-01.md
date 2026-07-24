# Design: Document `UnauthorizedAccessException` on `IGraphService.GetGroupMembersAsync`

## Scope note

This change has no user interface, no new component, and no data schema — it is a single XML doc comment addition on an existing interface method, with zero effect on runtime behavior, method signature, or types. The UX/UI and Data schemas sections are omitted accordingly (per the design-step convention of not writing placeholders for sections that don't apply).

## Component design

**Component:** `IGraphService` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs`)

**Responsibility (unchanged):** Contract for Microsoft Graph access in the UserManagement module — group member lookup, user search, app role member lookup. This design touches only the documented *contract* of one method; the component's responsibility, method signatures, and implementations are unchanged.

**Interface change — `GetGroupMembersAsync`:**

Current doc block (lines 7–12) documents two exceptions in the order they'd naturally occur (auth failure, then Graph API error). Add a third `<exception>` entry for `UnauthorizedAccessException`, placed after the two existing ones since it triggers at the API-response level (Graph returned a 403), the same tier as `GraphServiceException`:

```csharp
public interface IGraphService
{
    /// <exception cref="GraphServiceAuthException">
    /// Thrown when token acquisition fails (MSAL auth error).
    /// </exception>
    /// <exception cref="GraphServiceException">
    /// Thrown when Microsoft Graph returns an OData error response.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the caller lacks permission to read the specified group.
    /// </exception>
    Task<List<UserDto>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken = default);
    Task<List<UserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAppRoleMembersAsync(string appRoleValue, CancellationToken cancellationToken = default);
}
```

**No changes to:**
- `GraphService.GetGroupMembersAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs:179–183`) — the re-throw of `UnauthorizedAccessException` already exists and is correct; the doc now simply names it.
- `GetGroupMembersHandler` (`backend/src/Anela.Heblo.Application/Features/UserManagement/GetGroupMembersHandler.cs`) — its existing `catch (UnauthorizedAccessException) → ErrorCodes.Forbidden` mapping is validated, not modified, by this change.
- `GraphArticleUserResolver` — its existing comment about `UnauthorizedAccessException` propagation stays accurate; no edit needed there since it's not part of the interface contract.

**Consumers unaffected:** `SearchUsersAsync` and `GetAppRoleMembersAsync` are explicitly out of scope (per plan) — they swallow exceptions internally and return empty lists, so they have no undocumented propagation behavior to fix.

## Data schemas

Not applicable — no request/response DTOs, events, or persistence are touched. `UserDto` and all exception types (`GraphServiceAuthException`, `GraphServiceException`, `UnauthorizedAccessException`) already exist and are unchanged.

## Verification

- `dotnet build` succeeds (XML doc comments are validated at compile time when doc-file generation is enabled).
- `dotnet format` produces no diff.
- No test changes expected — this is a documentation-only edit; the UserManagement test suite should pass unmodified as a regression check.
