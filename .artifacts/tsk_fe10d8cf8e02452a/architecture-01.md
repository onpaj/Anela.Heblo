# Architecture: Stop `GetAppRoleMembersAsync` from swallowing Graph failures

## Verdict

The plan and design are sound for everything confined to the `UserManagement` module
(`GraphService`, `IGraphService`, their tests). I verified every file, line number, and existing
test referenced in both documents against the current source — all of it matches reality exactly
(`GraphService.cs:268-453`, `GetGroupMembersAsync:105-190` as the precedent, `IGraphService.cs`,
`GraphServiceTests.cs`, existing `GraphServiceAuthException`/`GraphServiceException` types).

**One change is required before implementation starts:** design component 3
(`GetEntraAccessUsersHandler` catching `GraphServiceAuthException`/`GraphServiceException`
directly) crosses a module boundary this codebase explicitly forbids and enforces via CI. Section
below explains why and gives the corrected design. Everything else in the plan/design can proceed
as written.

## Alignment with existing patterns

`docs/architecture/development_guidelines.md` ("Cross-Module Communication Example:
`ILeafletKnowledgeSource`") states the rule directly:

> When module A needs read-only access to data in module B, the dependency must **invert**: the
> consumer owns the contract, the provider implements an adapter.

This codebase already follows that rule for `Authorization` ↔ `UserManagement`:

- **Consumer contract**: `Authorization.Contracts.IEntraAccessUserSource` (`Authorization`-owned,
  `backend/src/Anela.Heblo.Application/Features/Authorization/Contracts/IEntraAccessUserSource.cs`) —
  a 5-line interface with zero exception documentation today.
- **Provider adapter**: `UserManagement.Infrastructure.EntraAccessUserSourceAdapter`
  (`UserManagement`-owned) — implements that contract, is the *only* class in the codebase that is
  allowed to know both `IGraphService` (UserManagement) and `IEntraAccessUserSource`
  (Authorization).
- **Consumer**: `GetEntraAccessUsersHandler`, which lives under
  `Anela.Heblo.Application.Features.Authorization.UseCases` and today imports nothing outside
  `Authorization.Contracts`.

`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces this exact pattern
with a reflection-based test for ~15 other module pairs (Leaflet→KnowledgeBase,
Purchase→Catalog, Catalog→Manufacture, etc.) — each with an explicit, commented allowlist for any
pre-existing leak. There is currently no rule entry for `Authorization → UserManagement` because
today there is nothing to check: the handler references only `Authorization.Contracts`.

**Design component 3, as written, breaks this.** It has `GetEntraAccessUsersHandler` (Authorization)
`catch (GraphServiceAuthException ex)` / `catch (GraphServiceException ex)` — both types live in
`Anela.Heblo.Application.Features.UserManagement.Contracts`. That is a consumer module reaching
into a provider module's exception types, the identical shape of coupling the `ModuleBoundariesTests`
suite exists to catch and that the `ILeafletKnowledgeSource` pattern was written to prevent. It
would compile and pass today only because no boundary test currently watches this pair — not
because it's architecturally sound. It is exactly the kind of undetected leak the allowlist
mechanism was built to surface and track, and the next `ModuleBoundariesTests` addition (or a
future arch-review pass) would flag it.

Note this is *not* a problem for `GetGroupMembersHandler` catching `GraphServiceAuthException` —
that handler lives under `Application.Features.UserManagement.UseCases`, the same module that owns
the exception. No boundary is crossed there. The two cases look symmetric on the surface (both
"handler catches Graph exception") but are architecturally different because of which module each
handler lives in.

## Proposed architecture (correction to design component 3 and 4)

Move the exception translation into `EntraAccessUserSourceAdapter` — it is already the designated
adapter boundary between the two modules, already imports `IGraphService`, and is the one place in
the codebase where referencing `UserManagement.Contracts` types is architecturally correct.

**1. New Authorization-owned exceptions**, in `Authorization/Contracts/` alongside
`IEntraAccessUserSource.cs` (mirrors the shape of `GraphServiceAuthException`/`GraphServiceException`,
scoped to what the Authorization module needs to distinguish — auth/config failure vs. everything
else):

```csharp
namespace Anela.Heblo.Application.Features.Authorization.Contracts;

/// <summary>
/// Thrown by <see cref="IEntraAccessUserSource"/> implementations when the underlying
/// identity provider could not be reached due to an authentication/configuration failure.
/// </summary>
public sealed class EntraAccessSourceAuthException : Exception
{
    public EntraAccessSourceAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown by <see cref="IEntraAccessUserSource"/> implementations for unexpected failures
/// that are not an auth/configuration problem.
/// </summary>
public sealed class EntraAccessSourceException : Exception
{
    public EntraAccessSourceException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

**2. `EntraAccessUserSourceAdapter` gains the translation try/catch** (this replaces design
component 4's "no code change" — that was correct only under the flawed component-3 design):

```csharp
public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
{
    List<UserDto> users;
    try
    {
        users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
    }
    catch (GraphServiceAuthException ex)
    {
        throw new EntraAccessSourceAuthException(
            $"Failed to resolve Entra Base role members: {ex.Message}", ex);
    }
    catch (GraphServiceException ex)
    {
        throw new EntraAccessSourceException(
            $"Failed to resolve Entra Base role members: {ex.Message}", ex);
    }

    return users
        .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
        .ToList();
}
```

This class already imports both `Authorization.Contracts` and `UserManagement.Services`/`Contracts`
— it is the one place where that's legitimate. No other file needs to know both vocabularies.

**3. `IEntraAccessUserSource.GetBaseMembersAsync` documents the new contract** (same xmldoc pattern
as `IGraphService`):

```csharp
public interface IEntraAccessUserSource
{
    /// <exception cref="EntraAccessSourceAuthException">
    /// Thrown when the underlying identity provider auth/configuration fails.
    /// </exception>
    /// <exception cref="EntraAccessSourceException">
    /// Thrown for other unexpected failures resolving Base role members.
    /// </exception>
    Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct);
}
```

**4. `GetEntraAccessUsersHandler` catches only Authorization-owned types** — this is now identical
in *shape* to design component 3, just retargeted to the new types, and requires zero import of
anything under `UserManagement`:

```csharp
public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
{
    private readonly IEntraAccessUserSource _source;
    private readonly ILogger<GetEntraAccessUsersHandler> _logger;

    public GetEntraAccessUsersHandler(IEntraAccessUserSource source, ILogger<GetEntraAccessUsersHandler> logger)
    {
        _source = source;
        _logger = logger;
    }

    public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
    {
        try
        {
            var users = await _source.GetBaseMembersAsync(ct);
            return new GetEntraAccessUsersResponse
            {
                Users = users.Select(u => new EntraUserDto
                {
                    EntraObjectId = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                }).OrderBy(u => u.DisplayName).ToList(),
            };
        }
        catch (EntraAccessSourceAuthException ex)
        {
            _logger.LogError(ex, "Failed to resolve Entra access users");
            return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ConfigurationError };
        }
        catch (EntraAccessSourceException ex)
        {
            _logger.LogError(ex, "Failed to resolve Entra access users");
            return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ExternalServiceError };
        }
    }
}
```

**5. `GraphService.GetAppRoleMembersAsync` / `IGraphService` changes stay exactly as designed**
(plan FR-1–FR-4, design component 1–2): narrow the token-acquisition catch to `MsalException` →
`GraphServiceAuthException`; remove the outer catch-all; keep the five FR-3-preserved
non-2xx-returns-`[]` branches untouched; add the `GraphServiceAuthException` xmldoc to
`IGraphService`. None of this touches module boundaries — it's entirely internal to
`UserManagement`.

## Data flow (revised)

```
GraphService.GetAppRoleMembersAsync
  ├─ MsalException on token acquisition        → throws GraphServiceAuthException   (UserManagement.Contracts)
  ├─ anything else unhandled (JSON, transport)  → propagates as-is                   (unchanged types)
  └─ 5 preserved branches (config/SP/batch etc) → returns [] (unchanged, FR-3)
        │
        ▼
EntraAccessUserSourceAdapter.GetBaseMembersAsync   (UserManagement.Infrastructure — the ONLY class
  ├─ catch GraphServiceAuthException → throw EntraAccessSourceAuthException  (Authorization.Contracts)
  ├─ catch GraphServiceException     → throw EntraAccessSourceException      (Authorization.Contracts)
  └─ else → maps List<UserDto> to List<EntraAccessUserRecord>                that spans both vocabularies)
        │
        ▼
GetEntraAccessUsersHandler.Handle   (Authorization.UseCases — imports ONLY Authorization.Contracts)
  ├─ catch EntraAccessSourceAuthException → Success=false, ErrorCode=ConfigurationError
  ├─ catch EntraAccessSourceException     → Success=false, ErrorCode=ExternalServiceError
  └─ else → Success=true, Users=[...]
```

This gives the exact same HTTP-observable behavior the plan specified in FR-5 (auth failure →
`ConfigurationError`, other Graph failure → `ExternalServiceError`, legitimate empty → success) —
the correction is purely about *where* the translation happens, not what the API contract ends up
looking like.

## Implementation guidance — file list and order

1. `GraphService.cs` — FR-1 (narrow token-acquisition catch), FR-2 (remove outer catch-all). No
   architecture concerns; matches `GetGroupMembersAsync` precedent line-for-line.
2. `IGraphService.cs` — FR-4 xmldoc addition.
3. **New file** `Authorization/Contracts/EntraAccessSourceExceptions.cs` (or two files, match
   existing convention — `UserManagement.Contracts` uses one file per exception type, so prefer
   `EntraAccessSourceAuthException.cs` + `EntraAccessSourceException.cs` for consistency).
4. `IEntraAccessUserSource.cs` — add the two `<exception>` xmldoc blocks.
5. `EntraAccessUserSourceAdapter.cs` — add the try/catch translating Graph exceptions to
   Authorization-owned ones. **This file changes** (design's "no code change" claim for component 4
   no longer holds).
6. `GetEntraAccessUsersHandler.cs` — add constructor `ILogger<GetEntraAccessUsersHandler>` param
   and the two-clause catch, catching only the new Authorization-owned types.
7. Tests:
   - `GraphServiceTests.cs` — per plan step 4 (rewrite the batch-failure test's outer-catch
     assertion per Open Question 1, add token-acquisition-failure test, keep FR-3 preserved-branch
     tests).
   - **New**: `EntraAccessUserSourceAdapterTests.cs` (or extend an existing one if present) —
     assert `GraphServiceAuthException` from the mocked `IGraphService` becomes
     `EntraAccessSourceAuthException`, and `GraphServiceException` becomes
     `EntraAccessSourceException`. This test did not exist before and is new scope introduced by
     this correction — flag it in the dev step's task list.
   - `GetEntraAccessUsersHandlerTests.cs` — per plan FR-5 acceptance, but mock
     `IEntraAccessUserSource.GetBaseMembersAsync` to throw `EntraAccessSourceAuthException` /
     `EntraAccessSourceException` (not the Graph-owned types).

## Risks and mitigations

- **Risk**: dev step implements design component 3 literally (catching Graph types in the
  Authorization handler) because it's simpler and the plan/design docs say so. **Mitigation**: this
  document supersedes design component 3/4 on this point; the dev step must follow the corrected
  version above. Consider adding a `ModuleBoundariesTests` rule for `Authorization → UserManagement`
  with an empty allowlist as part of this change — it costs one more `ModuleBoundaryRule` entry and
  permanently pins the fix, consistent with how every other module pair in this codebase is guarded.
  Recommended, not required for this task's scope, but cheap enough to include.
- **Risk**: two new near-identical exception types (`EntraAccessSourceAuthException`/
  `EntraAccessSourceException`) feel like boilerplate for a single call site. **Mitigation**:
  this mirrors the exact granularity already chosen for `GraphServiceAuthException`/
  `GraphServiceException` and for `GetGroupMembersResponse`'s `ConfigurationError`/
  `ExternalServiceError` split — collapsing to one exception type would lose the
  auth-vs-other distinction the plan explicitly wants surfaced (FR-5's stated goal). Keep both.
- **Risk** (carried over from the plan, unchanged): removing the outer catch-all in
  `GetAppRoleMembersAsync` also stops swallowing exceptions bubbling up from the nested
  `GetGroupMembersAsync` call at line 383 (group-expansion). After the fix, a `GraphServiceException`/
  `GraphServiceAuthException`/`UnauthorizedAccessException` from that nested call now propagates all
  the way through the adapter's new translation layer too — verify the adapter's catch clauses
  handle `UnauthorizedAccessException` deliberately (it isn't currently one of the two Graph
  exception types). **Recommendation**: leave `UnauthorizedAccessException` unhandled in the
  adapter — it will propagate past both catch clauses to `GetEntraAccessUsersHandler` unhandled,
  then to the API's existing `UnauthorizedAccessExceptionHandler` (`backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs`),
  which already maps it to a 401 — this is correct behavior and requires no extra code, but the dev
  step should add one test proving it (mock `IGraphService.GetAppRoleMembersAsync` inside the group-
  expansion path to throw `UnauthorizedAccessException`, assert it's unhandled by the adapter).
- **Risk**: `Success: true` → `Success: false` on this endpoint for the auth-failure case is a
  behavior change on `GET /api/admin/authorization/entra-users`. **Mitigation**: this is the entire
  point of the fix and was already flagged/accepted in the plan's Interfaces section — no new
  concern, just confirming it's intentional and the only consumer (`EntraMemberSearch.tsx`) doesn't
  currently branch on `.success`, so the failure mode goes from "silently wrong data" to "visibly
  empty list + logged/network-visible error," a strict improvement even without a FE change.

## Prerequisites before implementation

None blocking — `GraphServiceAuthException`, `GraphServiceException`, and the
`ModuleBoundariesTests` pattern to follow all already exist in the codebase. The only new
prerequisite is the decision this document makes: exception translation belongs in
`EntraAccessUserSourceAdapter`, not in `GetEntraAccessUsersHandler`.
